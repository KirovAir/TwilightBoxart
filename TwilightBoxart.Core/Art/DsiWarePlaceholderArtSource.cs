using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Art;

/// <summary>
/// The last rung: a generic DSiWare cover for the titles that essentially never have real art
/// upstream. libretro-thumbnails holds 13 DSi covers against a thousand-plus DSiWare titles
/// (measured 2026-07-20), so without this the whole category lands in "no art". The classic app
/// shipped the same image for K and H title ids; Z joins them because the 3DS Virtual Console
/// re-releases carry it too.
/// </summary>
public sealed class DsiWarePlaceholderArtSource : IArtSource
{
    private static readonly byte[] Image = LoadEmbedded();

    /// <summary>After GameTDB and libretro: a real cover, when one exists, always wins.</summary>
    public int Order => 100;

    public bool CanHandle(RomIdentity identity) =>
        identity.ConsoleType is ConsoleType.NintendoDs or ConsoleType.NintendoDsi
        && identity.Serial is { Length: 4 } serial
        && serial[0] is 'K' or 'H' or 'Z';

    public Task<ArtBlob?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default) =>
        Task.FromResult<ArtBlob?>(CanHandle(identity)
            ? new ArtBlob(Image, "embedded:dsiware", "image/jpeg")
            : null);

    private static byte[] LoadEmbedded()
    {
        using var stream = typeof(DsiWarePlaceholderArtSource).Assembly
            .GetManifestResourceStream("TwilightBoxart.Core.Art.dsiware.jpg")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
