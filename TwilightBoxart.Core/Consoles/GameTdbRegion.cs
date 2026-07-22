namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Maps the NDS/DSi region character (header byte 0x0F, also the 4th char of the game code) to the
/// GameTDB cover directory, as in <c>https://art.gametdb.com/ds/coverHQ/{region}/{titleId}.jpg</c>.
/// </summary>
/// <remarks>
/// <para>
/// The 2020 switch was missing four characters, and because it defaulted to "EN" without saying so,
/// the misses were invisible: 21 of the 124 detected DSi files fell through it - V (17), C (2), X (1),
/// A (1). <b>V is the standard Europe/Australia DSiWare region character</b>, so this silently affected
/// titles as ordinary as Flipnote Studio.
/// </para>
/// <para>
/// The fix is not "add more cases", it is <see cref="TryMap"/>: a caller (and a test) can now tell
/// "explicitly mapped to EN" apart from "unknown, defaulted to EN". The art layer should still fall
/// back to EN on a 404 - GameTDB does not stock every region for every title - but that fallback must
/// be a response to a real 404, not a mapping table quietly giving up.
/// </para>
/// </remarks>
public static class GameTdbRegion
{
    /// <summary>The region used when the character is unknown, and the art layer's 404 fallback.</summary>
    public const string Default = "EN";

    private static readonly Dictionary<char, string> Regions = new()
    {
        // Americas.
        ['E'] = "US", // USA
        ['T'] = "US", // USA, alternate (bilingual US/JP releases)

        // Asia.
        ['J'] = "JA", // Japan
        ['K'] = "KO", // Korea
        ['C'] = "ZH", // China (iQue DS). Missing in 2020; 2 files. GameTDB stocks few ZH covers, but a
                      // 404 here falls back to EN anyway, so mapping it can only ever win.

        // Europe / multi-region. All of these resolve to GameTDB's English cover set.
        ['O'] = "EN", // USA + Europe
        ['P'] = "EN", // Europe
        ['U'] = "EN", // Australia
        ['X'] = "EN", // Europe, alternate. Missing in 2020; 1 file.
        ['V'] = "EN", // Europe + Australia - the standard DSiWare region char. Missing in 2020; 17 files.
        ['A'] = "EN", // Region-free / Asia-English. Missing in 2020; 1 file.

        // European country-specific localisations.
        ['D'] = "DE", // German
        ['F'] = "FR", // French
        ['H'] = "NL", // Dutch
        ['I'] = "IT", // Italian
        ['R'] = "RU", // Russian
        ['S'] = "ES", // Spanish

        // Homebrew, conventionally game code "####".
        ['#'] = "HB",
    };

    /// <summary>
    /// True when <paramref name="regionId"/> is a region character we know, with its GameTDB directory.
    /// </summary>
    /// <remarks>
    /// Use this over <see cref="From"/> when you need to distinguish a deliberate EN from a defaulted
    /// one - for logging an unknown region, or for deciding not to bother with a request at all.
    /// </remarks>
    public static bool TryMap(char regionId, out string region)
    {
        // ToUpperInvariant, never ToUpper: a Turkish locale maps 'i' to a dotted capital and would miss
        // the 'I' -> IT entry entirely.
        return Regions.TryGetValue(char.ToUpperInvariant(regionId), out region!);
    }

    /// <summary>The GameTDB region directory for a region character, or <see cref="Default"/>.</summary>
    public static string From(char? regionId) =>
        regionId is { } c && TryMap(c, out var region) ? region : Default;

}
