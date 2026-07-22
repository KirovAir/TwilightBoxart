using TwilightBoxart.Data.Extensions;

namespace TwilightBoxart.Pipeline.Caching;

/// <summary>
/// Drains <see cref="CacheAccessBuffer"/> into the <c>CacheEntry</c> table on a fixed cadence.
/// </summary>
/// <remarks>
/// The flush half of the cache accounting design - see <see cref="Data.Entities.CacheEntry"/> for the
/// hot-path rule. This service is the only thing that turns a cache hit into a row change, in bulk,
/// off the request thread. The lag <see cref="CacheSettings.HitFlushInterval"/> introduces is part of
/// the design: the numbers it feeds are an LRU heuristic and a stats page.
/// </remarks>
public sealed class CacheAccountingService(
    CacheIndex index,
    ILogger<CacheAccountingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CacheSettings.HitFlushInterval);
        while (await timer.SafeWaitAsync(stoppingToken))
        {
            await FlushAsync(stoppingToken);
        }

        // One last drain on shutdown so a clean stop doesn't discard the current window. Not passing
        // the stopping token: it is already cancelled by the time we get here, and this write is the
        // whole reason a clean stop is better than a kill.
        await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        try
        {
            var updated = await index.FlushAsync(ct);
            if (updated > 0 && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Flushed buffered cache hits onto {Entries} entries", updated);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Accounting is best-effort: a failure here must never take the host down or stop the
            // loop. The cost of a lost window is a slightly stale LRU ordering.
            logger.LogWarning(ex, "Cache accounting flush failed");
        }
    }
}
