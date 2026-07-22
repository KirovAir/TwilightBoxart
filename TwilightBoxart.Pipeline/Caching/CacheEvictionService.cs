using TwilightBoxart.Data.Extensions;
using TwilightBoxart.Core;

namespace TwilightBoxart.Pipeline.Caching;

/// <summary>
/// Keeps both cache layers inside their disk budgets by evicting least-recently-used entries.
/// </summary>
/// <remarks>
/// Runs out of band on purpose. Evicting during a request would make a cold, over-budget cache pay
/// for the sweep on the very request that is already waiting on an upstream fetch.
///
/// The first pass also reconciles the entry table with what is actually on disk - which is what makes
/// the budget survive a restart - and sweeps temp files a hard kill left behind. Those are not cache
/// entries, so no amount of eviction would ever reclaim them.
/// </remarks>
public sealed class CacheEvictionService(
    ArtCaches caches,
    CacheIndex index,
    ArtRecordStore records,
    ILogger<CacheEvictionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield before ANY synchronous work: BackgroundService.StartAsync runs ExecuteAsync inline
        // until its first await, so the recursive temp sweep below would otherwise walk both cache
        // roots on the startup thread - and an UnauthorizedAccessException out of the enumerator
        // (EnumerationOptions.Compatible does not ignore inaccessible directories) would take the
        // whole host down before it ever served a request.
        await Task.Yield();

        foreach (var cache in caches.All)
        {
            int swept;
            try
            {
                swept = AtomicFile.SweepStaleTemporaries(cache.Root, TimeSpan.FromHours(1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A single unreadable cache directory is not a reason to refuse to start.
                logger.LogWarning(ex, "Temp sweep of {Root} failed", cache.Root);
                continue;
            }
            if (swept > 0)
            {
                logger.LogInformation("Swept {Count} stale temp file(s) from the {Cache} cache", swept, cache.Name);
            }
        }

        try
        {
            await index.ReconcileAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            // A failed reconcile leaves the budget stale, not the server broken; serving art matters
            // more than an accurate byte count.
            logger.LogWarning(ex, "Cache reconcile failed; budgets may be stale until the next restart");
        }

        await SweepAsync(stoppingToken);

        using var timer = new PeriodicTimer(CacheSettings.EvictionInterval);
        while (await timer.SafeWaitAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            foreach (var result in await index.EvictAsync(ct))
            {
                logger.LogInformation("Evicted {Files} file(s) / {Bytes} bytes from the {Cache} cache",
                    result.FilesRemoved, result.BytesFreed, result.Cache);
            }

            var pruned = await records.PruneUnresolvedAsync(ct);
            if (pruned > 0)
            {
                logger.LogInformation("Pruned {Count} expired unresolved title record(s)", pruned);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eviction sweep failed");
        }
    }
}
