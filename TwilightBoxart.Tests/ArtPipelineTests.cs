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

    /// <summary>
    /// The regression this whole column exists for. GET /v2/art/{platform}/{key} arrives with no
    /// identity, so the pipeline rebuilds one from the stored record; if that reconstruction drops the
    /// art name, every name-addressed source is asked for the canonical name and 404s, which is
    /// indistinguishable from the game having no cover. That is what shipped, and what this pins.
    /// </summary>
    [TestMethod]
    public async Task ArtName_OnTheRecord_ReachesTheSource()
    {
        await SeedRecordAsync(r =>
        {
            r.CanonicalName = "Fidgetts, The (Japan) (En)";
            r.ArtName = "Fidgetts, The (Japan)";
        });

        RomIdentity? asked = null;
        var pipeline = Build(new StubArtSource(0, (identity, _) =>
        {
            asked = identity;
            return Task.FromResult<ArtBlob?>(null);
        }));

        await pipeline.TryGetAsync(ConsoleType.NintendoDs, "ASME", RenderOptions.Default);

        Assert.AreEqual("Fidgetts, The (Japan)", asked?.ArtName, "the stored art name must address the source");
        Assert.AreEqual("Fidgetts, The (Japan) (En)", asked?.CanonicalName, "and the canonical name still travels");
    }

    /// <summary>
    /// A miss recorded before the index knew the real name was recorded against the wrong URL, so it is
    /// not evidence of anything. Learning the name has to release it, or a plain index rebuild cannot
    /// fix a title without the operator waiting out twelve hours or emptying the cache.
    /// </summary>
    [TestMethod]
    public async Task LearningAnArtName_ReleasesAMissRecordedWithoutOne()
    {
        await SeedRecordAsync(r =>
        {
            r.CanonicalName = "Fidgetts, The (Japan) (En)";
            r.MissUntil = DateTime.UtcNow + CacheSettings.NegativeCacheDuration;
        });

        var records = new ArtRecordStore(_factory, NullLogger<ArtRecordStore>.Instance);
        await records.RememberIdentityAsync(DsTitle with { ArtName = "Fidgetts, The (Japan)" });

        var record = await SingleRecordAsync();

        Assert.AreEqual("Fidgetts, The (Japan)", record.ArtName);
        Assert.IsNull(record.MissUntil, "the stale back-off was recorded against a name we now know was wrong");
    }

    /// <summary>
    /// The other half: once the name is stored, a repeat of the SAME name is not new knowledge. Without
    /// this the bypass fires on every request and a rebuild turns into a stampede on a volunteer upstream.
    /// </summary>
    [TestMethod]
    public async Task RelearningTheSameArtName_LeavesTheBackoffAlone()
    {
        var until = DateTime.UtcNow + CacheSettings.NegativeCacheDuration;
        await SeedRecordAsync(r =>
        {
            r.CanonicalName = "Fidgetts, The (Japan) (En)";
            r.ArtName = "Fidgetts, The (Japan)";
            r.MissUntil = until;
        });

        var records = new ArtRecordStore(_factory, NullLogger<ArtRecordStore>.Instance);
        await records.RememberIdentityAsync(DsTitle with { ArtName = "Fidgetts, The (Japan)" });

        var record = await SingleRecordAsync();

        Assert.IsNotNull(record.MissUntil, "nothing was learned, so the back-off stands");
    }

    /// <summary>A miss has to remember the name it tried, or the retry bypass never settles.</summary>
    [TestMethod]
    public async Task AMissRemembersTheArtNameItTried()
    {
        await SeedRecordAsync(r =>
        {
            r.CanonicalName = "Fidgetts, The (Japan) (En)";
            r.ArtName = "Fidgetts, The (Japan)";
        });

        var pipeline = Build(StubArtSource.Miss(0));
        await pipeline.TryGetAsync(ConsoleType.NintendoDs, "ASME", RenderOptions.Default);

        var record = await SingleRecordAsync();

        Assert.AreEqual("Fidgetts, The (Japan)", record.ArtName);
        Assert.IsNotNull(record.MissUntil);
    }

    /// <summary>
    /// The gap that shipped, in miniature. An existing row knows its canonical name but not its art
    /// name, and the request arrives with no identity: the only place left to learn the name is the
    /// index, so the lookup must not be gated on the canonical name already being missing.
    /// </summary>
    [TestMethod]
    public async Task KeyOnlyRequest_LearnsTheArtNameFromTheIndex()
    {
        await SeedRecordAsync(r => r.CanonicalName = "Fidgetts, The (Japan) (En)");

        RomIdentity? asked = null;
        var pipeline = Build(new FakeMetadataIndex { ArtName = "Fidgetts, The (Japan)" }, new StubArtSource(0, (identity, _) =>
        {
            asked = identity;
            return Task.FromResult<ArtBlob?>(null);
        }));

        await pipeline.TryGetAsync(ConsoleType.NintendoDs, "ASME", RenderOptions.Default);

        Assert.AreEqual("Fidgetts, The (Japan)", asked?.ArtName);
    }

    /// <summary>
    /// A hit has to record the name it succeeded with. Otherwise an eviction clears Sha256, the refetch
    /// arrives key-only, and a title that was working goes dark for the whole back-off.
    /// </summary>
    [TestMethod]
    public async Task AHitRemembersTheArtNameItSucceededWith()
    {
        var pipeline = Build(StubArtSource.Hit(0));

        await pipeline.TryGetAsync(DsTitle with { ArtName = "Fidgetts, The (Japan)" }, RenderOptions.Default);

        var record = await SingleRecordAsync();

        Assert.AreEqual("Fidgetts, The (Japan)", record.ArtName);
        Assert.IsNotNull(record.Sha256);
    }

    /// <summary>A back-off recorded against a different name is not evidence, so the retry must happen.</summary>
    [TestMethod]
    public async Task ANewArtName_BypassesALiveBackoff()
    {
        await SeedRecordAsync(r =>
        {
            r.CanonicalName = "Fidgetts, The (Japan) (En)";
            r.MissUntil = DateTime.UtcNow + CacheSettings.NegativeCacheDuration;
        });

        var asked = 0;
        var pipeline = Build(new StubArtSource(0, (_, _) =>
        {
            asked++;
            return Task.FromResult<ArtBlob?>(null);
        }));

        await pipeline.TryGetAsync(DsTitle with { ArtName = "Fidgetts, The (Japan)" }, RenderOptions.Default);

        Assert.AreEqual(1, asked, "the stored miss was recorded against a name we now know was wrong");
    }

    /// <summary>
    /// And it settles. If the same name kept bypassing, every request after a rebuild would hit a
    /// volunteer-run upstream, which is the failure mode this whole mechanism exists to avoid.
    /// </summary>
    [TestMethod]
    public async Task TheSameArtName_DoesNotBypassTwice()
    {
        await SeedRecordAsync(r => r.CanonicalName = "Fidgetts, The (Japan) (En)");

        var asked = 0;
        var pipeline = Build(new StubArtSource(0, (_, _) =>
        {
            asked++;
            return Task.FromResult<ArtBlob?>(null);
        }));

        var identity = DsTitle with { ArtName = "Fidgetts, The (Japan)" };
        await pipeline.TryGetAsync(identity, RenderOptions.Default);
        await pipeline.TryGetAsync(identity, RenderOptions.Default);
        await pipeline.TryGetAsync(identity, RenderOptions.Default);

        Assert.AreEqual(1, asked, "the first attempt stored the name it tried; the rest respect the back-off");
    }

    /// <summary>A cover we already have is never re-fetched, whatever the index has since learned.</summary>
    [TestMethod]
    public async Task ACoverAlreadyFetched_IsNeverRefetched()
    {
        var pipeline = Build(StubArtSource.Hit(0));
        await pipeline.TryGetAsync(DsTitle, RenderOptions.Default);

        var asked = 0;
        var second = Build(new StubArtSource(0, (_, _) =>
        {
            asked++;
            return Task.FromResult<ArtBlob?>(null);
        }));

        var art = await second.TryGetAsync(DsTitle with { ArtName = "Something Else (Japan)" }, RenderOptions.Default);

        Assert.IsNotNull(art, "the stored original still serves");
        Assert.AreEqual(0, asked, "a new art name must not invalidate art we already hold");
    }

    /// <summary>
    /// The whole bug, start to finish, as a user meets it. A name-digest key (Game Boy, so no serial to
    /// fall back on) whose record was written before art names existed and which carries a live miss
    /// from that era. Identify runs, learns the name the rebuilt index resolved, and the art request
    /// that follows must then reach the source under that name rather than the canonical one.
    /// </summary>
    [TestMethod]
    public async Task AStaleMissFromBeforeArtNames_HealsOnTheNextIdentify()
    {
        const string key = "a1b2c3d4e5f60718";
        await using (var db = _factory.CreateDbContext())
        {
            db.ArtRecords.Add(new ArtRecord
            {
                ConsoleType = ConsoleType.GameBoy,
                Key = key,
                CanonicalName = "Fidgetts, The (Japan) (En)",
                MissUntil = DateTime.UtcNow + CacheSettings.NegativeCacheDuration
            });
            await db.SaveChangesAsync();
        }

        var identified = new RomIdentity
        {
            ConsoleType = ConsoleType.GameBoy,
            Key = key,
            CanonicalName = "Fidgetts, The (Japan) (En)",
            ArtName = "Fidgetts, The (Japan)",
            MatchMethod = MatchMethod.Crc32
        };

        var records = new ArtRecordStore(_factory, NullLogger<ArtRecordStore>.Instance);
        await records.RememberIdentityAsync(identified);

        RomIdentity? asked = null;
        var pipeline = Build(new StubArtSource(0, (identity, _) =>
        {
            asked = identity;
            return Task.FromResult<ArtBlob?>(null);
        }));

        // The art request carries no identity, exactly as GET /v2/art/gb/{key} does.
        await pipeline.TryGetAsync(ConsoleType.GameBoy, key, RenderOptions.Default);

        Assert.IsNotNull(asked, "the stale miss was recorded against a name we now know was wrong");
        Assert.AreEqual("Fidgetts, The (Japan)", asked.ArtName);
    }

    /// <summary>
    /// The art route creates rows too, and it can win the insert race with nothing but a negative-cache
    /// stamp. Losing that race must not cost identify its whole write: dropping it would strand the
    /// key-only route with no name to address libretro with until some later identify repeated it.
    /// </summary>
    [TestMethod]
    public async Task ARacedIdentityWrite_IsReappliedToTheWinningRow()
    {
        var records = new ArtRecordStore(_factory, NullLogger<ArtRecordStore>.Instance);

        // Stand in for the art route winning the insert: the row exists, carrying only a miss.
        await SeedRecordAsync(r =>
        {
            r.Serial = null;
            r.CanonicalName = null;
            r.MissUntil = DateTime.UtcNow + CacheSettings.NegativeCacheDuration;
        });

        await records.RememberIdentityAsync(DsTitle with
        {
            CanonicalName = "Fidgetts, The (Japan) (En)",
            ArtName = "Fidgetts, The (Japan)"
        });

        var record = await SingleRecordAsync();

        Assert.AreEqual("Fidgetts, The (Japan)", record.ArtName, "identify's write must survive the race");
        Assert.AreEqual("Fidgetts, The (Japan) (En)", record.CanonicalName);
    }

    private async Task SeedRecordAsync(Action<ArtRecord> configure)
    {
        await using var db = _factory.CreateDbContext();
        var record = new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "ASME", Serial = "ASME" };
        configure(record);
        db.ArtRecords.Add(record);
        await db.SaveChangesAsync();
    }

    private ArtPipeline Build(params IArtSource[] sources)
    {
        return Build(new FakeMetadataIndex(), sources);
    }

    private ArtPipeline Build(FakeMetadataIndex index, params IArtSource[] sources)
    {
        var buffer = new CacheAccessBuffer();
        var cacheIndex = new CacheIndex(_factory, _caches, buffer, NullLogger<CacheIndex>.Instance);
        var records = new ArtRecordStore(_factory, NullLogger<ArtRecordStore>.Instance);
        var fetcher = new ArtFetcher(sources, new UpstreamMonitor(), NullLogger<ArtFetcher>.Instance);

        return new ArtPipeline(
            _caches, cacheIndex, records, fetcher,
            new FakeRenderer(), index, new SingleFlight(),
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
