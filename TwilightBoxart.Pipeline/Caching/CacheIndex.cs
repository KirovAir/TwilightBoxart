using TwilightBoxart.Data;
using TwilightBoxart.Data.Entities;
using TwilightBoxart.Data.Extensions;

namespace TwilightBoxart.Pipeline.Caching;

/// <summary>What one eviction sweep removed from one layer.</summary>
public readonly record struct EvictionResult(string Cache, int FilesRemoved, long BytesFreed);

/// <summary>A layer's current occupancy, for <c>/v2/health</c>.</summary>
public readonly record struct CacheUsage(string Name, int Files, long Bytes, long BudgetBytes);

/// <summary>
/// The only door to the art cache: bytes on disk, bookkeeping in the database, kept in step.
/// </summary>
/// <remarks>
/// Reads never touch the database - hits go through <see cref="CacheAccessBuffer"/> and are flushed
/// in bulk; see <see cref="CacheEntry"/> for the hot-path rule. Short-lived contexts from
/// <see cref="IDbContextFactory{TContext}"/>, one per unit of work, because the background services
/// here outlive any request scope.
/// </remarks>
public sealed class CacheIndex(
    IDbContextFactory<AppDbContext> dbFactory,
    ArtCaches caches,
    CacheAccessBuffer buffer,
    ILogger<CacheIndex> logger)
{
    /// <summary>
    /// How many rows one eviction pass materialises at a time. Bounded so a cold start over a large
    /// originals layer does not load the entire table to delete a handful of entries.
    /// </summary>
    private const int EvictionBatchSize = 256;

    /// <summary>False until the startup reconcile has run, so /v2/health can say "counting" rather than lie.</summary>
    public bool Scanned { get; private set; }

    /// <summary>
    /// Reads a cached blob and records the hit in memory. Returns null on a miss - never an
    /// exception, and never a database round-trip either way.
    /// </summary>
    public async Task<byte[]?> TryReadAsync(DiskCache cache, string relativePath, CancellationToken ct = default)
    {
        var data = await cache.TryReadAsync(relativePath, ct);
        if (data is not null)
        {
            buffer.Record(cache.CacheKeyFor(relativePath));
        }

        return data;
    }

    /// <summary>
    /// Writes a blob and registers it. The file lands first: a row pointing at a file that does not
    /// exist would make the sweep think it has disk to reclaim, whereas a file with no row is merely
    /// invisible until the next reconcile.
    /// </summary>
    public async Task WriteAsync(
        DiskCache cache,
        string relativePath,
        ReadOnlyMemory<byte> data,
        string? sourceSha256,
        CancellationToken ct = default)
    {
        await cache.WriteAsync(relativePath, data, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.RegisterAsync(cache.CacheKeyFor(relativePath), cache.Kind, data.Length, sourceSha256, ct);
    }

    /// <summary>
    /// Applies everything buffered since the last call, in one transaction. Returns the number of
    /// rows updated, which is legitimately lower than the batch size when entries were evicted
    /// between being served and being flushed.
    /// </summary>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        var hits = buffer.Drain();
        if (hits.Count == 0)
        {
            return 0;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FlushHitsAsync(hits, ct);
    }

    /// <summary>
    /// Brings the entry table back in line with what is actually on disk. Run once at startup, which
    /// is what makes the budget survive a restart - and what recovers from an operator deleting cache
    /// files by hand, or from a volume that came back empty.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        foreach (var cache in caches.All)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var onDisk = cache.Enumerate()
                .ToDictionary(f => cache.CacheKeyFor(f.RelativePath), f => f.SizeBytes, StringComparer.Ordinal);

            var rows = await db.CacheEntries.Where(e => e.Kind == cache.Kind).ToListAsync(ct);
            var now = DateTime.UtcNow;
            var removed = 0;

            foreach (var row in rows)
            {
                if (!onDisk.Remove(row.CacheKey, out var sizeBytes))
                {
                    // The file is gone: the row is a claim on disk nobody is using, and leaving it
                    // would make the layer look permanently over budget.
                    db.CacheEntries.Remove(row);
                    removed++;
                    continue;
                }

                row.SizeBytes = sizeBytes;
            }

            // Whatever is left is on disk with no row - a file written just before a hard kill, or a
            // blob restored from a backup. Adopt it rather than orphan it forever.
            foreach (var (cacheKey, sizeBytes) in onDisk)
            {
                db.CacheEntries.Add(new CacheEntry
                {
                    CacheKey = cacheKey,
                    Kind = cache.Kind,
                    SizeBytes = sizeBytes,
                    LastAccessUtc = now,
                });
            }

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "{Cache} cache reconciled: {Files} file(s), {Adopted} adopted, {Removed} stale row(s) dropped",
                cache.Name, rows.Count - removed + onDisk.Count, onDisk.Count, removed);
        }

        Scanned = true;
    }

    /// <summary>
    /// Evicts least-recently-served entries until each layer is back under its low-water mark.
    /// </summary>
    /// <remarks>
    /// Sweeping to a fraction of the budget rather than to exactly the budget is what stops the sweep
    /// from having to run again on the very next tick: evicting to 100% leaves the next write over it
    /// again. Recency, not hit count, picks the victim - a cover requested a thousand times last year
    /// is worth less than one requested once this morning.
    /// </remarks>
    public async Task<IReadOnlyList<EvictionResult>> EvictAsync(CancellationToken ct = default)
    {
        var results = new List<EvictionResult>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Age sweep first, and only on originals. This is a retention limit rather than a capacity
        // one (see CacheSettings.OriginalsMaxAge): full-resolution covers age out on a clock whether
        // or not the layer is over budget, so the cache stays a transient buffer instead of slowly
        // becoming a permanent mirror of artwork we do not own. Running it before the budget pass also
        // means the LRU sweep below usually has nothing left to do.
        var expired = await ExpireByAgeAsync(db, ct);

        var usage = await db.CacheUsageAsync(ct);

        foreach (var cache in caches.All)
        {
            // ExpireByAgeAsync committed its deletions before the usage query ran, so the numbers
            // here already exclude the expired rows.
            var bytes = usage.FirstOrDefault(u => u.Kind == cache.Kind).Bytes;
            if (bytes <= cache.BudgetBytes)
            {
                continue;
            }

            var target = (long)(cache.BudgetBytes * CacheSettings.EvictionLowWaterFraction);
            var removed = 0;
            long freed = 0;

            while (bytes > target && !ct.IsCancellationRequested)
            {
                var victims = await db.ColdestAsync(cache.Kind, EvictionBatchSize, ct);
                if (victims.Count == 0)
                {
                    break;
                }

                foreach (var victim in victims)
                {
                    if (bytes <= target)
                    {
                        break;
                    }

                    cache.Delete(cache.RelativePathFor(victim.CacheKey));
                    db.CacheEntries.Remove(victim);
                    bytes -= victim.SizeBytes;
                    freed += victim.SizeBytes;
                    removed++;
                }

                await db.SaveChangesAsync(ct);
            }

            if (removed > 0)
            {
                results.Add(new EvictionResult(cache.Name, removed, freed));
            }
        }

        if (expired.Count > 0)
        {
            results.Add(new EvictionResult("originals (expired)", expired.Count, expired.Bytes));
        }

        return results;
    }

    /// <summary>
    /// Drops originals older than <see cref="CacheSettings.OriginalsMaxAge"/> and returns the bytes
    /// freed. Batched like the LRU pass so a long-idle instance cannot materialise the whole layer in
    /// one query.
    /// </summary>
    private async Task<(int Count, long Bytes)> ExpireByAgeAsync(AppDbContext db, CancellationToken ct)
    {
        var maxAge = CacheSettings.OriginalsMaxAge;
        if (maxAge <= TimeSpan.Zero)
        {
            return (0, 0);
        }

        var originals = caches.All.FirstOrDefault(c => c.Kind == CacheKind.Original);
        if (originals is null)
        {
            return (0, 0);
        }

        var count = 0;
        long freed = 0;
        while (!ct.IsCancellationRequested)
        {
            var stale = await db.ExpiredAsync(CacheKind.Original, maxAge, EvictionBatchSize, ct);
            if (stale.Count == 0)
            {
                break;
            }

            foreach (var entry in stale)
            {
                originals.Delete(originals.RelativePathFor(entry.CacheKey));
                db.CacheEntries.Remove(entry);
                freed += entry.SizeBytes;
                count++;
            }

            await db.SaveChangesAsync(ct);
        }

        return (count, freed);
    }

    /// <summary>Per-layer occupancy for <c>/v2/health</c>, straight off the <c>Kind</c> index.</summary>
    public async Task<IReadOnlyList<CacheUsage>> UsageAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var usage = await db.CacheUsageAsync(ct);

        return
        [
            .. caches.All.Select(cache =>
            {
                var layer = usage.FirstOrDefault(u => u.Kind == cache.Kind);
                return new CacheUsage(cache.Name, layer.Files, layer.Bytes, cache.BudgetBytes);
            })
        ];
    }
}
