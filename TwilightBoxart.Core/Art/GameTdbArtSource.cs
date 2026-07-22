using Microsoft.Extensions.Logging;
using TwilightBoxart.Core.Consoles;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Art;

/// <summary>
/// GameTDB covers for Nintendo DS and DSi titles, keyed on the 4-character title id from the ROM
/// header. Runs first: a title id is an exact identifier, where the libretro path depends on a name
/// match. GameTDB is the only source of DS/DSi art, so a miss here usually means no art exists.
/// </summary>
public sealed class GameTdbArtSource(
    IHttpClientFactory httpClientFactory,
    ILogger<GameTdbArtSource> logger)
    : HttpArtSource(httpClientFactory, logger), IArtSource
{
    public const string BaseUrl = "https://art.gametdb.com/ds/";

    /// <summary>
    /// Cover variants in descending quality, tried in order. All three are JPEG.
    /// </summary>
    /// <remarks>
    /// The 2020 client's third entry was <c>coverS</c>, a path GameTDB does not serve,
    /// verified still 404 today. The small cover is plain <c>cover</c>, and it is a <b>JPEG</b>:
    /// <c>/ds/cover/US/ASME.jpg</c> answers 200 while <c>.png</c> answers 404 for every title checked.
    /// Requesting the wrong extension makes this rung unreachable, which costs nothing when a title has
    /// an HQ or M cover and costs the whole image when it only has the plain one.
    ///
    /// The <c>/ds/</c> base serves DSiWare too; there is no <c>/dsi/</c> host, also verified.
    /// </remarks>
    public static readonly IReadOnlyList<CoverVariant> Variants =
    [
        new("HQ", "jpg"),
        new("M", "jpg"),
        new("", "jpg"),
    ];

    public int Order => 0;

    protected override string SourceName => "gametdb";

    public bool CanHandle(RomIdentity identity) =>
        identity.ConsoleType is ConsoleType.NintendoDs or ConsoleType.NintendoDsi
        && IsUsableTitleId(identity.Serial);

    public async Task<ArtBlob?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default)
    {
        if (!CanHandle(identity))
        {
            return null;
        }

        var titleId = identity.Serial!;

        // The region character is the 4th character of the game code, so a header-derived identity that
        // did not carry RegionId separately still has it.
        var regionId = identity.RegionId ?? titleId[3];
        if (!GameTdbRegion.TryMap(regionId, out _))
        {
            // Worth saying out loud: an unrecognised character defaulting silently is exactly how V, C,
            // X and A stayed invisible in the 2020 client. We still try the English set below.
            logger.LogDebug(
                "gametdb: unknown region character '{RegionId}' on {TitleId}, defaulting to {Region}",
                regionId, titleId, GameTdbRegion.Default);
        }

        var region = GameTdbRegion.From(regionId);

        var blob = await TryRegionAsync(region, titleId, ct);
        if (blob is not null)
        {
            return blob;
        }

        // GameTDB only stocks localised covers for a fraction of titles, so the English set is the
        // fallback for everything. Skipped when we already asked for it.
        return string.Equals(region, GameTdbRegion.Default, StringComparison.Ordinal)
            ? null
            : await TryRegionAsync(GameTdbRegion.Default, titleId, ct);
    }

    /// <summary>
    /// Title ids go straight into a URL path, so anything outside <c>[A-Za-z0-9]</c> is rejected rather
    /// than escaped: a 4-character game code has no legitimate reason to contain anything else, and a
    /// header full of garbage should be a miss, not a request we send on someone's behalf.
    /// </summary>
    /// <remarks>
    /// This deliberately excludes homebrew, whose game code is conventionally <c>####</c>. The 2020
    /// client pasted that straight into the URL, where <c>#</c> is a fragment delimiter, so the server
    /// only ever saw the truncated path, so the homebrew region was never actually reachable. If GameTDB
    /// turns out to stock homebrew covers, escape the id rather than widening this test.
    /// </remarks>
    public static bool IsUsableTitleId(string? serial) =>
        serial is { Length: 4 } && serial.All(char.IsAsciiLetterOrDigit);

    /// <summary>Builds a GameTDB cover URL. Exposed so the shape can be asserted without a network call.</summary>
    public static string BuildUrl(CoverVariant variant, string region, string titleId) =>
        $"{BaseUrl}cover{variant.Quality}/{region}/{titleId}.{variant.Extension}";

    private async Task<ArtBlob?> TryRegionAsync(string region, string titleId, CancellationToken ct)
    {
        foreach (var variant in Variants)
        {
            var blob = await TryGetAsync(BuildUrl(variant, region, titleId), ct);
            if (blob is not null)
            {
                return blob;
            }
        }

        return null;
    }

    /// <summary>One GameTDB cover size: the path suffix after <c>cover</c>, and the file extension it is served as.</summary>
    public sealed record CoverVariant(string Quality, string Extension);
}
