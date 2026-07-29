using TwilightBoxart.Core.Art;

namespace TwilightBoxart.Core.Index;

/// <summary>
/// How a No-Intro name was matched to a libretro-thumbnails file name. Lower is better, and the
/// resolver never descends a tier while a better one still has a candidate: a cover for the right
/// region always beats a cover for the wrong one.
/// </summary>
public enum ArtMatchTier
{
    None = 0,

    /// <summary>The file name is the No-Intro name. No interpretation involved.</summary>
    Exact = 1,

    /// <summary>Same name once language tags are dropped from both sides. Same dump, two vintages of naming.</summary>
    Language = 2,

    /// <summary>Same title, and the region tags overlap.</summary>
    Region = 3,

    /// <summary>Same title, and the regions are at least in the same family (Japan and Asia, USA and Europe).</summary>
    RegionFamily = 4,

    /// <summary>Same title, different part of the world. Last resort, and the only tier that can pick a wrong cover.</summary>
    AnyRegion = 5
}

/// <summary>A resolved libretro-thumbnails file name (without the <c>.png</c>) and how we got there.</summary>
public sealed record ArtMatch(string FileName, ArtMatchTier Tier);

/// <summary>
/// Matches No-Intro names against one console's libretro-thumbnails file listing.
/// <para>
/// It exists because the two naming schemes have drifted apart. The thumbnail repository is a decade
/// of per-file community accretion that follows no single No-Intro vintage, while our index tracks
/// current DATs, so an exact string comparison, which is all the client ever did, misses 42% of the
/// corpus and reports every miss as "this game has no box art". Measured against the full listings on
/// 2026-07-29: 20,522 of 48,858 rows missed, and the tiers below recover 6,641 of them.
/// </para>
/// <para>
/// The drift runs both ways, which is why no keyed mapping can fix this and none is published: some
/// thumbnails are named from an older DAT than ours and some from a newer one. libretro's own
/// hash-keyed .rdb files were measured as an alternative and resolve 34 of the 20,522, because their
/// names ARE our names. Name matching is the only route, and per-title the ceiling is about 76%.
/// </para>
/// <para>
/// All of this runs at build time. The alternative, retrying variant names per request, would mean
/// tens of thousands of speculative 404s per library scan and matching logic that could only be
/// tested against the live network.
/// </para>
/// </summary>
public sealed class ThumbnailIndex
{
    private readonly HashSet<string> _exact;
    private readonly Dictionary<string, List<string>> _byLanguageStripped;
    private readonly Dictionary<string, List<string>> _byTitle;

    /// <summary>File names as published, without the <c>.png</c> suffix.</summary>
    public ThumbnailIndex(IEnumerable<string> fileNames)
    {
        _exact = new HashSet<string>(StringComparer.Ordinal);
        _byLanguageStripped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // TitleKey is already lower-cased and stripped, so ordinal is the right comparer here.
        _byTitle = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var name in fileNames)
        {
            if (!_exact.Add(name))
            {
                continue;
            }

            Add(_byLanguageStripped, RomName.StripLanguageTags(name), name);
            Add(_byTitle, RomName.TitleKey(name), name);
        }
    }

    public int Count => _exact.Count;

    /// <summary>
    /// Resolves a canonical No-Intro name, or null when this console genuinely has no cover for it.
    /// The returned name is the file as published, so the caller still applies its own URL escaping.
    /// </summary>
    public ArtMatch? Resolve(string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            return null;
        }

        // libretro's own illegal-character rule produced these file names, so apply it before any
        // comparison: "Asterix & Obelix (Europe)" is stored as "Asterix _ Obelix (Europe)".
        var name = LibRetroArtSource.SanitizeName(canonicalName);

        if (_exact.Contains(name))
        {
            return new ArtMatch(name, ArtMatchTier.Exact);
        }

        // A language tag the thumbnail predates carries no identity: "Fidgetts, The (Japan) (En)" and
        // "Fidgetts, The (Japan)" are one dump under two vintages of naming. Two conditions guard it.
        // The collapse must be unambiguous, since files differing only by language tag are different
        // releases. And the candidate must not declare a DIFFERENT language: dropping a tag the other
        // side never had is a naming difference, but "(Fr)" standing in for "(Ja)" is a swap.
        if (_byLanguageStripped.TryGetValue(RomName.StripLanguageTags(name), out var sameDump) &&
            sameDump.Count == 1)
        {
            var theirs = RomName.Languages(sameDump[0]);
            if (theirs.Count == 0 || theirs.SetEquals(RomName.Languages(name)))
            {
                return new ArtMatch(sameDump[0], ArtMatchTier.Language);
            }
        }

        return ResolveByTitle(name);
    }

    /// <summary>
    /// Last resort: the bare title matches but the tags do not. Only ever reached for the long tail
    /// (pirate carts, prototypes, Evercade and Mega Drive Mini reissues, DS Download Station demos),
    /// because anything with a straight retail release matched exactly two tiers ago.
    /// </summary>
    private ArtMatch? ResolveByTitle(string name)
    {
        var title = RomName.TitleKey(name);
        if (!_byTitle.TryGetValue(title, out var candidates))
        {
            return null;
        }

        // A dump flag ("[b]", "[tr en ...]") or a prototype marker means the file is not a picture of
        // the box, so it can never stand in for one. The NES repository is full of both.
        var usable = candidates.Where(RomName.IsRetailRelease).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        var wanted = RomName.Regions(name);
        var best = Pick(usable, wanted, ArtMatchTier.Region) ??
                   Pick(usable, wanted, ArtMatchTier.RegionFamily);
        if (best is not null)
        {
            return best;
        }

        // Nothing in the right part of the world. Handing over a foreign cover is normally still the
        // right call, but not for the handful of titles where the regional releases are different
        // games: "Super Mario Bros. 2" is Lost Levels in Japan and a reskinned Doki Doki Panic in the
        // USA, and they have nothing to do with each other.
        return DivergentTitles.Contains(title) ? null : Pick(usable, wanted, ArtMatchTier.AnyRegion);
    }

    /// <summary>
    /// Best candidate at one tier, or null when none qualifies. Ties break towards a plain retail
    /// release and then towards the shortest name, which is the one carrying the fewest qualifiers.
    /// </summary>
    private static ArtMatch? Pick(List<string> candidates, HashSet<string> wanted, ArtMatchTier tier)
    {
        string? best = null;
        var bestIsWorld = false;
        foreach (var candidate in candidates)
        {
            var regions = RomName.Regions(candidate);
            if (!Qualifies(regions, wanted, tier))
            {
                continue;
            }

            var isWorld = regions.Contains("world");
            if (best is null || Prefer(candidate, isWorld, best, bestIsWorld))
            {
                best = candidate;
                bestIsWorld = isWorld;
            }
        }

        return best is null ? null : new ArtMatch(best, tier);
    }

    /// <summary>
    /// Note a "(World)" cover does not satisfy <see cref="ArtMatchTier.Region"/> for a "(USA)" query:
    /// the tags do not overlap, so it enters one tier lower via the family rule, which treats World as
    /// belonging to both halves. That ordering is deliberate. A genuine USA cover should win over a
    /// World one, and World still beats every foreign alternative.
    /// </summary>
    private static bool Qualifies(HashSet<string> regions, HashSet<string> wanted, ArtMatchTier tier)
    {
        return tier switch
        {
            ArtMatchTier.Region => regions.Overlaps(wanted),
            ArtMatchTier.RegionFamily => RomName.SharesRegionFamily(regions, wanted),
            _ => true
        };
    }

    /// <summary>
    /// Whether the candidate should displace the incumbent. "World" covers more of the library than any
    /// single-region cover, so it wins a tie; otherwise the shortest name wins, being the one carrying
    /// the fewest qualifiers.
    /// </summary>
    private static bool Prefer(string candidate, bool candidateIsWorld, string incumbent, bool incumbentIsWorld)
    {
        return candidateIsWorld != incumbentIsWorld
            ? candidateIsWorld
            : candidate.Length < incumbent.Length;
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string value)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(value);
    }

    /// <summary>
    /// Bare titles whose regional releases are genuinely different games, so a cross-region cover
    /// would be wrong rather than merely imperfect. Only consulted by
    /// <see cref="ArtMatchTier.AnyRegion"/>: every other tier has already agreed on the region.
    /// <para>
    /// Deliberately tiny. This is a seam for known mistakes, not a place to enumerate every title that
    /// looks risky, and an entry belongs here only once a wrong cover has actually been observed.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> DivergentTitles = new(StringComparer.Ordinal)
    {
        RomName.TitleKey("Super Mario Bros. 2")
    };
}
