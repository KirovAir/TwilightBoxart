namespace TwilightBoxart.Pipeline;

/// <summary>The upstream art blob plus which source produced it.</summary>
public sealed record FetchedArt(ArtBlob Blob, string Source);

/// <summary>
/// Walks the registered <see cref="IArtSource"/>s in order and returns the first hit.
/// </summary>
/// <remarks>
/// Outbound concurrency is capped per source, inside each <c>HttpArtSource</c>: politeness is owed to
/// each upstream's operator individually, and gating there keeps a Retry-After back-off from holding a
/// slot for a different, healthy upstream. A miss and a failure stay distinct: the 2020 backend
/// collapsed every download problem into one exception and keyed its DS region fallback on whether the
/// message text contained "404", so a DNS outage read as "this game has no cover".
/// </remarks>
public sealed class ArtFetcher(
    IEnumerable<IArtSource> sources,
    UpstreamMonitor monitor,
    ILogger<ArtFetcher> logger)
{
    private readonly IReadOnlyList<IArtSource> _sources = [.. sources.OrderBy(s => s.Order)];

    public async Task<FetchedArt?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default)
    {
        foreach (var source in _sources)
        {
            if (!source.CanHandle(identity))
            {
                continue;
            }

            var name = SourceName(source);
            try
            {
                var blob = await source.TryFetchAsync(identity, ct);
                if (blob is null || blob.Data.Length == 0)
                {
                    monitor.RecordMiss(name);
                    continue;
                }

                monitor.RecordSuccess(name);
                return new FetchedArt(blob, name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken source must not stop the ladder: GameTDB being down should still let
                // libretro-thumbnails answer for a GBA title.
                monitor.RecordFailure(name, ex);
                logger.LogWarning(ex, "Art source {Source} failed for {Console}/{Key}",
                    name, identity.ConsoleType.Slug(), identity.Key);
            }
        }

        return null;
    }

    /// <summary>Short, stable label for logs and /v2/health: "GameTdbArtSource" becomes "gametdb".</summary>
    private static string SourceName(IArtSource source)
    {
        var name = source.GetType().Name;
        if (name.EndsWith("ArtSource", StringComparison.Ordinal))
        {
            name = name[..^"ArtSource".Length];
        }
        else if (name.EndsWith("Source", StringComparison.Ordinal))
        {
            name = name[..^"Source".Length];
        }

        return name.ToLowerInvariant();
    }
}
