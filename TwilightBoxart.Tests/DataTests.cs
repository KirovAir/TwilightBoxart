using Microsoft.EntityFrameworkCore;
using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Data;
using TwilightBoxart.Data.Entities;
using TwilightBoxart.Data.Extensions;

namespace TwilightBoxart.Tests;

/// <summary>
/// The mutable half of the two-database split (see the header of <c>AppDbContext.cs</c>), exercised
/// against a real migrated SQLite FILE in a temp directory - never the in-memory provider. The things
/// worth testing here are the things a fake provider cannot tell you: that the migration actually runs,
/// and that the unique indexes are really in the file and really reject duplicates (including the
/// NOCASE spellings the app promises to fold together).
/// </summary>
[TestClass]
public class DataTests
{
    private string _dir = null!;
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "twilight-data-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "twilightboxart.db");

        using var db = NewContext();
        db.Database.Migrate();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // SQLite holds the file open through the pooled connection; without this the delete fails on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>A fresh context per unit of work, exactly as the app's DbContextFactory hands them out.</summary>
    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    // The migration itself

    [TestMethod]
    public void AppDbContext_MigratesAFileBackedDatabaseWithNoPendingWork()
    {
        using var db = NewContext();

        Assert.IsTrue(File.Exists(_dbPath), "the migration must produce a real file on disk");
        Assert.AreEqual(0, db.Database.GetPendingMigrations().Count(),
            "the model and the migrations must not have drifted apart");
        CollectionAssert.Contains(db.Database.GetAppliedMigrations().Select(m => m.Split('_')[^1]).ToList(),
            "InitialCreate");
    }

    // ArtRecord: the title space, so the strictest tests

    [TestMethod]
    public async Task ArtRecord_StoresConsoleTypeAsItsName()
    {
        await using (var db = NewContext())
        {
            db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.GameBoyColor, Key = "X" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            // Reading the raw column: enums are persisted by name so the table stays legible and so
            // reordering the enum can never silently repoint existing rows at a different console.
            var raw = await db.Database
                .SqlQueryRaw<string>("SELECT \"ConsoleType\" AS \"Value\" FROM \"ArtRecord\"")
                .SingleAsync();
            Assert.AreEqual("GameBoyColor", raw);
        }
    }

    [TestMethod]
    public async Task ArtRecord_RejectsASecondRecordForTheSameConsoleAndKey()
    {
        await using var db = NewContext();
        db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "ASME" });
        await db.SaveChangesAsync();

        db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "ASME" });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ArtRecord_TreatsKeysThatDifferOnlyInCaseAsTheSameRecord()
    {
        await using var db = NewContext();
        db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "ASME" });
        await db.SaveChangesAsync();

        // Keys arrive from URL paths. If NOCASE were dropped from the index, "asme" would insert a
        // second row and the art path would then serve whichever one SQLite happened to return.
        db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "asme" });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ArtRecord_AllowsTheSameKeyOnDifferentConsoles()
    {
        await using var db = NewContext();
        db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.NintendoDs, Key = "ASME" });
        db.ArtRecords.Add(new ArtRecord { ConsoleType = ConsoleType.NintendoDsi, Key = "ASME" });
        await db.SaveChangesAsync();

        Assert.AreEqual(2, await db.ArtRecords.CountAsync());
    }

    // CacheEntry and the buffered hit accounting

    [TestMethod]
    public async Task CacheEntry_RoundTripsEveryField()
    {
        var lastAccess = new DateTime(2026, 7, 20, 11, 22, 33, DateTimeKind.Utc);

        await using (var db = NewContext())
        {
            db.CacheEntries.Add(new CacheEntry
            {
                CacheKey = "a1b2c3_128x115_ar_None_1_FF000000",
                Kind = CacheKind.Render,
                SizeBytes = 12_345,
                LastAccessUtc = lastAccess,
                HitCount = 7,
                SourceSha256 = "a1b2c3",
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var saved = await db.CacheEntries.SingleAsync();
            Assert.AreEqual(CacheKind.Render, saved.Kind);
            Assert.AreEqual(12_345, saved.SizeBytes);
            Assert.AreEqual(lastAccess, saved.LastAccessUtc);
            Assert.AreEqual(7, saved.HitCount);
            Assert.AreEqual("a1b2c3", saved.SourceSha256);
        }
    }

    [TestMethod]
    public async Task CacheEntry_RejectsADuplicateCacheKey()
    {
        await using var db = NewContext();
        db.CacheEntries.Add(new CacheEntry { CacheKey = "dupe", Kind = CacheKind.Original });
        await db.SaveChangesAsync();

        // Two rows for one file would double-count the entry against the layer budget and leave the
        // sweep able to delete a file another row still claims exists.
        db.CacheEntries.Add(new CacheEntry { CacheKey = "dupe", Kind = CacheKind.Render });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task FlushHits_AppliesBufferedCountsWithoutTouchingTheAuditDates()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = NewContext())
        {
            db.CacheEntries.Add(new CacheEntry
            {
                CacheKey = "hot", Kind = CacheKind.Render, LastAccessUtc = created, HitCount = 2,
            });
            await db.SaveChangesAsync();
        }

        DateTime updatedBefore;
        await using (var db = NewContext())
        {
            updatedBefore = (await db.CacheEntries.SingleAsync()).UpdatedDate;
        }

        var newAccess = created.AddHours(3);
        await using (var db = NewContext())
        {
            var flushed = await db.FlushHitsAsync(new Dictionary<string, BufferedHits>
            {
                ["hot"] = new(40, newAccess),
                // An entry evicted between being served and being flushed is expected, not an error.
                ["evicted-since"] = new(5, newAccess),
            });
            Assert.AreEqual(1, flushed);
        }

        await using (var db = NewContext())
        {
            var saved = await db.CacheEntries.SingleAsync();
            Assert.AreEqual(42, saved.HitCount, "buffered hits must accumulate onto the stored count");
            Assert.AreEqual(newAccess, saved.LastAccessUtc);
            Assert.AreEqual(updatedBefore, saved.UpdatedDate,
                "being read is not a modification: the flush must not bump the audit date");
        }
    }

    [TestMethod]
    public async Task FlushHits_NeverMovesLastAccessBackwards()
    {
        var now = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = NewContext())
        {
            db.CacheEntries.Add(new CacheEntry { CacheKey = "hot", Kind = CacheKind.Render, LastAccessUtc = now });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            // A buffer that was filled before the current one and flushed late. If this won the write,
            // the LRU sweep could evict something that is actually hot.
            await db.FlushHitsAsync(new Dictionary<string, BufferedHits>
            {
                ["hot"] = new(3, now.AddMinutes(-30)),
            });
        }

        await using (var db = NewContext())
        {
            var saved = await db.CacheEntries.SingleAsync();
            Assert.AreEqual(now, saved.LastAccessUtc);
            Assert.AreEqual(3, saved.HitCount, "the count is still additive even when the timestamp is stale");
        }
    }

    // Auditing

    [TestMethod]
    public async Task AuditableEntity_SetsBothDatesOnInsert()
    {
        var before = DateTime.UtcNow;

        await using var db = NewContext();
        var row = new ArtRecord { ConsoleType = ConsoleType.GameBoyAdvance, Key = "ASME" };
        db.ArtRecords.Add(row);
        await db.SaveChangesAsync();

        var after = DateTime.UtcNow;
        Assert.IsTrue(row.CreatedDate >= before && row.CreatedDate <= after, "CreatedDate must be set on insert");
        Assert.AreEqual(row.CreatedDate, row.UpdatedDate, "an unmodified row's dates are the same instant");
    }

    [TestMethod]
    public async Task AuditableEntity_MovesOnlyUpdatedDateOnModify()
    {
        int id;
        DateTime created;

        await using (var db = NewContext())
        {
            var row = new ArtRecord { ConsoleType = ConsoleType.Nes, Key = "0011223344556677" };
            db.ArtRecords.Add(row);
            await db.SaveChangesAsync();
            id = row.Id;
            created = row.CreatedDate;
        }

        // The clock is precise on .NET but not infinitely so; a short wait keeps the assertion honest.
        await Task.Delay(25);

        await using (var db = NewContext())
        {
            var row = await db.ArtRecords.SingleAsync(r => r.Id == id);
            row.Title = "modified";
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var row = await db.ArtRecords.SingleAsync(r => r.Id == id);
            Assert.AreEqual(created, row.CreatedDate, "CreatedDate is written once and never again");
            Assert.IsTrue(row.UpdatedDate > created, "UpdatedDate must advance on modify");
        }
    }

    // The settings POCOs

    [TestMethod]
    public void RenderOptions_DefaultIsTheBoxArtSizeTwilightMenuRecommends()
    {
        // The render defaults are a constant now, not configuration. TWiLightMenu++ silently ignores
        // any PNG over 0xB000, so the ceiling in particular must not be something a deployment can
        // raise: doing so produces art that never appears and logs no error anywhere.
        Assert.AreEqual(128, RenderOptions.Default.Width);
        Assert.AreEqual(115, RenderOptions.Default.Height);
        Assert.IsTrue(RenderOptions.Default.KeepAspectRatio);
        Assert.AreEqual(BoxartBorderStyle.None, RenderOptions.Default.BorderStyle);
        Assert.AreEqual(RenderOptions.TwilightMaxPngBytes, RenderOptions.Default.MaxPngBytes);
    }

    [TestMethod]
    public void RenderOptions_ClampsDimensionsToTheCeiling()
    {
        var normalized = new RenderOptions { Width = 9999, Height = -5 }.Normalized();

        Assert.AreEqual(RenderOptions.MaxWidth, normalized.Width);
        Assert.AreEqual(1, normalized.Height);
    }

    [TestMethod]
    public void RenderOptions_DsSizedRenderKeepsTheTwilightByteCap()
    {
        // Anything TWiLightMenu++ can display must stay under its 0xB000 cache slot, or the cover is
        // silently dropped on the device.
        var normalized = new RenderOptions
        {
            Width = RenderOptions.TwilightMaxWidth,
            Height = RenderOptions.TwilightMaxHeight,
        }.Normalized();

        Assert.AreEqual(RenderOptions.TwilightMaxPngBytes, normalized.MaxPngBytes);
    }

    [TestMethod]
    public void RenderOptions_OversizeRenderIsFreedFromTheTwilightByteCap()
    {
        // A cover too large for the DS screen can never land in TWiLightMenu's cache anyway; holding
        // it to the 44 KB cap would only quantize it into mush for the frontend it IS for.
        var normalized = new RenderOptions
        {
            Width = RenderOptions.TwilightMaxWidth + 1,
            Height = RenderOptions.TwilightMaxHeight,
        }.Normalized();

        Assert.AreEqual(RenderOptions.OversizeMaxPngBytes, normalized.MaxPngBytes);
    }

    [TestMethod]
    public void RenderOptions_OversizeRenderKeepsAnExplicitCallerBudget()
    {
        var normalized = new RenderOptions
        {
            Width = RenderOptions.MaxWidth,
            Height = RenderOptions.MaxHeight,
            MaxPngBytes = 100_000,
        }.Normalized();

        Assert.AreEqual(100_000, normalized.MaxPngBytes, "a deliberate budget must never be widened");
    }

    [TestMethod]
    public void RenderOptions_FallsBackToNoBorderForAnUndefinedStyle()
    {
        var normalized = new RenderOptions { BorderStyle = (BoxartBorderStyle)99, BorderThickness = 400 }
            .Normalized();

        Assert.AreEqual(BoxartBorderStyle.None, normalized.BorderStyle);
        Assert.AreEqual(0, normalized.BorderThickness, "no border means bt is dead and folds flat");
    }

    [TestMethod]
    public void CacheSettings_ClampsTheTwoBudgetsThatAreStillConfigurable()
    {
        // Disk space is the one cache fact that genuinely differs per host, so it is the one that is
        // still bound from configuration - and therefore the one that still needs clamping.
        var normalized = new CacheSettings
        {
            OriginalsBudgetBytes = 1,
            RendersBudgetBytes = long.MaxValue,
        }.Normalized();

        Assert.AreEqual(16L * 1024 * 1024, normalized.OriginalsBudgetBytes);
        Assert.AreEqual(1024L * 1024 * 1024 * 1024, normalized.RendersBudgetBytes);
    }

    [TestMethod]
    public void ArtSourceLimits_KeepTheUserAgentHonestAndSafeToPutInAHeader()
    {
        // The numeric limits are consts now, so asserting their ranges is a tautology the compiler
        // will tell you about. What is still worth pinning is the User-Agent: it goes into an HTTP
        // header (a CR or LF there would be header injection) and it is the only way an upstream
        // operator who dislikes our traffic can reach us instead of silently null-routing us.
        Assert.IsFalse(ArtSourceLimits.UserAgent.Any(char.IsControl),
            "a CR or LF here would be header injection");
        Assert.IsTrue(ArtSourceLimits.UserAgent.Contains("github.com", StringComparison.OrdinalIgnoreCase),
            "the user agent must stay contactable");
    }
}
