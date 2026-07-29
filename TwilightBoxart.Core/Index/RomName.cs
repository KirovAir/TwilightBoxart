using System.Buffers;
using System.Text;

namespace TwilightBoxart.Core.Index;

/// <summary>
/// Reads the parenthesised tags off a No-Intro style name: "Fidgetts, The (Japan) (En)" is the title
/// "Fidgetts, The", the region "Japan" and the language "En". Every comparison is ordinal, and the
/// vocabularies are closed lists rather than shape-matching, because a rule like "any two-letter tag
/// is a language" would quietly eat "(Rev 1)" and "(GB Compatible)" too.
/// </summary>
public static class RomName
{
    /// <summary>
    /// Drops tags that are purely language codes: "(En)", "(En,Fr,De,Es,It,Nl,Sv)", "(Pt-BR)".
    /// Everything else survives, including the region, revision and hardware tags that do carry identity.
    /// </summary>
    public static string StripLanguageTags(string name)
    {
        return Rewrite(name, tag => !IsLanguageTag(tag));
    }

    /// <summary>
    /// The languages the name declares, or empty when it declares none. Used to check that collapsing a
    /// language tag really is collapsing a naming vintage rather than swapping one release for another.
    /// </summary>
    public static HashSet<string> Languages(string name)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in Tags(name))
        {
            if (!IsLanguageTag(tag))
            {
                continue;
            }

            foreach (var part in tag.Split(',', '+'))
            {
                result.Add(part.Trim());
            }
        }

        return result;
    }

    /// <summary>The title with every "(...)" and "[...]" group removed.</summary>
    public static string BareTitle(string name)
    {
        return Rewrite(name, static _ => false, dropBrackets: true);
    }

    /// <summary>
    /// The bare title reduced to its meaningful characters, for comparing two spellings of one title.
    /// <para>
    /// Pinyin is the reason this exists. The same Chinese title is written "Yinglie Qunxiazhuan" in one
    /// DAT and "Ying Lie Qun Xia Zhuan" in the other, and there are hundreds of those. Punctuation
    /// drifts the same way ("Super Breakout" against "Super Breakout!", "I Spy - Castle" against
    /// "I Spy Castle").
    /// </para>
    /// <para>
    /// What is dropped is decoration: whitespace, hyphens, dots, apostrophes, exclamation marks. What is
    /// KEPT is anything that joins or qualifies a title, because dropping those merges different games:
    /// </para>
    /// <code>
    /// Dragon Warrior I &amp; II       vs Dragon Warrior III        (&amp;, as "_" after sanitising)
    /// Final Fantasy I, II         vs Final Fantasy III          (comma)
    /// Love Plus                   vs Love Plus+                 (plus)
    /// Korg DS-10 Synthesizer      vs Korg DS-10+ Synthesizer    (plus)
    /// </code>
    /// <para>
    /// Every one of those pairs is real and sits on the same console in the current index. Keeping the
    /// four characters costs 10 matches across the corpus and prevents 53 such collapses.
    /// </para>
    /// <para>
    /// Digits are kept for the same reason, and that is why this is an equality rather than a fuzzy
    /// match. Edit distance rates "Tomb Raider 2" against "Tomb Raider" at 0.92 and "FIFA Soccer 99"
    /// against "FIFA Soccer 97" at 0.93, but a sequel is a different game with a different box.
    /// Measured over the corpus, a 0.90 threshold matches 565 more titles and gets every numbered one
    /// of them wrong; this rule matches 395 and got none wrong in review.
    /// </para>
    /// </summary>
    public static string TitleKey(string name)
    {
        var title = BareTitle(name);
        var builder = new StringBuilder(title.Length);
        foreach (var c in title)
        {
            if (char.IsAsciiLetterOrDigit(c) || Joiners.Contains(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The regions named by the first region tag, lower-cased. Empty when the name carries none, which
    /// is common for homebrew and for the GoodNES-style names in the NES repository.
    /// </summary>
    public static HashSet<string> Regions(string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in Tags(name))
        {
            if (!IsRegionTag(tag))
            {
                continue;
            }

            foreach (var part in tag.Split(','))
            {
                var region = part.Trim();
                result.Add(RegionAliases.TryGetValue(region, out var canonical)
                    ? canonical
                    : region.ToLowerInvariant());
            }

            return result;
        }

        return result;
    }

    /// <summary>
    /// Whether two region sets sit in the same half of the world. The split is coarse on purpose: it
    /// only has to be good enough to prefer a Japanese cover for a Taiwanese pirate cart over an
    /// American one, and to stop a Japan-only release borrowing a USA box.
    /// </summary>
    public static bool SharesRegionFamily(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        // "World" belongs to both halves, so it can pair with anything.
        return left.Contains("world") || right.Contains("world") ||
               (left.Overlaps(EastAsia) && right.Overlaps(EastAsia)) ||
               (!left.Overlaps(EastAsia) && !right.Overlaps(EastAsia));
    }

    /// <summary>
    /// Whether a libretro file name looks like a picture of a released game's box, rather than of a
    /// prototype or of a dump somebody flagged. A "[b]" or "[tr en ...]" suffix is GoodNES dump-flag
    /// syntax, and the NES repository carries thousands of them alongside the No-Intro named files.
    /// </summary>
    public static bool IsRetailRelease(string fileName)
    {
        if (fileName.Contains('[', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var tag in Tags(fileName))
        {
            // Word by word, because the marker is not always first: the corpus has "Possible Proto",
            // "Auto Demo", "Taikenban Sample ROM" and "Wi-Fi Kiosk" as well as plain "(Beta)".
            foreach (var word in tag.Split(' ', '-', '/'))
            {
                if (IsNonRetailWord(word))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsNonRetailWord(string word)
    {
        if (NonRetailExact.Contains(word))
        {
            return true;
        }

        foreach (var marker in NonRetailPrefixes)
        {
            if (word.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The contents of each "(...)" group, in order, trimmed. Nested parentheses are not a thing here.</summary>
    public static IEnumerable<string> Tags(string name)
    {
        var start = -1;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '(')
            {
                start = i + 1;
            }
            else if (name[i] == ')' && start >= 0)
            {
                yield return name[start..i].Trim();
                start = -1;
            }
        }
    }

    private static bool IsLanguageTag(string tag)
    {
        if (tag.Length == 0)
        {
            return false;
        }

        // Two-game carts join each game's language list with "+": "2 in 1 - Spider-Man + Spider-Man 2
        // (USA) (En,Fr,De+En,Fr,De,Es)". Splitting on the comma alone leaves "De+En", which is not a
        // language, so the whole tag would be treated as identity-bearing and the Language tier skipped.
        foreach (var part in tag.Split(',', '+'))
        {
            if (!LanguageCodes.Contains(part.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRegionTag(string tag)
    {
        if (tag.Length == 0)
        {
            return false;
        }

        foreach (var part in tag.Split(','))
        {
            var region = part.Trim();
            if (!RegionNames.Contains(region) && !RegionAliases.ContainsKey(region))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Rebuilds a name, keeping only the tags <paramref name="keep"/> accepts.</summary>
    private static string Rewrite(string name, Func<string, bool> keep, bool dropBrackets = false)
    {
        var builder = new StringBuilder(name.Length);
        var i = 0;
        while (i < name.Length)
        {
            var c = name[i];
            if (c == '(')
            {
                var close = name.IndexOf(')', i);
                if (close < 0)
                {
                    builder.Append(name[i..]);
                    break;
                }

                if (keep(name[(i + 1)..close].Trim()))
                {
                    builder.Append(name, i, close - i + 1);
                }
                else
                {
                    TrimTrailingSpace(builder);
                }

                i = close + 1;
                continue;
            }

            if (dropBrackets && c == '[')
            {
                var close = name.IndexOf(']', i);
                if (close < 0)
                {
                    break;
                }

                TrimTrailingSpace(builder);
                i = close + 1;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString().Trim();
    }

    private static void TrimTrailingSpace(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }
    }

    /// <summary>
    /// Characters that join or qualify a title rather than decorate it. See <see cref="TitleKey"/>.
    /// </summary>
    private static readonly SearchValues<char> Joiners = SearchValues.Create("_+,&");

    /// <summary>
    /// Words meaning the file is not a retail box. Matched as prefixes so "Beta 4", "Proto 2" and
    /// "Prototype" are all covered without listing every variation.
    /// </summary>
    private static readonly string[] NonRetailPrefixes =
    [
        "Beta", "Proto", "Demo", "Sample", "Prerelease", "Pirate", "Bootleg",
        "Kiosk", "Debug", "Promo", "Preview"
    ];

    /// <summary>
    /// Words that must match exactly. "Hack" cannot be a prefix: Hacker International is a real NES
    /// publisher and the listings tag 31 genuine covers with its name.
    /// </summary>
    private static readonly HashSet<string> NonRetailExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hack"
    };

    /// <summary>
    /// No-Intro language codes. A closed list on purpose: shape-matching "two letters, capitalised"
    /// would also swallow tags that carry identity.
    /// </summary>
    private static readonly HashSet<string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "En", "Ja", "Fr", "De", "Es", "It", "Nl", "Pt", "Sv", "No", "Da", "Fi", "Zh", "Ko", "Pl",
        "Ru", "Cs", "Hu", "Tr", "El", "He", "Ar", "Ca", "Hr", "Sl", "Sk", "Uk", "Ro", "Bg", "Et",
        "Lv", "Lt", "Ga", "Cy", "Is", "Af", "Id", "Ms", "Th", "Vi", "Hi", "Fa", "Gl", "Eu", "Sq",
        "Sr", "Mk", "Be", "Ka", "Hy", "Az", "Kk", "Ur", "Bn", "Ta", "Te", "Mr", "Gu", "Kn", "Ml",
        "Pa", "Ne", "Si", "My", "Km", "Lo", "Mn", "Bs", "Mt", "Lb", "Fo", "Gd", "Br", "Co", "Oc",
        "Rm", "Fy", "Yi", "Eo", "La", "Tl", "Sw", "Zu", "Xh",
        "Pt-BR", "Pt-PT", "Zh-Hans", "Zh-Hant", "Es-XL", "En-US", "En-GB", "Fr-CA"
    };

    /// <summary>No-Intro region names, as they appear inside a region tag.</summary>
    private static readonly HashSet<string> RegionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "World", "USA", "Europe", "Japan", "Asia", "Korea", "China", "Taiwan", "Hong Kong",
        "Australia", "New Zealand", "Canada", "Brazil", "Latin America", "Mexico", "Argentina", "Peru",
        "France", "Germany", "Spain", "Italy", "Netherlands", "Belgium", "Switzerland", "Austria",
        "Sweden", "Norway", "Denmark", "Finland", "Scandinavia", "Portugal", "Greece", "Poland",
        "Russia", "Ukraine", "Czech Republic", "Hungary", "Croatia", "Serbia", "Turkey", "Israel",
        "India", "United Kingdom", "Ireland", "South Africa", "UAE", "Unknown"
    };

    /// <summary>
    /// GoodNES region codes, which libretro-thumbnails carries alongside the No-Intro named files:
    /// "Contra (1988-02)(Konami)(JP)". Roughly 2,100 NES covers are named this way, and without these
    /// every one of them reads as having no region at all and can only be reached by the cross-region
    /// tier, which is precisely the tier that should be the last resort.
    /// <para>
    /// Two- and three-letter publisher codes ("LJN", "SNK", "FCI") share the shape and must not be
    /// mistaken for regions, which is why this is a fixed list rather than a pattern.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> RegionAliases = new(StringComparer.Ordinal)
    {
        ["US"] = "usa",
        ["USA"] = "usa",
        ["EU"] = "europe",
        ["JP"] = "japan",
        ["AS"] = "asia",
        ["AU"] = "australia",
        ["BR"] = "brazil",
        ["KR"] = "korea",
        ["CN"] = "china",
        ["TW"] = "taiwan",
        ["FR"] = "france",
        ["DE"] = "germany",
        ["ES"] = "spain",
        ["IT"] = "italy",
        ["NL"] = "netherlands",
        ["SE"] = "sweden",
        ["RU"] = "russia",
        ["UK"] = "united kingdom"
    };

    /// <summary>
    /// The eastern half of the coarse region split. Everything not named here counts as western,
    /// which is what makes an untagged homebrew name pair with a western cover rather than nothing.
    /// </summary>
    private static readonly HashSet<string> EastAsia = new(StringComparer.Ordinal)
    {
        "japan", "asia", "korea", "china", "taiwan", "hong kong"
    };
}
