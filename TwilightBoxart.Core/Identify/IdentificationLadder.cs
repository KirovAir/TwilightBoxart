using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TwilightBoxart.Core.Consoles;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;

namespace TwilightBoxart.Core.Identify;

/// <summary>
/// The cost-ordered identification ladder. Each rung is tried in turn and the first
/// hit wins; <see cref="RomIdentity.MatchMethod"/> records which one it was, so a caller can tell an
/// exact match from a guess.
///
/// <list type="number">
/// <item><b>Header serial</b> - free, and exact where it exists: DSi 99.3%, DS 97.8%, Mega Drive 93.3%,
/// GBA 87.7%, FDS 86.7%.</item>
/// <item><b>CRC32 to the index</b> - free too, because zip and 7z both record the CRC of the
/// *uncompressed* entry in their own headers. This is the path the entire .7z DS set takes, and it is
/// the difference between seconds and ~2.2 hours of LZMA.</item>
/// <item><b>SHA-1 to the index</b> - costs a full read, so it only ever arrives for loose files on the
/// consoles with no usable serial.</item>
/// <item><b>Filename</b> - fuzzy FTS5 trigram match. Last resort, and the only rung that can be
/// confidently wrong, so it is threshold-gated inside the index.</item>
/// </list>
///
/// Roughly 59.6% of DAT rows carry no serial at all, so misses here are structural rather than a
/// tuning problem: no reordering of these rungs moves that number. The product answer is cheap manual
/// correction, not a looser threshold.
/// </summary>
public sealed class IdentificationLadder(IMetadataIndex index, ILogger<IdentificationLadder> logger)
    : IRomIdentifier
{
    /// <summary>Bytes of the digest kept for a name-derived key: 16 hex characters.</summary>
    private const int DigestBytes = 8;

    /// <inheritdoc />
    public Task<RomIdentity> IdentifyAsync(RomFingerprint fingerprint, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Identify(fingerprint));
    }

    /// <summary>
    /// Identifies a batch, collapsing duplicate fingerprints to one walk of the ladder.
    /// </summary>
    /// <remarks>
    /// Duplicates are common in practice: the same ROM under two names, or a client re-sending a page.
    /// No connection work happens per item: <see cref="SqliteMetadataIndex"/> holds its connections and
    /// compiled statements open for its own lifetime, so a batch of 18,000 costs 18,000 prepared-statement
    /// executions and nothing else.
    /// </remarks>
    public Task<IReadOnlyList<RomIdentity>> IdentifyBatchAsync(
        IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct = default)
    {
        var results = new RomIdentity[fingerprints.Count];
        var seen = new Dictionary<string, RomIdentity>(fingerprints.Count, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < fingerprints.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var fingerprint = fingerprints[i];
            var key = DedupeKey(fingerprint);
            if (!seen.TryGetValue(key, out var identity))
            {
                identity = Identify(fingerprint);
                seen[key] = identity;
            }

            // Tag is the caller's own correlation id, so it is never shared between two request items
            // even when they resolved to the same identity.
            results[i] = identity.Tag == fingerprint.Tag ? identity : identity with { Tag = fingerprint.Tag };
        }

        return Task.FromResult<IReadOnlyList<RomIdentity>>(results);
    }

    private RomIdentity Identify(RomFingerprint fingerprint)
    {
        var header = fingerprint.Header;
        var detection = header is { Length: > 0 } ? ConsoleDetector.Detect(header) : HeaderDetection.None;

        // Rung 1: the serial in the ROM's own header.
        if (detection.Serial is { Length: > 0 } serial)
        {
            foreach (var console in Partitions(detection))
            {
                if (index.TryBySerial(console, serial, out var bySerial))
                {
                    return Matched(MatchMethod.HeaderSerial, detection, bySerial, fingerprint.Tag);
                }
            }

            // A miss here does NOT end rung 1, but neither may it return yet. See TrySerialKey below.
        }

        // Rung 2: CRC32 out of the container header.
        // Zero is not a CRC. 7z reports 0 for both "absent" and "genuinely zero" because the IsAvailable
        // flag sits on an internal descriptor SharpCompress never surfaces, so trusting it would pin
        // every CRC-less 7z entry onto whichever DAT row happens to hash to zero. Treat it as unknown
        // and fall through. The probe should already have nulled it; this is the
        // backstop, because the cost of being wrong is a silently mis-identified ROM.
        if (fingerprint.Crc32 is { } crc32 && crc32 != 0)
        {
            if (index.TryByCrc32(crc32, out var byCrc))
            {
                return Matched(MatchMethod.Crc32, detection, byCrc, fingerprint.Tag);
            }

            // The double lookup. No-Intro and real-world files disagree about whether a container header
            // (16 bytes for iNES, 512 for an SNES copier header) is inside the hashed region, so the
            // same ROM has two legitimate CRCs and we cannot know which one the DAT recorded. It matters
            // most for NES: 33% of the index at 0% serial coverage, so the hash is the *only* thing that
            // can identify it. We never decompressed the ROM, but CRC-32 is affine, so the headerless
            // CRC follows exactly from the whole-file CRC plus the header bytes we already hold
            // (Crc32Arithmetic): no re-read, no decompression.
            if (TryStrippedCrc32(fingerprint, detection, crc32) is { } stripped &&
                stripped != 0 &&
                index.TryByCrc32(stripped, out var byStrippedCrc))
            {
                return Matched(MatchMethod.Crc32, detection, byStrippedCrc, fingerprint.Tag);
            }
        }

        // Rung 3: SHA-1.
        // No equivalent of the CRC trick exists here: SHA-1 is not affine, so a headerless digest can
        // only be had by hashing the bytes again. If that turns out to matter for loose .nes files, the
        // client is the place to send both digests, not this rung.
        if (fingerprint.Sha1 is { Length: > 0 } sha1 && index.TryBySha1(sha1, out var bySha1))
        {
            return Matched(MatchMethod.Sha1, detection, bySha1, fingerprint.Tag);
        }

        // Rung 3b: the bare title id, once every exact rung has missed.
        if (TrySerialKey(detection, fingerprint.Tag) is { } bySerialKey)
        {
            return bySerialKey;
        }

        // Rung 4: fuzzy filename.
        // FileName is contractually the ROM's own name; for an archive that is the inner entry name and
        // never the archive's. The 2020 client sent the archive name, which disabled name matching for
        // every archived ROM.
        foreach (var console in SearchPartitions(detection, fingerprint.FileName))
        {
            if (index.SearchByName(console, fingerprint.FileName) is { } byName)
            {
                return Matched(MatchMethod.Filename, detection, byName, fingerprint.Tag);
            }
        }

        logger.LogDebug(
            "No match for {FileName} (console {Console}, crc {Crc32}).",
            fingerprint.FileName, detection.ConsoleType, fingerprint.Crc32);

        // Unmatched, but not necessarily unknown: the header may well have told us the console and the
        // internal title even though no rung produced a key. Carrying that through beats collapsing it
        // to nothing, because it is what lets a UI say "some GBA game" and offer a manual correction.
        return new RomIdentity
        {
            ConsoleType = detection.ConsoleType,
            Key = string.Empty,
            Serial = detection.Serial,
            Title = detection.Title,
            RegionId = detection.RegionId,
            MatchMethod = MatchMethod.None,
            Tag = fingerprint.Tag,
        };
    }

    /// <summary>
    /// The header's title id used as an art key directly, with no index row behind it. Null when the
    /// header carried no serial that can stand alone.
    /// </summary>
    /// <remarks>
    /// GameTDB is keyed on the 4-char title id, so a DS/DSi/GBA serial is already a complete and correct
    /// art key; the index would only have contributed a nicer display name. That is what keeps those
    /// platforms working with no index at all, and it is why this exists.
    ///
    /// <b>But it must run after the exact rungs, not instead of them.</b> A <c>TryBySerial</c> miss
    /// against a populated index is not evidence that the serial is unknown; it is very often evidence
    /// that the serial is <i>ambiguous</i>, because the builder deliberately nulls any serial naming
    /// more than one game (1,789 rows on a live build; see EntryDeduplicator). NDS "ASME" is a real
    /// example: it is carried by Super Mario 64 DS, Custom Robo Arena, EDGE and two more. Returning it
    /// as the key the moment the serial lookup missed collapsed all of them onto one art key (so one
    /// game got another's cover) while their CRC32s, which are 100% populated and exact, sat one rung
    /// below unread. Running this after CRC32 and SHA-1 restores the builder's guarantee at no cost to
    /// the index-less case, where those rungs miss anyway and this still fires.
    ///
    /// It stays <i>ahead</i> of the fuzzy filename rung on purpose: an exact id from the binary should
    /// outrank a threshold-gated trigram match, which is the one rung that can be confidently wrong.
    ///
    /// A long-form serial (Mega Drive's "00001051-00") is neither URL-shaped nor meaningful to any art
    /// source on its own, so without an index row it proves nothing and yields null here.
    /// </remarks>
    private static RomIdentity? TrySerialKey(HeaderDetection detection, string? tag)
    {
        if (detection.Serial is not { Length: > 0 } serial || !IsUsableSerialKey(serial))
        {
            return null;
        }

        return new RomIdentity
        {
            ConsoleType = detection.ConsoleType,
            Key = serial.Trim().ToUpperInvariant(),
            Serial = serial,
            Title = detection.Title,
            RegionId = detection.RegionId,
            MatchMethod = MatchMethod.HeaderSerial,
            Tag = tag,
        };
    }

    /// <summary>
    /// Derives the public art key. <b>Everything downstream depends on this being stable</b>: it is the
    /// <c>{key}</c> in <c>/v2/art/{platform}/{key}.png</c>, so it is baked into client config files, CDN
    /// caches and on-disk render caches. Changing how it is computed invalidates all of them.
    /// </summary>
    /// <remarks>
    /// Two forms, in order of preference:
    /// <list type="bullet">
    /// <item>A clean 4-character alphanumeric title id ("ASME"), uppercased. Already stable, already
    /// URL-safe, and it is what GameTDB itself is keyed on, so it doubles as the upstream lookup.</item>
    /// <item>Otherwise the first 8 bytes of SHA-256 over the canonical No-Intro name, as 16 lowercase
    /// hex characters. 64 bits over a ~42k-row index gives a collision probability around 5e-11,
    /// far below the rate at which the name matching above is simply wrong.</item>
    /// </list>
    /// The key is derived from the <i>title</i>, never from the fingerprint, so every copy of a game,
    /// zipped, loose, differently named, resolves to one cache entry and one upstream fetch.
    /// Normalisation before hashing is deliberately minimal (trim, collapse whitespace, invariant
    /// lowercase): each extra rule is another way for two builds of the server to disagree about a URL
    /// that has already been published.
    /// </remarks>
    public static string DeriveKey(string? serial, string? canonicalName)
    {
        if (serial is not null && IsUsableSerialKey(serial))
        {
            return serial.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(canonicalName))
        {
            return Digest(NormalizeForKey(canonicalName));
        }

        // A serial we could not use directly still identifies the game; prefixed so it can never
        // collide with a name that happens to normalise to the same text.
        if (!string.IsNullOrWhiteSpace(serial))
        {
            return Digest("serial:" + NormalizeForKey(serial));
        }

        return string.Empty;
    }

    /// <summary>
    /// True for a title id that can be used verbatim as a URL path segment: exactly four ASCII
    /// alphanumerics, which is the DS/DSi/GBA game-code shape GameTDB indexes on.
    /// </summary>
    private static bool IsUsableSerialKey(string serial)
    {
        var trimmed = serial.AsSpan().Trim();
        if (trimmed.Length != 4)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeForKey(string value)
    {
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.AsSpan().Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    private static string Digest(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
        return Convert.ToHexStringLower(hash[..DigestBytes]);
    }

    /// <summary>
    /// Builds the identity for a matched index row.
    /// </summary>
    /// <remarks>
    /// The index's console wins over the header's whenever it has one. That looks backwards (the header
    /// is a fact about the binary, the index column is a cataloguing decision), but the console here
    /// selects an <i>art repository</i>, and the art is organised by the No-Intro set the ROM was filed
    /// in. A Game Boy cart whose CGB flag says "Color" is still filed, and drawn, under Game Boy;
    /// preferring its header is precisely the bug that made the 2020 server query the wrong platform's
    /// art database. The header's contribution is the title and region.
    ///
    /// <para>
    /// The <b>serial</b> comes from the matched row too, and for the same reason. A header title id is
    /// not reliably unique: flashcart firmware and homebrew spoof a retail id to pass console checks, so
    /// NDS "ASME" is carried by Super Mario 64 DS, CycloDS Evolution and EDGE alike. The builder already
    /// resolves this, nulling any serial that names more than one game (1,789 rows on a live build),
    /// and taking the header's value back here would undo exactly that work: all three matched their own
    /// correct row by CRC32, then collapsed onto one art key, one cache entry and one cover.
    /// </para>
    /// <para>
    /// The cost is real but small and the right way round. Of 1,080 NDS rows with no serial, only ~182
    /// never had one in the DAT; the rest were cleared as ambiguous. So this trades a possible miss on
    /// ~182 rows for correct art on ~898, and a miss is the failure this codebase prefers: a blank
    /// cover is obviously something to fix, a plausible wrong one is not. If the ~182 ever matter, the
    /// fix belongs in the builder (mark cleared serials distinctly from absent ones), not here.
    /// </para>
    /// </remarks>
    private static RomIdentity Matched(
        MatchMethod method, HeaderDetection detection, IndexEntry entry, string? tag)
    {
        var serial = entry.Serial;

        return new RomIdentity
        {
            ConsoleType = entry.ConsoleType is not ConsoleType.Unknown
                ? entry.ConsoleType
                : detection.ConsoleType,
            Key = DeriveKey(serial, entry.Name),
            Serial = serial,
            Title = detection.Title,
            CanonicalName = entry.Name,
            RegionId = detection.RegionId,
            MatchMethod = method,
            Tag = tag,
        };
    }

    /// <summary>
    /// The console partitions to try for an exact lookup, most likely first. The second one exists
    /// because a header can be genuinely ambiguous about which No-Intro set a ROM belongs to: a
    /// DSi-enhanced hybrid (unitcode 0x02) is filed under Nintendo DS, and a Game Boy cart's CGB flag
    /// describes its silicon rather than its set. See <see cref="HeaderDetection.AlternateConsoleType"/>.
    /// </summary>
    private static IEnumerable<ConsoleType> Partitions(HeaderDetection detection)
    {
        if (detection.ConsoleType is not ConsoleType.Unknown)
        {
            yield return detection.ConsoleType;
        }

        if (detection.AlternateConsoleType is not ConsoleType.Unknown &&
            detection.AlternateConsoleType != detection.ConsoleType)
        {
            yield return detection.AlternateConsoleType;
        }
    }

    /// <summary>
    /// Partitions for the fuzzy name search. When the header established nothing, the file extension is
    /// used as a weak hint, and only as a last resort do we search every partition: "Mario Bros." exists
    /// on six platforms, and an unconstrained trigram match would pick whichever bm25 ranked first.
    /// </summary>
    private static IEnumerable<ConsoleType> SearchPartitions(HeaderDetection detection, string fileName)
    {
        var any = false;
        foreach (var console in Partitions(detection))
        {
            any = true;
            yield return console;
        }

        if (any)
        {
            yield break;
        }

        var byExtension = ConsoleFromExtension(fileName);
        if (byExtension is not ConsoleType.Unknown)
        {
            yield return byExtension;
        }

        // ConsoleType.Unknown is SqliteMetadataIndex.SearchByName's "search every partition" wildcard.
        yield return ConsoleType.Unknown;
    }

    /// <summary>
    /// Recovers the headerless CRC32 when the header carries a container prefix the DAT may not have
    /// hashed. Returns null when the inputs do not let us do it exactly.
    /// </summary>
    private static uint? TryStrippedCrc32(RomFingerprint fingerprint, HeaderDetection detection, uint crc32)
    {
        var prefixLength = detection.LeadingHeaderBytes;
        if (prefixLength <= 0 ||
            fingerprint.Header is not { } header ||
            header.Length < prefixLength ||
            fingerprint.Size is not { } size ||
            size <= prefixLength)
        {
            return null;
        }

        return Crc32Arithmetic.TryStripPrefix(crc32, header.AsSpan(0, prefixLength), size - prefixLength);
    }

    /// <summary>
    /// A last-resort console guess from the file extension, used only to narrow the fuzzy name search.
    /// Never used to build an identity; an extension is a naming convention, not evidence.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="SupportedFiles.RomExtensions"/>, the single source of truth for ROM extensions,
    /// keyed OrdinalIgnoreCase so ".NES" and ".nes" behave identically under every locale. The 2020
    /// client folded case with a culture-sensitive ToLower, which under a Turkish locale skipped every
    /// .ZIP it was handed.
    /// </remarks>
    private static ConsoleType ConsoleFromExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length == 0
            ? ConsoleType.Unknown
            : SupportedFiles.RomExtensions.GetValueOrDefault(extension);
    }

    /// <summary>
    /// Collapses fingerprints that describe the same file.
    /// </summary>
    /// <remarks>
    /// The header participates, and has to. It is redundant whenever a CRC32 or SHA-1 is present
    /// (derived from the same bytes, it cannot distinguish two items those already agree on), but when
    /// both are absent it is the <i>only</i> evidence left, and dropping it merges things that are not
    /// the same: two different loose ROMs both called "game.nds" in different folders, or one item
    /// carrying a header next to one that failed to probe. Whichever the batch happened to list first
    /// then answered for both.
    ///
    /// Hashed rather than concatenated so the key stays short for a 512-byte header, and only when there
    /// is no stronger identity, so the common path costs nothing.
    /// </remarks>
    private static string DedupeKey(RomFingerprint fingerprint)
    {
        var discriminator = fingerprint switch
        {
            { Crc32: not null } or { Sha1: not null } => null,
            { Header: { Length: > 0 } header } => Convert.ToHexStringLower(SHA256.HashData(header)[..DigestBytes]),
            _ => null,
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{fingerprint.Crc32}|{fingerprint.Sha1}|{fingerprint.Size}|{discriminator}|{fingerprint.FileName}");
    }
}
