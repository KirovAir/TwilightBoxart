using Microsoft.Data.Sqlite;
using TwilightBoxart.Data.Entities;

namespace TwilightBoxart.Data.Extensions;

/// <summary>
/// Hits accumulated in memory for one <see cref="CacheEntry"/> since the last flush.
/// </summary>
public readonly record struct BufferedHits(long Count, DateTime LastAccessUtc);

/// <summary>How much one cache layer is holding, as of right now.</summary>
public readonly record struct CacheLayerUsage(CacheKind Kind, int Files, long Bytes);

/// <summary>
/// The write side of the cache-hit accounting; see <see cref="CacheEntry"/> for the hot-path rule.
/// </summary>
public static class CacheEntryExtensions
{
    /// <summary>
    /// Applies a batch of buffered hits in ONE transaction, keyed on <see cref="CacheEntry.CacheKey"/>.
    /// Returns the number of rows actually updated - lower than the batch size when entries were
    /// evicted between being served and being flushed, which is expected and not an error.
    /// </summary>
    /// <remarks>
    /// Uses <c>ExecuteUpdate</c> rather than loading and saving entities, for three reasons:
    /// it does not materialise rows that are about to be discarded; it does not bump
    /// <see cref="AuditableEntity.UpdatedDate"/>, because being read is not a modification of the
    /// entry; and it turns a flush into N cheap statements in one transaction instead of a change-
    /// tracker round-trip. Both writes are monotonic, so flushing a stale buffer late (or twice out of
    /// order) can never move <see cref="CacheEntry.LastAccessUtc"/> backwards and make the LRU sweep
    /// evict something hot.
    /// </remarks>
    public static async Task<int> FlushHitsAsync(
        this AppDbContext db,
        IReadOnlyDictionary<string, BufferedHits> hits,
        CancellationToken ct = default)
    {
        if (hits.Count == 0)
        {
            return 0;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var updated = 0;
        foreach (var (cacheKey, hit) in hits)
        {
            if (hit.Count <= 0)
            {
                continue;
            }

            var lastAccess = hit.LastAccessUtc;
            var count = hit.Count;

            updated += await db.CacheEntries
                .Where(e => e.CacheKey == cacheKey)
                .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.HitCount, e => e.HitCount + count)
                        .SetProperty(e => e.LastAccessUtc,
                            e => e.LastAccessUtc < lastAccess ? lastAccess : e.LastAccessUtc),
                    ct);
        }

        await tx.CommitAsync(ct);
        return updated;
    }

    /// <summary>
    /// Records that a file now exists in the cache, inserting the row or refreshing an existing one.
    /// Called on a cache WRITE - which is a miss, and therefore already the expensive path - never on
    /// a read. Reads go through <see cref="FlushHitsAsync"/> on a timer instead.
    /// </summary>
    public static async Task RegisterAsync(
        this AppDbContext db,
        string cacheKey,
        CacheKind kind,
        long sizeBytes,
        string? sourceSha256,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entry = await db.CacheEntries.FirstOrDefaultAsync(e => e.CacheKey == cacheKey, ct);
        if (entry is null)
        {
            db.CacheEntries.Add(new CacheEntry
            {
                CacheKey = cacheKey,
                Kind = kind,
                SizeBytes = sizeBytes,
                // Insert time, not default(DateTime): an entry that is written and never hit again
                // must still be evictable, and a zero timestamp would make it eternally the coldest.
                LastAccessUtc = now,
                SourceSha256 = sourceSha256,
            });
        }
        else
        {
            // A rewrite of the same key: the size can genuinely change (a re-encoded render), and
            // the budget is only honest if the row follows it.
            entry.Kind = kind;
            entry.SizeBytes = sizeBytes;
            entry.LastAccessUtc = now;
            entry.SourceSha256 = sourceSha256 ?? entry.SourceSha256;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 }) // SQLITE_CONSTRAINT
        {
            // Two DIFFERENT art keys whose upstream bytes are byte-identical hash to the same cache
            // key - GameTDB region variants and libretro "(USA)" vs "(USA) (Rev 1)" do this routinely.
            // Their single-flight keys differ, so nothing coalesces them, and the read-then-insert
            // above races. The loser's row would be identical to the winner's, so the only correct
            // response is to carry on: throwing would turn a successful fetch into a 500.
            // Only the unique-key race is swallowed; any other database failure still surfaces.
        }
    }

    /// <summary>
    /// What each layer is currently holding: <c>SELECT Kind, COUNT(*), SUM(SizeBytes) GROUP BY Kind</c>.
    /// This is the check that decides whether the eviction sweep needs to do anything at all, and it
    /// is why <see cref="CacheEntry.Kind"/> is indexed.
    /// </summary>
    public static async Task<IReadOnlyList<CacheLayerUsage>> CacheUsageAsync(
        this AppDbContext db, CancellationToken ct = default)
    {
        return await db.CacheEntries
            .AsNoTracking()
            .GroupBy(e => e.Kind)
            .Select(g => new CacheLayerUsage(g.Key, g.Count(), g.Sum(e => e.SizeBytes)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The <paramref name="take"/> least-recently-served entries in a layer - the LRU sweep's victim
    /// list, served straight off the <c>LastAccessUtc</c> index.
    /// </summary>
    /// <remarks>
    /// Taken in bounded batches rather than as one ordered list of the whole layer: a cold start over
    /// a large originals cache would otherwise materialise every row to delete a handful of them.
    /// The caller loops until the layer is back under its low-water mark.
    /// </remarks>
    public static async Task<List<CacheEntry>> ColdestAsync(
        this AppDbContext db, CacheKind kind, int take, CancellationToken ct = default)
    {
        return await db.CacheEntries
            .Where(e => e.Kind == kind)
            .OrderBy(e => e.LastAccessUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Entries stored longer ago than <paramref name="maxAge"/>, regardless of how popular they are.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on <see cref="AuditableEntity.CreatedDate"/> (when we took the copy) rather
    /// than <c>LastAccessUtc</c> (when someone last wanted it), because this is a RETENTION limit, not
    /// a popularity one; LRU already handles popularity. Applied to the originals layer, it means a
    /// full-resolution cover publishers own is held as a transient performance cache and ages out on a
    /// clock, instead of accumulating into a permanent mirror of somebody else's artwork. The renders
    /// layer is 128x115 thumbnails and is left to LRU.
    /// </remarks>
    public static async Task<List<CacheEntry>> ExpiredAsync(
        this AppDbContext db, CacheKind kind, TimeSpan maxAge, int take, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return await db.CacheEntries
            .Where(e => e.Kind == kind && e.CreatedDate < cutoff)
            .OrderBy(e => e.CreatedDate)
            .Take(take)
            .ToListAsync(ct);
    }
}
