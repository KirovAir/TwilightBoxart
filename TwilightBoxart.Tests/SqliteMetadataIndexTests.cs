using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Identify;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Tests.Fixtures;

namespace TwilightBoxart.Tests;

/// <summary>
/// Covers the read side of the generated index against a real file with the real schema, including
/// FTS5's trigram tokenizer, which is the whole reason the index is generated rather than migrated.
/// </summary>
[TestClass]
public class SqliteMetadataIndexTests
{
    private static SqliteMetadataIndex OpenIndex(NoIntroIndexFile file) =>
        new(file.Path, NullLogger.Instance);

    [TestMethod]
    public void SqliteMetadataIndex_TryByCrc32_FindsSeededRow()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. (World)", Crc32: 0x0D8C_1B0Au));
        using var index = OpenIndex(file);

        Assert.IsTrue(index.TryByCrc32(0x0D8C_1B0Au, out var entry));
        Assert.AreEqual("Super Mario Bros. (World)", entry.Name);
        Assert.AreEqual(ConsoleType.Nes, entry.ConsoleType);
        Assert.AreEqual(0x0D8C_1B0Au, entry.Crc32);
    }

    /// <summary>
    /// SQLite has no unsigned integer, so a CRC with the high bit set is stored negative. Getting the
    /// reinterpretation wrong in either direction silently loses half of all lookups.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_TryByCrc32_RoundTripsCrcsWithTheHighBitSet()
    {
        const uint high = 0xFFFF_FFFEu;
        using var file = NoIntroIndexFile.Create(new IndexRow(ConsoleType.Snes, "High Bit", Crc32: high));
        using var index = OpenIndex(file);

        Assert.IsTrue(index.TryByCrc32(high, out var entry));
        Assert.AreEqual("High Bit", entry.Name);
        Assert.AreEqual(high, entry.Crc32);
    }

    [TestMethod]
    public void SqliteMetadataIndex_TryByCrc32_UnknownCrc_Misses()
    {
        using var file = NoIntroIndexFile.Create(new IndexRow(ConsoleType.Nes, "Seeded", Crc32: 1u));
        using var index = OpenIndex(file);

        Assert.IsFalse(index.TryByCrc32(2u, out _));
    }

    [TestMethod]
    public void SqliteMetadataIndex_TryBySha1_IgnoresCaseInvariantly()
    {
        const string sha1 = "9d1a1c2f0e6b5a4d3c2b1a0f9e8d7c6b5a4d3c2b";
        using var file = NoIntroIndexFile.Create(new IndexRow(ConsoleType.GameGear, "Loose Cart", Sha1: sha1));
        using var index = OpenIndex(file);

        Assert.IsTrue(index.TryBySha1(sha1.ToUpperInvariant(), out var entry));
        Assert.AreEqual("Loose Cart", entry.Name);
    }

    [TestMethod]
    public void SqliteMetadataIndex_TryBySerial_IsScopedToItsConsole()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.NintendoDs, "Mario Kart DS (USA)", Serial: "AMCE"),
            new IndexRow(ConsoleType.GameBoyAdvance, "Some GBA Game (USA)", Serial: "AMCE"));
        using var index = OpenIndex(file);

        Assert.IsTrue(index.TryBySerial(ConsoleType.NintendoDs, "AMCE", out var ds));
        Assert.AreEqual("Mario Kart DS (USA)", ds.Name);

        Assert.IsTrue(index.TryBySerial(ConsoleType.GameBoyAdvance, "amce", out var gba));
        Assert.AreEqual("Some GBA Game (USA)", gba.Name);

        Assert.IsFalse(index.TryBySerial(ConsoleType.Snes, "AMCE", out _));
    }

    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_MatchesDespiteExtensionAndPunctuation()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. (World)"),
            new IndexRow(ConsoleType.Nes, "Duck Hunt (World)"));
        using var index = OpenIndex(file);

        var match = index.SearchByName(ConsoleType.Nes, "Super Mario Bros (World).nes");

        Assert.IsNotNull(match);
        Assert.AreEqual("Super Mario Bros. (World)", match.Name);
    }

    /// <summary>
    /// The rung that can be confidently wrong. A different game in the same series must fall below the
    /// threshold rather than produce a plausible-looking wrong cover.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_UnrelatedName_ReturnsNull()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. (World)"));
        using var index = OpenIndex(file);

        Assert.IsNull(index.SearchByName(ConsoleType.Nes, "Castlevania III - Dracula's Curse (USA).nes"));
    }

    /// <summary>
    /// A No-Intro name carries tags the user's file name will not: the threshold is applied to the title
    /// stem so a revision suffix cannot push a correct match below it.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_IgnoresExtraNoIntroTags()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Castlevania (USA) (Rev 1)"));
        using var index = OpenIndex(file);

        var match = index.SearchByName(ConsoleType.Nes, "Castlevania (USA).nes");

        Assert.IsNotNull(match);
        Assert.AreEqual("Castlevania (USA) (Rev 1)", match.Name);
    }

    /// <summary>Tags are what choose between the region variants of a title that already matched.</summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_UsesTagsToPickTheRegionVariant()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Castlevania (Japan)"),
            new IndexRow(ConsoleType.Nes, "Castlevania (Europe)"),
            new IndexRow(ConsoleType.Nes, "Castlevania (USA)"));
        using var index = OpenIndex(file);

        var match = index.SearchByName(ConsoleType.Nes, "Castlevania (Europe).nes");

        Assert.IsNotNull(match);
        Assert.AreEqual("Castlevania (Europe)", match.Name);
    }

    /// <summary>
    /// The dominant false-positive mode. A sequel differs from its predecessor by one character and
    /// scores ~0.90 on any character metric, so it must be excluded by rule rather than by threshold.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_DoesNotMatchASequel()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. 2 (USA)"),
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. 3 (USA)"));
        using var index = OpenIndex(file);

        Assert.IsNull(index.SearchByName(ConsoleType.Nes, "Super Mario Bros. (World).nes"));
        Assert.IsNull(index.SearchByName(ConsoleType.Nes, "Super Mario Bros. 4 (USA).nes"));

        // ...and the right sequel still resolves.
        var match = index.SearchByName(ConsoleType.Nes, "Super Mario Bros 3 (USA).nes");
        Assert.IsNotNull(match);
        Assert.AreEqual("Super Mario Bros. 3 (USA)", match.Name);
    }

    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_TreatsRomanAndArabicSequelsAlike()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Snes, "Final Fantasy IV (USA)"),
            new IndexRow(ConsoleType.Snes, "Final Fantasy VI (USA)"));
        using var index = OpenIndex(file);

        var match = index.SearchByName(ConsoleType.Snes, "Final Fantasy VI (USA).sfc");

        Assert.IsNotNull(match);
        Assert.AreEqual("Final Fantasy VI (USA)", match.Name);

        // DAT and file names disagree about roman vs arabic often enough that the two must fold
        // together, or every one of these becomes a miss.
        var arabic = index.SearchByName(ConsoleType.Snes, "Final Fantasy 6 (USA).sfc");
        Assert.IsNotNull(arabic);
        Assert.AreEqual("Final Fantasy VI (USA)", arabic.Name);
    }

    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_WrongConsolePartition_ReturnsNull()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. (World)"));
        using var index = OpenIndex(file);

        Assert.IsNull(index.SearchByName(ConsoleType.Snes, "Super Mario Bros. (World)"));
    }

    /// <summary>ConsoleType.Unknown is the documented "search every partition" wildcard.</summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_UnknownConsole_SearchesEveryPartition()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.MasterSystem, "Sonic The Hedgehog (Europe)"));
        using var index = OpenIndex(file);

        var match = index.SearchByName(ConsoleType.Unknown, "Sonic The Hedgehog (Europe).sms");

        Assert.IsNotNull(match);
        Assert.AreEqual(ConsoleType.MasterSystem, match.ConsoleType);
    }

    /// <summary>
    /// FTS5 has a query language, and the caller-supplied file name goes straight into it. Quotes,
    /// operators and column filters must all be inert text.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_FtsSyntaxInName_IsInertText()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Super Mario Bros. (World)"));
        using var index = OpenIndex(file);

        foreach (var hostile in new[]
                 {
                     "\" OR name : \"mario",
                     "mario\" AND \"bros\" OR \"",
                     "NEAR(mario bros, 2)",
                     "castlevania* OR mario*",
                     "^mario",
                 })
        {
            // Must not throw, and must not be tricked into returning the row by query syntax alone:
            // the terms survive only as inert text, and none of these score near the row's name.
            Assert.IsNull(index.SearchByName(ConsoleType.Nes, hostile), hostile);
        }
    }

    /// <summary>The trigram tokenizer emits nothing below three characters, so there is nothing to ask.</summary>
    [TestMethod]
    public void SqliteMetadataIndex_SearchByName_TooShortToTokenize_ReturnsNull()
    {
        using var file = NoIntroIndexFile.Create(new IndexRow(ConsoleType.Nes, "Ox (World)"));
        using var index = OpenIndex(file);

        Assert.IsNull(index.SearchByName(ConsoleType.Nes, "Ox.nes"));
        Assert.IsNull(index.SearchByName(ConsoleType.Nes, "   "));
    }

    [TestMethod]
    public void SqliteMetadataIndex_VersionAndRowCount_ComeFromMeta()
    {
        using var file = NoIntroIndexFile.Create(
            "2026-01-02T03:04:05Z",
            1,
            new IndexRow(ConsoleType.Nes, "One"),
            new IndexRow(ConsoleType.Snes, "Two"));
        using var index = OpenIndex(file);

        Assert.AreEqual("2026-01-02T03:04:05Z", index.Version);
        Assert.AreEqual(2, index.RowCount);
    }

    /// <summary>
    /// A missing index must not stop the service: DS/DSi/GBA resolve from the ROM header alone.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_Open_MissingFile_ReturnsAnIndexThatAlwaysMisses()
    {
        var missing = Path.Combine(Path.GetTempPath(), "twilight-tests", Guid.NewGuid().ToString("N"), "nointro.db");

        var index = SqliteMetadataIndex.Open(missing, NullLogger.Instance);

        Assert.IsInstanceOfType<NullMetadataIndex>(index);
        Assert.IsFalse(index.TryByCrc32(1u, out _));
        Assert.IsFalse(index.TryBySha1("abc", out _));
        Assert.IsFalse(index.TryBySerial(ConsoleType.NintendoDs, "AMCE", out _));
        Assert.IsNull(index.SearchByName(ConsoleType.Nes, "Super Mario Bros. (World)"));
        Assert.AreEqual(0, index.RowCount);
    }

    /// <summary>
    /// An index built by a newer DatBuilder must be refused, not read with the old column meanings.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_Open_UnsupportedSchema_ReturnsAnIndexThatAlwaysMisses()
    {
        using var file = NoIntroIndexFile.Create(
            "2026-01-01T00:00:00Z", schemaVersion: 2, new IndexRow(ConsoleType.Nes, "Future"));

        var index = SqliteMetadataIndex.Open(file.Path, NullLogger.Instance);

        Assert.IsInstanceOfType<NullMetadataIndex>(index);
        Assert.AreEqual(0, index.RowCount);
    }

    [TestMethod]
    public void SqliteMetadataIndex_Open_ExistingFile_ReturnsTheRealIndex()
    {
        using var file = NoIntroIndexFile.Create(new IndexRow(ConsoleType.Nes, "Real", Crc32: 7u));

        var index = SqliteMetadataIndex.Open(file.Path, NullLogger.Instance);
        using (index as IDisposable)
        {
            Assert.IsInstanceOfType<SqliteMetadataIndex>(index);
            Assert.IsTrue(index.TryByCrc32(7u, out var entry));
            Assert.AreEqual("Real", entry.Name);
        }
    }

    /// <summary>
    /// Lookups are served from a pool of connections with pre-compiled statements, so concurrent use is
    /// a correctness property rather than a performance one.
    /// </summary>
    [TestMethod]
    public void SqliteMetadataIndex_ConcurrentLookups_AllSucceed()
    {
        using var file = NoIntroIndexFile.Create(
            Enumerable.Range(0, 64)
                .Select(i => new IndexRow(ConsoleType.Nes, $"Game {i}", Crc32: (uint)(i + 1)))
                .ToArray());
        using var index = OpenIndex(file);

        var results = new bool[512];
        Parallel.For(0, results.Length, i =>
        {
            var crc = (uint)((i % 64) + 1);
            results[i] = index.TryByCrc32(crc, out var entry) && entry.Name == $"Game {crc - 1}";
        });

        Assert.IsTrue(results.All(r => r));
    }
}
