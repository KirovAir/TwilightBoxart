using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Data;
using TwilightBoxart.Data.Entities;
using TwilightBoxart.Data.Extensions;
using TwilightBoxart.Pipeline;
using TwilightBoxart.Pipeline.Caching;

namespace TwilightBoxart.Tests;

/// <summary>
/// End to end through the pipeline: the back-off a no-art outcome earns depends on WHY there was no art.
/// </summary>
/// <remarks>
/// This is the payoff for the miss-vs-outage split below it. A genuine miss is negative-cached for hours;
/// a passing upstream outage is left alone for only minutes, so the next scan retries rather than hiding
/// a title's real cover for half a day. A regression here is the exact bug that filled the log with
/// timeouts and starved DSi art, so it is pinned.
/// </remarks>
[TestClass]
public class ArtPipelineTests
{
    private static readonly RomIdentity DsTitle = new()
    {
        ConsoleType = ConsoleType.NintendoDs,
        Key = "ASME",
        Serial = "ASME",
        MatchMethod = MatchMethod.HeaderSerial
    };

    private string _dir = "";
    private DbContextOptions<AppDbContext> _options = null!;
    private IDbContextFactory<AppDbContext> _factory = null!;
    private ArtCaches _caches = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "twlpipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dir, "test.db")}").Options;
        _factory = new PooledFactory(_options);

        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        _caches = new ArtCaches(
            new CachePaths(Path.Combine(_dir, "originals"), Path.Combine(_dir, "renders")),
            new CacheSettings().Normalized());
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            /* best effort */
        }
    }

    [TestMethod]
    public async Task UpstreamOutage_BacksOffForMinutes_NotTheFullMissDuration()
    {
        var before = DateTime.UtcNow;
        var pipeline = Build(StubArtSource.Unavailable(0));

        var art = await pipeline.TryGetAsync(DsTitle, RenderOptions.Default);

        Assert.IsNull(art, "an unreachable upstream has no art to serve this request");

        var record = await SingleRecordAsync();
        Assert.IsNull(record.Sha256, "nothing was fetched, so no original is attached");
        Assert.IsNotNull(record.MissUntil, "an outage still records a short back-off so a scan cannot re-hammer it");

        // The whole fix: minutes, not the 12-hour miss duration. Bounded generously to stay clear of the
        // test's own clock jitter while still failing loudly if the long duration crept back in.
        var backoff = record.MissUntil!.Value - before;
        Assert.IsTrue(backoff <= CacheSettings.TransientFailureBackoff + TimeSpan.FromMinutes(1),
            $"an outage must back off briefly; was {backoff}");
        Assert.IsTrue(backoff < CacheSettings.NegativeCacheDuration,
            "an outage must NOT inherit the genuine-miss negative-cache duration");
    }

    [TestMethod]
    public async Task GenuineMiss_BacksOffForTheFullNegativeCacheDuration()
    {
        var before = DateTime.UtcNow;
        var pipeline = Build(StubArtSource.Miss(0));

        var art = await pipeline.TryGetAsync(DsTitle, RenderOptions.Default);

        Assert.IsNull(art);

        var record = await SingleRecordAsync();
        Assert.IsNull(record.Sha256);
        Assert.IsNotNull(record.MissUntil);

        var backoff = record.MissUntil!.Value - before;
        Assert.IsTrue(backoff >= CacheSettings.NegativeCacheDuration - TimeSpan.FromMinutes(1),
            $"a real 'no art' answer keeps the full negative-cache duration; was {backoff}");
    }

    [TestMethod]
    public async Task Hit_StoresTheOriginalAndClearsAnyBackoff()
    {
        var pipeline = Build(StubArtSource.Hit(0));

        var art = await pipeline.TryGetAsync(DsTitle, RenderOptions.Default);

        Assert.IsNotNull(art, "a source had art, so this request must serve it");
        CollectionAssert.AreEqual(FakeArtSource.Png, art.Bytes, "the fake renderer passes the source bytes through");

        var record = await SingleRecordAsync();
        Assert.IsNotNull(record.Sha256, "a hit attaches the fetched original to the title");
        Assert.IsNull(record.MissUntil, "a hit clears any back-off");
    }

    private ArtPipeline Build(params IArtSource[] sources)
    {
        var buffer = new CacheAccessBuffer();
        var cacheIndex = new CacheIndex(_factory, _caches, buffer, NullLogger<CacheIndex>.Instance);
        var records = new ArtRecordStore(_factory, NullLogger<ArtRecordStore>.Instance);
        var fetcher = new ArtFetcher(sources, new UpstreamMonitor(), NullLogger<ArtFetcher>.Instance);

        return new ArtPipeline(
            _caches, cacheIndex, records, fetcher,
            new FakeRenderer(), new FakeMetadataIndex(), new SingleFlight(),
            NullLogger<ArtPipeline>.Instance);
    }

    private async Task<ArtRecord> SingleRecordAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.ArtRecords.AsNoTracking().SingleAsync();
    }

    /// <summary>Hands out short-lived contexts over this test's database, the way the app's factory does.</summary>
    private sealed class PooledFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
