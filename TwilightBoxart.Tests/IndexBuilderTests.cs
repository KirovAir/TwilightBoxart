using System.Net;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Index;

namespace TwilightBoxart.Tests;

/// <summary>
/// Covers the generated index end to end: a real DAT fixture goes in, a real file-backed SQLite
/// database comes out, and the schema, rows, FTS5 search and dedupe rules are asserted against it.
/// No mocks and no in-memory SQLite: the schema *is* the contract with SqliteMetadataIndex, so it has
/// to be checked on the same storage engine the reader will open.
/// </summary>
[TestClass]
public class IndexBuilderTests
{
    private string _workDirectory = "";

    /// <summary>A minimal but representative No-Intro XML datafile: a normal dump, a serial-bearing
    /// dump, a baddump colliding with a good dump, and a multi-rom game.</summary>
    private const string NoIntroXml = """
        <?xml version="1.0"?>
        <!DOCTYPE datafile PUBLIC "-//Logiqx//DTD ROM Management Datafile//EN" "http://www.logiqx.com/Dats/datafile.dtd">
        <datafile>
          <header>
            <name>Nintendo - Nintendo DS</name>
            <description>Nintendo - Nintendo DS</description>
            <version>20260701-000000</version>
          </header>
          <game name="Mario Kart DS (USA)">
            <description>Mario Kart DS (USA)</description>
            <rom name="Mario Kart DS (USA).nds" size="16777216" crc="AABBCCDD" md5="0123456789abcdef0123456789abcdef" sha1="1111111111111111111111111111111111111111" serial="NTR-AMCE-USA"/>
          </game>
          <game name="New Super Mario Bros. (Europe)">
            <description>New Super Mario Bros. (Europe)</description>
            <rom name="New Super Mario Bros. (Europe).nds" size="33554432" crc="11223344" sha1="2222222222222222222222222222222222222222" serial="NTR-A2DP-EUR"/>
          </game>
          <game name="Nintendogs - Lab &amp; Friends (USA)">
            <rom name="Nintendogs - Lab &amp; Friends (USA).nds" size="16777216" crc="DEADBEEF" sha1="3333333333333333333333333333333333333333"/>
          </game>
          <game name="Multi Part Game (Japan)">
            <rom name="Multi Part Game (Japan) (Disc 1).nds" size="1024" crc="0000000A" sha1="4444444444444444444444444444444444444444"/>
            <rom name="Multi Part Game (Japan) (Disc 2).nds" size="1024" crc="0000000B" sha1="5555555555555555555555555555555555555555"/>
          </game>
          <game name="No Hash Game (World)">
            <rom name="No Hash Game (World).nds" size="0" status="nodump" serial="NTR-XXXX-WLD"/>
          </game>
        </datafile>
        """;

    /// <summary>The ClrMamePro text dialect libretro-database mirrors No-Intro in.</summary>
    private const string ClrMameProDat = """
        clrmamepro (
        	name "Nintendo - Game Boy"
        	description "Nintendo - Game Boy"
        	version 20260701
        )

        game (
        	name "Tetris (World) (Rev 1)"
        	description "Tetris (World) (Rev 1)"
        	rom ( name "Tetris (World) (Rev 1).gb" size 32768 crc 1A2B3C4D md5 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa sha1 6666666666666666666666666666666666666666 )
        )

        game (
        	name "Super Mario Land (World)"
        	serial "DMG-ML-USA"
        	rom ( name "Super Mario Land (World).gb" size 65536 crc CAFEBABE sha1 7777777777777777777777777777777777777777 flags baddump )
        )
        """;

    [TestInitialize]
    public void SetUp()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "twilightboxart-indexbuilder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test run over.
        }
    }

    // parsing

    [TestMethod]
    public void DatParser_NoIntroXml_ReadsHeaderGamesAndSerials()
    {
        var document = DatParser.Parse(NoIntroXml, ConsoleType.NintendoDs, "test");

        Assert.AreEqual("Nintendo - Nintendo DS", document.HeaderName);
        Assert.AreEqual("20260701-000000", document.HeaderVersion);

        // Five games, six roms: the multi-part game contributes one row per rom so each stays
        // findable by its own CRC32.
        Assert.AreEqual(5, document.GameCount);
        Assert.AreEqual(6, document.Entries.Count);

        var mario = document.Entries.Single(e => e.Name == "Mario Kart DS (USA)");
        Assert.AreEqual(ConsoleType.NintendoDs, mario.Console);
        Assert.AreEqual("NTR-AMCE-USA", mario.Serial);
        Assert.AreEqual(0xAABBCCDDu, mario.Crc32);
        Assert.AreEqual("1111111111111111111111111111111111111111", mario.Sha1);
        Assert.IsNull(mario.Status);

        // The first game after </header> is the one a naive XmlReader loop drops.
        Assert.IsTrue(document.Entries.Any(e => e.Name == "Mario Kart DS (USA)"));

        // XML entities in names must survive: libretro-thumbnails file names contain the decoded form.
        Assert.IsTrue(document.Entries.Any(e => e.Name == "Nintendogs - Lab & Friends (USA)"));

        var nodump = document.Entries.Single(e => e.Name == "No Hash Game (World)");
        Assert.AreEqual("nodump", nodump.Status);
        Assert.IsNull(nodump.Crc32);
        Assert.AreEqual("NTR-XXXX-WLD", nodump.Serial);
    }

    [TestMethod]
    public void DatParser_ClrMameProText_ReadsRomsAndFlags()
    {
        var document = DatParser.Parse(ClrMameProDat, ConsoleType.GameBoy, "test");

        Assert.AreEqual("Nintendo - Game Boy", document.HeaderName);
        Assert.AreEqual(2, document.Entries.Count);

        var tetris = document.Entries.Single(e => e.Name == "Tetris (World) (Rev 1)");
        Assert.AreEqual(0x1A2B3C4Du, tetris.Crc32);
        Assert.AreEqual("6666666666666666666666666666666666666666", tetris.Sha1);

        // `flags baddump` is the text dialect's spelling of the XML `status` attribute.
        var mario = document.Entries.Single(e => e.Name == "Super Mario Land (World)");
        Assert.AreEqual("baddump", mario.Status);

        // A <serial> on the game applies to a rom that carries none of its own.
        Assert.AreEqual("DMG-ML-USA", mario.Serial);
    }

    [TestMethod]
    public void DatParser_DialectIsSniffedNotConfigured()
    {
        Assert.IsTrue(DatParser.LooksLikeXml(NoIntroXml));
        Assert.IsFalse(DatParser.LooksLikeXml(ClrMameProDat));
    }

    [TestMethod]
    public void DatParser_FamicomDiskSystem_ReducesSerialToTheBareGameCode()
    {
        // No-Intro writes FDS serials as "FMC-ZEL" (sometimes "FMC-MET-JPN"), but the disk header carries
        // only the middle code. The builder reduces them so the header-serial rung matches; every other
        // console keeps its serial exactly as No-Intro wrote it.
        const string fds = """
            <datafile>
              <game name="Zelda no Densetsu (Japan)">
                <rom name="Zelda.fds" size="131000" crc="AAAAAAAA" serial="FMC-ZEL"/>
              </game>
              <game name="Metroid (Japan)">
                <rom name="Metroid.fds" size="131000" crc="BBBBBBBB" serial="FMC-MET-JPN"/>
              </game>
            </datafile>
            """;

        var fdsDoc = DatParser.Parse(fds, ConsoleType.FamicomDiskSystem, "test");
        CollectionAssert.AreEquivalent(
            new[] { "ZEL", "MET" }, fdsDoc.Entries.Select(e => e.Serial).ToArray());

        var nesDoc = DatParser.Parse(fds, ConsoleType.Nes, "test");
        CollectionAssert.AreEquivalent(
            new[] { "FMC-ZEL", "FMC-MET-JPN" }, nesDoc.Entries.Select(e => e.Serial).ToArray());
    }

    [TestMethod]
    public void DatFields_CrcParsing_IsCultureInvariant()
    {
        // The 2020 client used culture-sensitive casing and skipped every .ZIP under tr-TR.
        // Anything the builder parses has to be immune to the CI runner's locale.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.AreEqual(0xABCDEF01u, DatFields.ParseCrc32("abcdef01"));
            Assert.AreEqual(0xABCDEF01u, DatFields.ParseCrc32("ABCDEF01"));
            Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DatFields.ParseSha1("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
            Assert.AreEqual("NTR-AMCE-USA", DatFields.ParseSerial("ntr-amce-usa"));
            Assert.AreEqual("baddump", DatEntryQuality.Normalize("BADDUMP"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void DatFields_MalformedHashes_AreTreatedAsAbsent()
    {
        Assert.IsNull(DatFields.ParseCrc32("not-hex"));
        Assert.IsNull(DatFields.ParseCrc32(""));
        Assert.IsNull(DatFields.ParseSha1("tooshort"));
        Assert.IsNull(DatFields.ParseSha1("zzzz111111111111111111111111111111111111"));

        // A DAT crc of 00000000 is a declared value, not the 7z "unknown" sentinel, so it survives.
        Assert.AreEqual(0u, DatFields.ParseCrc32("00000000"));
    }

    // dedupe

    [TestMethod]
    public void EntryDeduplicator_SameCrcWithBaddump_KeepsTheGoodDump()
    {
        var good = Entry("Contra (USA)", crc: 0x1234, sha1: "a".PadRight(40, 'a'));
        var bad = Entry("Contra (USA) [b]", crc: 0x1234, sha1: null, status: "baddump");

        // Order reversed on purpose: the winner must come from the rule, not from load order.
        var (entries, report) = EntryDeduplicator.Deduplicate([bad, good]);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Contra (USA)", entries[0].Name);
        Assert.IsNull(entries[0].Status);
        Assert.AreEqual(1, report.Crc32Duplicates);
        Assert.AreEqual(1, report.BadDumpsSuperseded);
    }

    [TestMethod]
    public void EntryDeduplicator_Winner_InheritsFieldsItLacked()
    {
        // The good dump has no serial; the baddump it supersedes does. Same bytes, so the serial
        // describes the survivor just as well.
        var good = Entry("Contra (USA)", crc: 0x1234);
        var bad = Entry("Contra (USA) [b]", crc: 0x1234, serial: "NES-CT-USA", status: "baddump");

        var (entries, _) = EntryDeduplicator.Deduplicate([good, bad]);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Contra (USA)", entries[0].Name);
        Assert.AreEqual("NES-CT-USA", entries[0].Serial);
    }

    [TestMethod]
    public void EntryDeduplicator_IdenticalRowsFromTwoDats_CollapseToOne()
    {
        var a = Entry("Tetris (World)", crc: 0x99, sha1: "b".PadRight(40, 'b'), source: "no-intro");
        var b = Entry("Tetris (World)", crc: 0x99, sha1: "b".PadRight(40, 'b'), source: "libretro");

        var (entries, report) = EntryDeduplicator.Deduplicate([a, b]);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(1, report.ExactDuplicates);
    }

    [TestMethod]
    public void EntryDeduplicator_TrueCrc32Collision_KeepsBothRowsAndClearsCrc()
    {
        // Two different ROMs (different SHA-1) that happen to share 32 bits. Over 42k rows the birthday
        // odds make this likely. Answering a CRC lookup from either would be a coin flip, so neither
        // stays reachable by CRC - but both stay reachable by SHA-1, serial and name.
        var one = Entry("Game One (USA)", crc: 0x5555, sha1: "c".PadRight(40, 'c'));
        var two = Entry("Game Two (Japan)", crc: 0x5555, sha1: "d".PadRight(40, 'd'));

        var (entries, report) = EntryDeduplicator.Deduplicate([one, two]);

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Crc32 is null));
        Assert.IsTrue(entries.All(e => e.Sha1 is not null));
        Assert.AreEqual(2, report.Crc32CollisionsCleared);
    }

    [TestMethod]
    public void EntryDeduplicator_SameSerialDifferentRevisions_KeepsBothWithBestFirst()
    {
        // Two revisions share a serial but not a CRC. Collapsing them would make the dropped revision
        // unfindable by CRC, so both survive; the better row must be written first so an unordered
        // LIMIT 1 in the reader lands on it.
        var revised = Entry("Zelda (USA) (Rev 1)", crc: 0xAAA1, serial: "NTR-AZLE-USA");
        var proto = Entry("Zelda (USA) (Proto)", crc: 0xAAA2, serial: "NTR-AZLE-USA");

        var (entries, _) = EntryDeduplicator.Deduplicate([proto, revised]);
        var ordered = EntryDeduplicator.Order(entries);

        Assert.AreEqual(2, ordered.Count);
        Assert.AreEqual("Zelda (USA) (Rev 1)", ordered[0].Name);
        Assert.AreEqual("Zelda (USA) (Proto)", ordered[1].Name);
    }

    [TestMethod]
    public void EntryDeduplicator_SameSerialWithBaddump_OrdersTheGoodDumpFirst()
    {
        // This is the exact case the 2020 backend handled per-request with the "// lol" filter: several
        // rows share a serial and one is a bad dump. They are NOT collapsed - a baddump has different
        // bytes and therefore a different CRC, so dropping it would lose a real CRC lookup. Instead the
        // good dump is written first, and an unordered `WHERE console = ? AND serial = ? LIMIT 1` in the
        // reader lands on it.
        var bad = Entry("Sonic (USA) [b]", crc: 0xB4D0, serial: "MK-1001", status: "baddump");
        var good = Entry("Sonic the Hedgehog (USA, Europe)", crc: 0x600D, serial: "MK-1001");

        var (entries, report) = EntryDeduplicator.Deduplicate([bad, good]);
        var ordered = EntryDeduplicator.Order(entries);

        Assert.AreEqual(2, ordered.Count, "different bytes must stay separately findable by CRC");
        Assert.AreEqual(0, report.Crc32Duplicates);
        Assert.AreEqual("Sonic the Hedgehog (USA, Europe)", ordered[0].Name);
        Assert.AreEqual("baddump", ordered[1].Status);

        // Ordinal name ordering must not be what decided this - "Sonic (USA) [b]" sorts first.
        Assert.IsTrue(string.CompareOrdinal("Sonic (USA) [b]", "Sonic the Hedgehog (USA, Europe)") < 0);
    }

    [TestMethod]
    public void EntryDeduplicator_NodumpRow_LosesToEverything()
    {
        // A nodump entry carries no hash at all; it exists only so a header serial can still name the
        // game. It must never outrank a real dump sharing its serial.
        var nodump = Entry("A Game (USA)", serial: "NTR-AAAA-USA", status: "nodump");
        var good = Entry("Z Game (USA)", crc: 0x1234, serial: "NTR-AAAA-USA");

        var ordered = EntryDeduplicator.Order(EntryDeduplicator.Deduplicate([nodump, good]).Entries);

        Assert.AreEqual("Z Game (USA)", ordered[0].Name);
    }

    [TestMethod]
    public void EntryDeduplicator_UnknownStatus_SortsBetweenGoodAndBaddump()
    {
        // An unrecognised status means we do not know what it means: it must not beat a known-good dump,
        // and must not be discarded like a known-bad one.
        Assert.IsTrue(DatEntryQuality.DumpRank(null) < DatEntryQuality.DumpRank("weird"));
        Assert.IsTrue(DatEntryQuality.DumpRank("weird") < DatEntryQuality.DumpRank("baddump"));
        Assert.IsTrue(DatEntryQuality.DumpRank("baddump") < DatEntryQuality.DumpRank("nodump"));
        Assert.AreEqual(DatEntryQuality.DumpRank(null), DatEntryQuality.DumpRank("good"));

        // A game called "Bad Mojo" is not a bad dump - the 2020 code's Status.Contains("bad") said it was.
        Assert.AreEqual(0, DatEntryQuality.DumpRank(null));
        Assert.AreEqual(0, DatEntryQuality.Rank(Entry("Bad Mojo (USA)")));
    }

    [TestMethod]
    public void EntryDeduplicator_SerialNamingUnrelatedGames_IsCleared()
    {
        // Real data: DS game code AGEE is listed for both of these. A serial lookup would answer with
        // whichever row it reached first, and the serial rung runs before CRC32 in the ladder - so the
        // wrong answer would preempt the right one.
        var goldeneye = Entry("GoldenEye - Rogue Agent (USA)", crc: 0x1111, serial: "AGEE", console: ConsoleType.NintendoDs);
        var starWars = Entry("Star Wars - The Force Unleashed (USA)", crc: 0x2222, serial: "AGEE", console: ConsoleType.NintendoDs);

        var (entries, report) = EntryDeduplicator.Deduplicate([goldeneye, starWars]);

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Serial is null), "an ambiguous serial must not answer a lookup");
        Assert.AreEqual(2, report.AmbiguousSerialsCleared);

        // The rows stay findable by every other rung.
        CollectionAssert.AreEquivalent(new uint?[] { 0x1111, 0x2222 }, entries.Select(e => e.Crc32).ToArray());
    }

    [TestMethod]
    public void EntryDeduplicator_SerialSharedByRevisionsOfOneGame_IsKept()
    {
        // The counterpart: revisions and regional dumps of a single game share a serial legitimately and
        // share box art, so the serial stays usable. Only *different* games disqualify it.
        var rev0 = Entry("Mario Kart DS (USA)", crc: 0x1111, serial: "AMCE", console: ConsoleType.NintendoDs);
        var rev1 = Entry("Mario Kart DS (USA) (Rev 1)", crc: 0x2222, serial: "AMCE", console: ConsoleType.NintendoDs);
        var demo = Entry("Mario Kart DS (USA) (Demo)", crc: 0x3333, serial: "AMCE", console: ConsoleType.NintendoDs);

        var (entries, report) = EntryDeduplicator.Deduplicate([demo, rev1, rev0]);

        Assert.AreEqual(3, entries.Count);
        Assert.IsTrue(entries.All(e => e.Serial == "AMCE"));
        Assert.AreEqual(0, report.AmbiguousSerialsCleared);

        // And the shipped release must sort ahead of the demo, since all three answer the same serial.
        var ordered = EntryDeduplicator.Order(entries);
        Assert.AreEqual("Mario Kart DS (USA)", ordered[0].Name);
        Assert.AreEqual("Mario Kart DS (USA) (Demo)", ordered[^1].Name);
    }

    [TestMethod]
    public void EntryDeduplicator_Order_IsStableAcrossInputPermutations()
    {
        var entries = new[]
        {
            Entry("B Game", crc: 2, sha1: "1".PadRight(40, '1')),
            Entry("A Game", crc: 1, sha1: "2".PadRight(40, '2')),
            Entry("C Game", crc: 3, sha1: "3".PadRight(40, '3')),
        };

        var forward = EntryDeduplicator.Order(EntryDeduplicator.Deduplicate(entries).Entries);
        var reversed = EntryDeduplicator.Order(EntryDeduplicator.Deduplicate(entries.Reverse().ToArray()).Entries);

        CollectionAssert.AreEqual(
            forward.Select(e => e.Name).ToArray(),
            reversed.Select(e => e.Name).ToArray());
    }

    // the generated database

    [TestMethod]
    public void IndexWriter_Write_ProducesTheDeclaredSchema()
    {
        var path = BuildFixtureIndex(out _);

        using var connection = OpenReadOnly(path);

        var tables = Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
        CollectionAssert.Contains(tables, "entry");
        CollectionAssert.Contains(tables, "meta");
        CollectionAssert.Contains(tables, "entry_fts");

        // Column order and affinities are part of the contract with SqliteMetadataIndex.
        var columns = Query(connection, "SELECT name || ':' || type FROM pragma_table_info('entry');");
        CollectionAssert.AreEqual(
            new[] { "id:INTEGER", "console:INTEGER", "name:TEXT", "serial:TEXT", "crc32:INTEGER", "sha1:TEXT", "status:TEXT" },
            columns);

        var indexes = Query(connection,
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_%' ORDER BY name;");
        CollectionAssert.AreEqual(new[] { "ix_entry_crc32", "ix_entry_serial", "ix_entry_sha1" }, indexes);

        // Partial indexes: ~60% of rows have no serial and NES has none at all, so the NULLs stay out.
        var serialIndexSql = Scalar(connection, "SELECT sql FROM sqlite_master WHERE name = 'ix_entry_serial';");
        StringAssert.Contains(serialIndexSql, "WHERE serial IS NOT NULL");
    }

    [TestMethod]
    public void IndexWriter_Write_PopulatesMetaRows()
    {
        var path = BuildFixtureIndex(out var expectedRowCount);

        using var connection = OpenReadOnly(path);

        Assert.AreEqual("1", Scalar(connection, "SELECT value FROM meta WHERE key = 'schema';"));
        Assert.AreEqual("2026-07-20T12:00:00Z", Scalar(connection, "SELECT value FROM meta WHERE key = 'version';"));
        Assert.AreEqual(
            expectedRowCount.ToString(CultureInfo.InvariantCulture),
            Scalar(connection, "SELECT value FROM meta WHERE key = 'rowCount';"));
        Assert.AreEqual(expectedRowCount, int.Parse(Scalar(connection, "SELECT COUNT(*) FROM entry;"), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void IndexWriter_Write_StoresCrc32AsSigned32BitAndRoundTrips()
    {
        // 0xAABBCCDD is > int.MaxValue; the schema stores the signed reinterpretation and the reader
        // casts back. This is the assertion that catches a writer/reader disagreement about the cast.
        var path = BuildFixtureIndex(out _);

        using var connection = OpenReadOnly(path);
        var stored = long.Parse(
            Scalar(connection, "SELECT crc32 FROM entry WHERE name = 'Mario Kart DS (USA)';"),
            CultureInfo.InvariantCulture);

        Assert.AreEqual(unchecked((int)0xAABBCCDD), (int)stored);
        Assert.AreEqual(0xAABBCCDDu, unchecked((uint)(int)stored));
    }

    [TestMethod]
    public void IndexWriter_Write_LandsRowsWithTheirSerialAndConsole()
    {
        var path = BuildFixtureIndex(out _);

        using var connection = OpenReadOnly(path);

        Assert.AreEqual("NTR-AMCE-USA",
            Scalar(connection, "SELECT serial FROM entry WHERE name = 'Mario Kart DS (USA)';"));
        Assert.AreEqual(((int)ConsoleType.NintendoDs).ToString(CultureInfo.InvariantCulture),
            Scalar(connection, "SELECT console FROM entry WHERE name = 'Mario Kart DS (USA)';"));
        Assert.AreEqual(((int)ConsoleType.GameBoy).ToString(CultureInfo.InvariantCulture),
            Scalar(connection, "SELECT console FROM entry WHERE name = 'Tetris (World) (Rev 1)';"));

        // The multi-rom game contributed two rows sharing one name.
        Assert.AreEqual("2",
            Scalar(connection, "SELECT COUNT(*) FROM entry WHERE name = 'Multi Part Game (Japan)';"));
    }

    [TestMethod]
    public void IndexWriter_Fts5TrigramSearch_FindsTheExpectedRow()
    {
        var path = BuildFixtureIndex(out _);

        using var connection = OpenReadOnly(path);

        var name = Scalar(connection, """
            SELECT e.name FROM entry_fts f
            JOIN entry e ON e.id = f.rowid
            WHERE entry_fts MATCH 'mario kart'
            ORDER BY rank
            LIMIT 1;
            """);
        Assert.AreEqual("Mario Kart DS (USA)", name);

        // Trigram means substring matching, case-insensitively, without a leading-token anchor -
        // this is the property that replaces PostgreSQL's pg_trgm.
        var substring = Query(connection, """
            SELECT e.name FROM entry_fts f
            JOIN entry e ON e.id = f.rowid
            WHERE entry_fts MATCH '"uper mario"'
            ORDER BY e.name;
            """);
        CollectionAssert.Contains(substring, "Super Mario Land (World)");
    }

    [TestMethod]
    public void IndexBuilder_LocalDirectory_WritesTheDatabase()
    {
        var input = Path.Combine(_workDirectory, "dats");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "Nintendo - Nintendo DS.dat"), NoIntroXml);
        File.WriteAllText(Path.Combine(input, "Nintendo - Game Boy.dat"), ClrMameProDat);

        var output = Path.Combine(_workDirectory, "out", "nointro.db");
        var options = new BuildOptions
        {
            OutputPath = output,
            InputDirectory = input,
            Version = "2026-07-20T12:00:00Z",
        };

        var result = new IndexBuilder(options, _ => { }).RunAsync().GetAwaiter().GetResult();

        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual("2026-07-20T12:00:00Z", result.Version);
        Assert.AreEqual(8, result.RowCount);

        // Missing sources are reported rather than swallowed: only two of the catalog's consoles were
        // supplied, so the other ten must be named.
        Assert.IsTrue(result.MissingSources.Count > 0);
        CollectionAssert.Contains(result.MissingSources.ToArray(), "Nintendo - Super Nintendo Entertainment System");

        // The build stamp is the only timestamp anywhere, so the printed report is reproducible.
        var report = BuildReport.Render(result);
        StringAssert.Contains(report, "Serial coverage");
        StringAssert.Contains(report, "2026-07-20T12:00:00Z");

        // No temp file may survive a successful build.
        Assert.IsFalse(File.Exists(output + ".tmp"));
    }

    [TestMethod]
    public void IndexBuilder_RunTwice_ProducesAByteIdenticalDatabase()
    {
        var input = Path.Combine(_workDirectory, "dats");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "Nintendo - Nintendo DS.dat"), NoIntroXml);
        File.WriteAllText(Path.Combine(input, "Nintendo - Game Boy.dat"), ClrMameProDat);

        var first = Build(Path.Combine(_workDirectory, "a", "nointro.db"), input);
        var second = Build(Path.Combine(_workDirectory, "b", "nointro.db"), input);

        // The determinism requirement: the same DATs and the same declared version give the same bytes,
        // so a changed output means an upstream DAT actually changed.
        CollectionAssert.AreEqual(
            File.ReadAllBytes(first.DatabasePath),
            File.ReadAllBytes(second.DatabasePath));

        BuildResult Build(string output, string inputDirectory) =>
            new IndexBuilder(
                new BuildOptions
                {
                    OutputPath = output,
                    InputDirectory = inputDirectory,
                    Version = "2026-07-20T12:00:00Z",
                },
                _ => { }).RunAsync().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void IndexWriter_Write_ReplacesAnExistingIndexAtomically()
    {
        var path = Path.Combine(_workDirectory, "nointro.db");
        IndexWriter.Write(path, [Entry("First Game", crc: 1)], "2026-01-01T00:00:00Z");
        var firstLength = new FileInfo(path).Length;

        IndexWriter.Write(path, [Entry("Second Game", crc: 2), Entry("Third Game", crc: 3)], "2026-02-01T00:00:00Z");

        Assert.IsTrue(firstLength > 0);
        Assert.IsFalse(File.Exists(path + ".tmp"));

        using var connection = OpenReadOnly(path);
        Assert.AreEqual("2026-02-01T00:00:00Z", Scalar(connection, "SELECT value FROM meta WHERE key = 'version';"));
        CollectionAssert.AreEqual(
            new[] { "Second Game", "Third Game" },
            Query(connection, "SELECT name FROM entry ORDER BY id;"));
    }

    // catalog

    [TestMethod]
    public void DatCatalog_SiblingNoIntroSets_FoldIntoOneConsoleType()
    {
        // The 2020 crawler merged these for a reason: a DS dump's SHA-1 is regularly listed in the DSi
        // set and vice versa, so keeping them apart loses matches.
        Assert.IsTrue(DatCatalog.Default.TryResolve("Nintendo - Nintendo DS (Download Play)", out var downloadPlay));
        Assert.AreEqual(ConsoleType.NintendoDs, downloadPlay);

        Assert.IsTrue(DatCatalog.Default.TryResolve("Nintendo - Nintendo DSi (Digital)", out var dsiDigital));
        Assert.AreEqual(ConsoleType.NintendoDsi, dsiDigital);

        Assert.IsTrue(DatCatalog.Default.TryResolve("Nintendo - Nintendo DSi", out var dsi));
        Assert.AreEqual(ConsoleType.NintendoDsi, dsi);

        Assert.IsFalse(DatCatalog.Default.TryResolve("Sony - PlayStation", out _));
    }

    [TestMethod]
    public void DatCatalog_NameResolution_IsCaseInsensitiveAndCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // tr-TR: "I".ToLower() is a dotless i, which breaks any culture-sensitive comparison of
            // "Nintendo - Nintendo DSi" against itself.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.IsTrue(DatCatalog.Default.TryResolve("NINTENDO - NINTENDO DSI (DIGITAL)", out var console));
            Assert.AreEqual(ConsoleType.NintendoDsi, console);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void DatSource_ResolveUrl_SubstitutesAndEscapesTheName()
    {
        var source = new DatSource { Name = "Sega - Mega Drive - Genesis", Console = ConsoleType.MegaDrive };

        Assert.AreEqual(
            "https://example.invalid/Sega%20-%20Mega%20Drive%20-%20Genesis.dat",
            source.ResolveUrl("https://example.invalid/{name}.dat"));

        // An explicit URL wins, so a single awkward DAT can be pointed elsewhere without a code change.
        var pinned = source with { Url = "https://elsewhere.invalid/md.dat" };
        Assert.AreEqual("https://elsewhere.invalid/md.dat", pinned.ResolveUrl("https://example.invalid/{name}.dat"));
    }


    // helpers

    private string BuildFixtureIndex(out int rowCount)
    {
        var entries = new List<DatEntry>();
        entries.AddRange(DatParser.Parse(NoIntroXml, ConsoleType.NintendoDs, "test").Entries);
        entries.AddRange(DatParser.Parse(ClrMameProDat, ConsoleType.GameBoy, "test").Entries);

        var (deduped, _) = EntryDeduplicator.Deduplicate(entries);
        var ordered = EntryDeduplicator.Order(deduped);
        rowCount = ordered.Count;

        var path = Path.Combine(_workDirectory, "nointro.db");
        IndexWriter.Write(path, ordered, "2026-07-20T12:00:00Z");
        return path;
    }

    /// <summary>Opens the built file exactly as the runtime reader will: read-only, shared cache.</summary>
    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString());

        connection.Open();
        return connection;
    }

    private static string[] Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? "";
    }

    private static DatEntry Entry(
        string name,
        uint? crc = null,
        string? sha1 = null,
        string? serial = null,
        string? status = null,
        string source = "test",
        ConsoleType console = ConsoleType.Nes) => new()
        {
            Console = console,
            Name = name,
            Crc32 = crc,
            Sha1 = sha1,
            Serial = serial,
            Status = status,
            SourceName = source,
        };

    // fetching
    //
    // The cache directory is an HTTP cache, not a snapshot. These pin the three behaviours that
    // make the admin panel's rebuild honest: revalidate instead of trusting the cache forever,
    // take a fresh 200 over the cache, and serve from the cache when the mirror is down.

    private static readonly DatSource GameBoySource = new()
    {
        Name = "Nintendo - Game Boy",
        Console = ConsoleType.GameBoy,
    };

    private const string FetchTemplate = "https://example.invalid/{name}.dat";

    /// <summary>One scripted response (or throw) per request; records each If-None-Match at send time.</summary>
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] script)
        : HttpMessageHandler
    {
        public List<string?> IfNoneMatch { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            IfNoneMatch.Add(request.Headers.TryGetValues("If-None-Match", out var values)
                ? string.Join(",", values)
                : null);
            var step = script[Math.Min(IfNoneMatch.Count - 1, script.Length - 1)];
            return Task.FromResult(step(request));
        }
    }

    private static HttpResponseMessage Dat(string text, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    [TestMethod]
    public async Task DatFetcher_RevalidatesTheCacheInsteadOfTrustingItForever()
    {
        var handler = new ScriptedHandler(
            _ => Dat("first", "\"v1\""),
            _ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var fetcher = new DatFetcher(Path.Combine(_workDirectory, "dat-cache"), handler, TimeSpan.Zero);

        var first = await fetcher.FetchAsync(GameBoySource, FetchTemplate);
        Assert.AreEqual("first", first!.Text);
        Assert.IsNull(handler.IfNoneMatch[0], "nothing cached yet, so nothing to revalidate");

        var second = await fetcher.FetchAsync(GameBoySource, FetchTemplate);
        Assert.AreEqual("first", second!.Text, "a 304 serves the cached copy");
        Assert.AreEqual("\"v1\"", handler.IfNoneMatch[1], "the stored ETag must ride the second request");
        StringAssert.Contains(second.Origin, "not modified");
    }

    [TestMethod]
    public async Task DatFetcher_TakesAFresh200OverTheCache()
    {
        var handler = new ScriptedHandler(
            _ => Dat("old", "\"v1\""),
            _ => Dat("new", "\"v2\""),
            _ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var fetcher = new DatFetcher(Path.Combine(_workDirectory, "dat-cache"), handler, TimeSpan.Zero);

        await fetcher.FetchAsync(GameBoySource, FetchTemplate);
        var updated = await fetcher.FetchAsync(GameBoySource, FetchTemplate);
        Assert.AreEqual("new", updated!.Text, "the mirror moved on, so must the cache");

        var third = await fetcher.FetchAsync(GameBoySource, FetchTemplate);
        Assert.AreEqual("new", third!.Text);
        Assert.AreEqual("\"v2\"", handler.IfNoneMatch[2], "the replaced cache must revalidate with the NEW tag");
    }

    [TestMethod]
    public async Task DatFetcher_ServesTheCacheWhenTheMirrorIsDown()
    {
        var handler = new ScriptedHandler(
            _ => Dat("kept", "\"v1\""),
            _ => throw new HttpRequestException("mirror on fire"));
        using var fetcher = new DatFetcher(Path.Combine(_workDirectory, "dat-cache"), handler, TimeSpan.Zero);

        await fetcher.FetchAsync(GameBoySource, FetchTemplate);
        var offline = await fetcher.FetchAsync(GameBoySource, FetchTemplate);

        Assert.AreEqual("kept", offline!.Text, "last month's data beats no data");
        StringAssert.Contains(offline.Origin, "mirror unreachable");
        Assert.AreEqual(4, handler.IfNoneMatch.Count, "one priming GET plus three retried attempts");
    }

    [TestMethod]
    public async Task DatFetcher_A404WithNothingCachedIsNull()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var fetcher = new DatFetcher(cacheDirectory: null, handler, TimeSpan.Zero);

        Assert.IsNull(await fetcher.FetchAsync(GameBoySource, FetchTemplate),
            "an optional source that a mirror does not carry is a skip, not a failure");
    }
}
