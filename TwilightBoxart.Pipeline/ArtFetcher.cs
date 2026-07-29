namespace TwilightBoxart.Pipeline;

/// <summary>The upstream art blob plus which source produced it.</summary>
public sealed record FetchedArt(ArtBlob Blob, string Source);

/// <summary>Why a walk of the source ladder produced no art. Kept distinct so callers back off correctly.</summary>
public enum FetchStatus
{
    /// <summary>A source had art; <see cref="FetchResult.Art"/> carries it.</summary>
    Hit,

    /// <summary>Every source that could answer said "no art for this title". A long negative-cache is right.</summary>
    Miss,

    /// <summary>
    /// At least one source could not be REACHED and none of the others had art. A passing outage, not
    /// evidence the art is absent: back off for minutes so the next scan retries, never for hours.
    /// </summary>
    Unavailable
}

/// <summary>Outcome of walking the source ladder: a hit carries its art, a non-hit carries only why.</summary>
public readonly record struct FetchResult(FetchStatus Status, FetchedArt? Art)
{
    public static readonly FetchResult Miss = new(FetchStatus.Miss, null);
    public static readonly FetchResult Unavailable = new(FetchStatus.Unavailable, null);

    public static FetchResult Hit(FetchedArt art)
    {
        return new FetchResult(FetchStatus.Hit, art);
    }
}

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

    public async Task<FetchResult> TryFetchAsync(RomIdentity identity, CancellationToken ct = default)
    {
        var anyUnavailable = false;

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
                return FetchResult.Hit(new FetchedArt(blob, name));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken source must not stop the ladder: GameTDB being down should still let
                // libretro-thumbnails answer for a GBA title. But remember it COULD NOT answer, so a
                // no-art result that included a real outage is reported as Unavailable rather than Miss -
                // the pipeline then backs off for minutes instead of negative-caching the title for hours.
                monitor.RecordFailure(name, ex);
                anyUnavailable = true;
                logger.LogWarning(ex, "Art source {Source} failed for {Console}/{Key}",
                    name, identity.ConsoleType.Slug(), identity.Key);
            }
        }

        return anyUnavailable ? FetchResult.Unavailable : FetchResult.Miss;
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
