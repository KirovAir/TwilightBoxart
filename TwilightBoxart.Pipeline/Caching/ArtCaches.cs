using TwilightBoxart.Data.Entities;
using TwilightBoxart.Data.Extensions;

namespace TwilightBoxart.Pipeline.Caching;

/// <summary>
/// The two cache layers, as one injectable. Kept together because the render key deliberately embeds
/// the original's SHA-256: publishing an override changes the hash, which invalidates every size and
/// border variant of that title for free, with no cross-layer bookkeeping.
/// </summary>
public sealed class ArtCaches
{
    /// <remarks>
    /// The roots come from deployment configuration and the budgets from the database. That split is
    /// on purpose: the sweep DELETES the files these paths point at, so the paths must stay somewhere
    /// HTTP cannot reach, while a budget is exactly the sort of thing an owner should be able to
    /// change without editing a file and restarting a container (see <see cref="CacheSettings"/>).
    /// </remarks>
    public ArtCaches(CachePaths paths, CacheSettings cache)
    {
        Originals = new DiskCache("originals", CacheKind.Original, paths.OriginalsPath, cache.OriginalsBudgetBytes);
        Renders = new DiskCache("renders", CacheKind.Render, paths.RendersPath, cache.RendersBudgetBytes);
    }

    /// <summary>Upstream art exactly as downloaded, content-addressed. Expensive to refetch.</summary>
    public DiskCache Originals { get; }

    /// <summary>Resized and bordered output. Disposable - about 5 ms to regenerate.</summary>
    public DiskCache Renders { get; }

    public IEnumerable<DiskCache> All => [Originals, Renders];

    /// <summary>
    /// Content-addressed layout: <c>ab/cd/abcd...bin</c>. Two levels of fan-out keep any single
    /// directory well under the point where enumeration on ext4/NTFS gets slow, at ~65k blobs
    /// per leaf before it matters.
    /// </summary>
    public static string OriginalPath(string sha256) =>
        Path.Combine(sha256[..2], sha256[2..4], sha256 + ".bin");

    /// <summary>
    /// Render layout: <c>{platform}/{key}/{sourceSha}/{discriminator}.png</c>. The key is the TITLE,
    /// never the fingerprint - that is what structurally kills the cache-key-explosion DoS the old
    /// BoxartRequest.FilenameHash had, where every distinct 512-byte header minted a new cache file.
    /// </summary>
    public static string RenderPath(ConsoleType console, string key, string sourceSha, RenderOptions options) =>
        Path.Combine(console.Slug(), key, sourceSha[..16], options.CacheDiscriminator() + ".png");
}
