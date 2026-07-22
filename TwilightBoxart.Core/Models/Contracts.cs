using System.Globalization;

namespace TwilightBoxart.Core.Models;

/// <summary>
/// How a ROM was matched. Ordered by descending confidence; see the identification ladder
/// (serial is free and exact, filename is the last resort).
/// </summary>
public enum MatchMethod
{
    None = 0,

    /// <summary>Title ID read straight out of the ROM header (DS/DSi 97.8%/99.3%, GBA 87.7%).</summary>
    HeaderSerial,

    /// <summary>CRC32 taken from the archive header, no decompression needed.</summary>
    Crc32,

    /// <summary>SHA-1 over the whole ROM. Only for loose files on consoles with no usable serial.</summary>
    Sha1,

    /// <summary>Fuzzy name match against the index (FTS5 trigram).</summary>
    Filename,
}

public enum BoxartBorderStyle
{
    None = 0,
    Line,
    NintendoDsi,
    Nintendo3Ds,
}

/// <summary>
/// What a client knows about a ROM before the server identifies it. Every field is optional except
/// <see cref="FileName"/>; clients supply whatever they could obtain cheaply.
/// </summary>
public sealed record RomFingerprint
{
    /// <summary>The ROM's own filename (the entry name inside an archive, NOT the archive name).</summary>
    public required string FileName { get; init; }

    /// <summary>CRC32 of the uncompressed ROM, normally free from the archive header.</summary>
    public uint? Crc32 { get; init; }

    /// <summary>SHA-1 hex of the uncompressed ROM, when the client had it cheaply.</summary>
    public string? Sha1 { get; init; }

    /// <summary>
    /// First bytes of the ROM. 512 covers every front-loaded header, ending exactly at the Mega
    /// Drive header's last byte (0x1FF); see <c>IRomProbe.HeaderBytesWanted</c>.
    /// </summary>
    public byte[]? Header { get; init; }

    /// <summary>Uncompressed size in bytes, when known.</summary>
    public long? Size { get; init; }

    /// <summary>Client-supplied correlation id, echoed back so batch responses can be matched up.</summary>
    public string? Tag { get; init; }
}

/// <summary>The outcome of identification: enough to build a canonical, cacheable art URL.</summary>
public sealed record RomIdentity
{
    public required ConsoleType ConsoleType { get; init; }

    /// <summary>
    /// The art key: the 4-char title id for serial-bearing platforms ("ASME"), otherwise a 16-hex
    /// digest of the canonical No-Intro name. Stable, and safe in a URL path.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Title id / game code read from the header, when present.</summary>
    public string? Serial { get; init; }

    /// <summary>Internal title from the ROM header, when present.</summary>
    public string? Title { get; init; }

    /// <summary>Canonical No-Intro name, when the index matched.</summary>
    public string? CanonicalName { get; init; }

    /// <summary>Region character from the header (NDS/DSi byte 0x0F), when present.</summary>
    public char? RegionId { get; init; }

    public required MatchMethod MatchMethod { get; init; }

    public string? Tag { get; init; }

    public bool IsMatched => MatchMethod != MatchMethod.None && Key.Length > 0;

    /// <summary>
    /// Server-relative URL of this title's box art, e.g. <c>/v2/art/nds/ASME.png</c>; null until
    /// matched. Clients append render parameters and follow it verbatim. The URL's structure is
    /// deliberately the server's private business: a shipped client that echoes this path can never
    /// hold a stale copy of the route scheme, which matters because clients in the wild do not
    /// update - v0.7 installs were still calling the retired endpoint six years on.
    /// </summary>
    public string? ArtPath =>
        IsMatched ? $"/v2/art/{ConsoleType.Slug()}/{Uri.EscapeDataString(Key)}.png" : null;
}

/// <summary>Batch identification request: the wire envelope both the server and its clients speak.</summary>
public sealed record IdentifyRequest
{
    public required IReadOnlyList<RomFingerprint> Items { get; init; }
}

/// <summary>
/// Batch identification response. <c>Items</c> is positionally aligned with the request and every
/// entry echoes its <see cref="RomFingerprint.Tag"/>, so a client can correlate without relying on order.
/// </summary>
public sealed record IdentifyResponse
{
    public required IReadOnlyList<RomIdentity> Items { get; init; }

    /// <summary>How many items produced a usable identity. Cheap signal for a client progress bar.</summary>
    public int Matched { get; init; }
}

/// <summary>Render parameters. Defaults match TWiLightMenu++'s recommended box art size.</summary>
public sealed record RenderOptions
{
    /// <summary>
    /// What a caller that asks for nothing gets: 128x115, letterboxed, no border.
    /// </summary>
    /// <remarks>
    /// A constant rather than a setting. These are TWiLightMenu++'s own recommended dimensions, and
    /// every client that has an opinion already sends it in the query string, so a server-side
    /// "default render" knob only ever changed the answer for callers who had deliberately expressed
    /// no preference, which is the one group with no reason to want it changed.
    /// </remarks>
    public static readonly RenderOptions Default = new();

    public int Width { get; init; } = 128;
    public int Height { get; init; } = 115;
    public bool KeepAspectRatio { get; init; } = true;
    public BoxartBorderStyle BorderStyle { get; init; } = BoxartBorderStyle.None;
    public int BorderThickness { get; init; } = 1;
    public uint BorderColor { get; init; } = 0xFF000000;

    /// <summary>
    /// TWiLightMenu++ allocates its box art cache as 40 slots of 0xB000 bytes and SILENTLY drops any
    /// PNG larger than that (ThemeTextures.cpp:964). The renderer must guarantee this ceiling rather
    /// than letting the client rewrite the user's settings.ini.
    /// </summary>
    public const int TwilightMaxPngBytes = 0xB000;

    /// <summary>
    /// Byte ceiling the rendered PNG must not exceed; the renderer quantizes until it fits.
    /// Defaults to <see cref="TwilightMaxPngBytes"/>.
    /// </summary>
    /// <remarks>
    /// A property rather than a hard global because the ceiling is TWiLightMenu++'s constraint, not
    /// the format's. A desktop client driving a different frontend, or anything wanting a full 256x192
    /// cover, should not silently inherit a DS cache limit, while the DS path, where exceeding it
    /// fails invisibly, still gets it by default.
    /// </remarks>
    public int MaxPngBytes { get; init; } = TwilightMaxPngBytes;

    /// <summary>Hard pixel ceiling enforced by TWiLightMenu++'s drawBoxArt (the DS screen).</summary>
    public const int MaxWidth = 256;
    public const int MaxHeight = 192;

    /// <summary>
    /// Parses a border colour in any spelling a client has ever used, with or without a leading
    /// <c>#</c> or <c>0x</c>. Six- and eight-character strings parse as hex (<c>RRGGBB</c> /
    /// <c>AARRGGBB</c> - so an all-digit string like <c>16777215</c> is hex here too); any other
    /// length of all-digit string parses as decimal, which is where legacy full-ARGB decimals land,
    /// being at least nine digits. The one parser for all of them - the web query, the v0.7 form and
    /// the desktop's stored settings - so the spellings cannot drift apart. Culture-invariant by
    /// construction.
    /// </summary>
    public static uint? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().TrimStart('#');
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain)
                ? plain
                : null;
        }

        return argb;
    }

    /// <summary>Clamps every field into a sane, non-abusive range. Always apply to untrusted input.</summary>
    public RenderOptions Normalized()
    {
        // An undefined enum member falls through every switch in the border compositor and draws
        // nothing, which reads as a rendering bug rather than a bad parameter.
        var style = Enum.IsDefined(BorderStyle) ? BorderStyle : BoxartBorderStyle.None;

        return this with
        {
            Width = Math.Clamp(Width, 1, MaxWidth),
            Height = Math.Clamp(Height, 1, MaxHeight),
            BorderStyle = style,

            // Only Line reads the colour and thickness (the sprite frames carry their own metrics),
            // so for every other style they are folded flat. Left free, bc alone is a 32-bit cache-key
            // dimension minting distinct cache entries for byte-identical renders.
            BorderThickness = style == BoxartBorderStyle.Line ? Math.Clamp(BorderThickness, 0, 5) : 0,
            BorderColor = style == BoxartBorderStyle.Line ? BorderColor : 0,

            // Floor of 4 KiB: below that quantization cannot converge and the renderer would throw on
            // every image. No upper bound: a caller that wants a large cover is not doing anything
            // unsafe, since the pixel dimensions are what actually bound the work.
            MaxPngBytes = Math.Max(MaxPngBytes, 4096),
        };
    }

    /// <summary>Stable, filesystem-safe discriminator for the render cache key.</summary>
    /// <remarks>
    /// <see cref="MaxPngBytes"/> participates: two requests identical but for the ceiling produce
    /// genuinely different bytes (different quantization), so they must not share a cache entry.
    /// </remarks>
    public string CacheDiscriminator() =>
        $"{Width}x{Height}_{(KeepAspectRatio ? "ar" : "fill")}_{BorderStyle}_{BorderThickness}_{BorderColor:X8}_{MaxPngBytes}";

    /// <summary>
    /// The options as art-URL query parameters: <c>?w=&amp;h=&amp;ar=&amp;b=&amp;bt=&amp;bc=</c>.
    /// The single encoder for this wire format on purpose: the server's Content-Location and the
    /// desktop client must emit identical strings or the same render stops converging on the same
    /// cache URL. <see cref="MaxPngBytes"/> deliberately does not travel; it is TWiLightMenu++'s
    /// hard constraint, not a client preference.
    /// </summary>
    public string ToQueryString() =>
        $"?w={Width}&h={Height}&ar={(KeepAspectRatio ? 1 : 0)}&b={BorderStyle}&bt={BorderThickness}&bc={BorderColor:X8}";
}

/// <summary>A fetched, unrendered piece of upstream art.</summary>
public sealed record ArtBlob(byte[] Data, string SourceUrl, string ContentType);

/// <summary>Resolves upstream art for an identified ROM. Implementations must not throw on a miss.</summary>
public interface IArtSource
{
    /// <summary>Lower runs first. GameTDB (title-id exact) should outrank libretro (name-based).</summary>
    int Order { get; }

    bool CanHandle(RomIdentity identity);

    /// <summary>Returns null when this source has no art for the ROM: a miss, not an error.</summary>
    Task<ArtBlob?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default);
}

/// <summary>Turns a fingerprint into an identity, walking the cost-ordered ladder.</summary>
public interface IRomIdentifier
{
    Task<RomIdentity> IdentifyAsync(RomFingerprint fingerprint, CancellationToken ct = default);

    Task<IReadOnlyList<RomIdentity>> IdentifyBatchAsync(
        IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct = default);
}

/// <summary>Read-only lookup over the generated No-Intro index.</summary>
public interface IMetadataIndex
{
    bool TryByCrc32(uint crc32, out IndexEntry entry);
    bool TryBySha1(string sha1, out IndexEntry entry);
    bool TryBySerial(ConsoleType console, string serial, out IndexEntry entry);

    /// <summary>Fuzzy name lookup (FTS5 trigram). Returns null below the confidence threshold.</summary>
    IndexEntry? SearchByName(ConsoleType console, string name);

    string Version { get; }
    int RowCount { get; }
}

public sealed record IndexEntry(
    ConsoleType ConsoleType,
    string Name,
    string? Serial,
    uint? Crc32,
    string? Sha1);

/// <summary>Renders upstream art to a TWiLightMenu-safe PNG.</summary>
public interface IBoxartRenderer
{
    /// <summary>Result is guaranteed &lt;= <see cref="RenderOptions.TwilightMaxPngBytes"/>.</summary>
    byte[] Render(ArtBlob source, RenderOptions options);
}
