using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Data;
using TwilightBoxart.Data.Entities;
using TwilightBoxart.Data.Extensions;
using TwilightBoxart.Pipeline;

namespace TwilightBoxart.Tests;

/// <summary>
/// Covers the retention limit on the originals layer (<see cref="CacheSettings.OriginalsMaxAge"/>).
/// </summary>
/// <remarks>
/// This is a legal-posture feature as much as a storage one: full-resolution covers are held as a
/// transient buffer that ages out on a clock, rather than accumulating into a permanent mirror of
/// artwork nobody has licensed to us. A regression here would be silent, so it is pinned.
/// </remarks>
[TestClass]
public class CacheRetentionTests
{
    private string _dir = "";
    private DbContextOptions<AppDbContext> _options = null!;
    private AppDbContext _db = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "twlretention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "test.db")}").Options;
        _db = new AppDbContext(_options);
        await _db.Database.MigrateAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Inserts a cache entry with a back-dated <c>CreatedDate</c>.
    /// </summary>
    /// <remarks>
    /// Two saves, not one, and that is forced by the audit behaviour: AppDbContext.SetAuditDates
    /// stamps CreatedDate = UtcNow on every ADDED entity, so a back-dated value set before the first
    /// save is silently overwritten. It only rewrites UpdatedDate on MODIFIED, so the second save
    /// leaves our value intact.
    /// </remarks>
    private async Task AddAsync(CacheKind kind, string key, DateTime created, long hits = 0)
    {
        var entry = new CacheEntry
        {
            CacheKey = key,
            Kind = kind,
            SizeBytes = 1024,
            HitCount = hits,
            LastAccessUtc = DateTime.UtcNow,
        };
        _db.CacheEntries.Add(entry);
        await _db.SaveChangesAsync();

        entry.CreatedDate = created;
        await _db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task ExpiredAsync_ReturnsOriginalsOlderThanTheLimit()
    {
        await AddAsync(CacheKind.Original, "old", DateTime.UtcNow.AddDays(-10));
        await AddAsync(CacheKind.Original, "fresh", DateTime.UtcNow.AddHours(-1));

        var expired = await _db.ExpiredAsync(CacheKind.Original, TimeSpan.FromDays(7), 100);

        Assert.AreEqual(1, expired.Count);
        Assert.AreEqual("old", expired[0].CacheKey);
    }

    [TestMethod]
    public async Task ExpiredAsync_IgnoresPopularityAndUsesStoredDate()
    {
        // Served a second ago, 9999 hits, but taken 10 days ago. Retention is about how long we have
        // HELD the copy, not how wanted it is - otherwise a popular cover would never age out at all.
        await AddAsync(CacheKind.Original, "popular", DateTime.UtcNow.AddDays(-10), hits: 9999);

        var expired = await _db.ExpiredAsync(CacheKind.Original, TimeSpan.FromDays(7), 100);

        Assert.AreEqual(1, expired.Count, "a frequently served original must still age out");
    }

    [TestMethod]
    public async Task ExpiredAsync_DoesNotTouchRenders()
    {
        await AddAsync(CacheKind.Render, "old-render", DateTime.UtcNow.AddDays(-90));

        var expired = await _db.ExpiredAsync(CacheKind.Original, TimeSpan.FromDays(7), 100);

        Assert.AreEqual(0, expired.Count, "renders are thumbnails and are left to LRU");
    }

    [TestMethod]
    public void OriginalsMaxAge_IsAConstantSevenDays()
    {
        // Licence hygiene, not tuning: a deployment that could raise or disable this would turn the
        // originals layer from a transient buffer into a permanent mirror of somebody else's artwork.
        Assert.AreEqual(TimeSpan.FromDays(7), CacheSettings.OriginalsMaxAge);
    }

    /// <summary>
    /// The prune must take only rows that are pure expired negative cache. A record still backing
    /// off, or one that identify ever enriched, is load-bearing: deleting the latter would break
    /// art-by-key resolution for that title.
    /// </summary>
    [TestMethod]
    public async Task PruneUnresolved_DeletesOnlyExpiredRecordsThatCarryNothing()
    {
        _db.ArtRecords.AddRange(
            new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "AAAA", MissUntil = DateTime.UtcNow.AddDays(-1) },
            new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "BBBB", MissUntil = DateTime.UtcNow.AddDays(1) },
            new ArtRecord
            {
                ConsoleType = ConsoleType.NintendoDs,
                Key = "CCCC",
                CanonicalName = "Some Game (USA)",
                MissUntil = DateTime.UtcNow.AddDays(-1),
            });
        await _db.SaveChangesAsync();

        var store = new ArtRecordStore(new SingleDbFactory(_options), NullLogger<ArtRecordStore>.Instance);
        var pruned = await store.PruneUnresolvedAsync();

        Assert.AreEqual(1, pruned, "only the expired, never-enriched row goes");
        var keys = await _db.ArtRecords.AsNoTracking().Select(r => r.Key).OrderBy(k => k).ToListAsync();
        CollectionAssert.AreEqual(new[] { "BBBB", "CCCC" }, keys);
    }

    /// <summary>Hands out contexts over this test's database, the way the app's factory does.</summary>
    private sealed class SingleDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
