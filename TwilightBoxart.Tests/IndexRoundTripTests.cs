using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Identify;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Index;

namespace TwilightBoxart.Tests;

/// <summary>
/// The writer/reader seam: <see cref="IndexWriter"/> produces <c>nointro.db</c> and
/// <see cref="SqliteMetadataIndex"/> consumes it, in two different projects that never reference each
/// other.
/// </summary>
/// <remarks>
/// Every other test on either side works against a fixture that restates the DDL by hand, so both
/// could agree with their own copy of the schema and still disagree with each other. These tests are
/// the only place the real writer's output is opened by the real reader. If the schema changes, this
/// is what is supposed to break.
/// </remarks>
[TestClass]
public class IndexRoundTripTests
{
    private string _workDirectory = "";

    [TestInitialize]
    public void Initialize()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "twilight-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a green run over.
        }
    }

    /// <summary>
    /// Deliberately includes 0xC1F8B000, whose signed 32-bit form is negative. The index stores CRC32
    /// as a signed INTEGER, so a writer and reader that disagree about the cast lose half the keyspace
    /// and would lose it silently, because the other half keeps working.
    /// </summary>
    private static readonly DatEntry[] Entries =
    [
        new()
        {
            Console = ConsoleType.NintendoDs,
            Name = "Mario Kart DS (USA)",
            Serial = "AMCE",
            Crc32 = 0xAABBCCDD,
            Sha1 = "1111111111111111111111111111111111111111",
        },
        new()
        {
            Console = ConsoleType.NintendoDs,
            Name = "New Super Mario Bros. (Europe)",
            Serial = "A2DP",
            Crc32 = 0xC1F8B000,
            Sha1 = "2222222222222222222222222222222222222222",
        },
        new()
        {
            Console = ConsoleType.GameBoy,
            Name = "Tetris (World) (Rev 1)",
            Crc32 = 0x0000000A,
            Sha1 = "3333333333333333333333333333333333333333",
        },
    ];

    private SqliteMetadataIndex WriteAndOpen()
    {
        var path = Path.Combine(_workDirectory, IndexWriter.DefaultFileName);
        IndexWriter.Write(path, Entries, "2026-07-20T00:00:00Z");
        return new SqliteMetadataIndex(path, NullLogger.Instance);
    }

    [TestMethod]
    public void IndexRoundTrip_WriterOutput_OpensWithTheRealReader()
    {
        using var index = WriteAndOpen();

        Assert.AreEqual("2026-07-20T00:00:00Z", index.Version);
        Assert.AreEqual(Entries.Length, index.RowCount);
    }

    [TestMethod]
    public void IndexRoundTrip_SerialWrittenByTheBuilder_IsFoundByTheReader()
    {
        using var index = WriteAndOpen();

        Assert.IsTrue(index.TryBySerial(ConsoleType.NintendoDs, "AMCE", out var entry));
        Assert.AreEqual("Mario Kart DS (USA)", entry.Name);
        Assert.AreEqual(ConsoleType.NintendoDs, entry.ConsoleType);
    }

    /// <summary>
    /// The console column is the enum's numeric value, so the reader must map it back to the same
    /// member. A serial that exists on one console must not resolve on another.
    /// </summary>
    [TestMethod]
    public void IndexRoundTrip_SerialLookup_IsPartitionedByConsole()
    {
        using var index = WriteAndOpen();

        Assert.IsFalse(index.TryBySerial(ConsoleType.GameBoy, "AMCE", out _));
    }

    [TestMethod]
    public void IndexRoundTrip_Crc32AboveIntMaxValue_SurvivesTheSignedColumn()
    {
        using var index = WriteAndOpen();

        Assert.IsTrue(index.TryByCrc32(0xC1F8B000, out var entry),
            "0xC1F8B000 is negative as a signed 32-bit integer; writer and reader must agree on the cast.");
        Assert.AreEqual("New Super Mario Bros. (Europe)", entry.Name);
        Assert.AreEqual(0xC1F8B000u, entry.Crc32);
    }

    [TestMethod]
    public void IndexRoundTrip_Crc32BelowIntMaxValue_IsFoundToo()
    {
        using var index = WriteAndOpen();

        Assert.IsTrue(index.TryByCrc32(0xAABBCCDD, out var entry));
        Assert.AreEqual("Mario Kart DS (USA)", entry.Name);
    }

    [TestMethod]
    public void IndexRoundTrip_Sha1_IsStoredAndMatchedInLowercaseHex()
    {
        using var index = WriteAndOpen();

        Assert.IsTrue(index.TryBySha1("3333333333333333333333333333333333333333", out var entry));
        Assert.AreEqual("Tetris (World) (Rev 1)", entry.Name);
    }

    /// <summary>
    /// <c>entry_fts</c> is external-content FTS5: it holds no data until the writer runs
    /// <c>INSERT INTO entry_fts(entry_fts) VALUES('rebuild')</c>. Skip that and every name search
    /// silently returns nothing, which is indistinguishable from "no match" at the call site.
    /// </summary>
    [TestMethod]
    public void IndexRoundTrip_NameSearch_FindsRowsTheBuilderIndexed()
    {
        using var index = WriteAndOpen();

        var match = index.SearchByName(ConsoleType.NintendoDs, "Mario Kart DS (USA).nds");

        Assert.IsNotNull(match, "the FTS5 content table must have been rebuilt by the writer");
        Assert.AreEqual("Mario Kart DS (USA)", match.Name);
    }

    [TestMethod]
    public void IndexRoundTrip_NameSearch_SubstringMatchesViaTheTrigramTokenizer()
    {
        using var index = WriteAndOpen();

        Assert.IsNotNull(index.SearchByName(ConsoleType.NintendoDs, "New Super Mario Bros. (Europe)"));
    }
}
