using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Identify;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Tests.Fixtures;

namespace TwilightBoxart.Tests;

/// <summary>
/// The cost-ordered ladder. The tests that matter here are the ordering ones: that a
/// higher rung short-circuits a lower one, because a lower rung producing an answer the higher rung
/// would have contradicted is exactly how a wrong cover gets shipped.
/// </summary>
[TestClass]
public class IdentificationLadderTests
{
    private static IdentificationLadder Ladder(IMetadataIndex index) =>
        new(index, NullLogger<IdentificationLadder>.Instance);

    private static RomIdentity Identify(IMetadataIndex index, RomFingerprint fingerprint) =>
        Ladder(index).IdentifyAsync(fingerprint).GetAwaiter().GetResult();

    // Rung ordering

    [TestMethod]
    public void IdentificationLadder_HeaderSerial_ShortCircuitsCrc32()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.NintendoDs, "Mario Kart DS (USA)", Serial: "AMCE"),
            new IndexRow(ConsoleType.NintendoDs, "A Different Game (Japan)", Crc32: 0xDEAD_BEEFu));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "0001 - Mario Kart DS.nds",
            Header = DsHeader("MARIOKARTDS", "AMCE", unitCode: 0x00),
            Crc32 = 0xDEAD_BEEFu,
        });

        Assert.AreEqual(MatchMethod.HeaderSerial, identity.MatchMethod);
        Assert.AreEqual("Mario Kart DS (USA)", identity.CanonicalName);
        Assert.AreEqual("AMCE", identity.Key);
    }

    [TestMethod]
    public void IdentificationLadder_Crc32_ShortCircuitsSha1()
    {
        const string sha1 = "1111111111111111111111111111111111111111";
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Snes, "By Crc", Crc32: 0x1234_5678u),
            new IndexRow(ConsoleType.Snes, "By Sha1", Sha1: sha1));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "whatever.sfc",
            Crc32 = 0x1234_5678u,
            Sha1 = sha1,
        });

        Assert.AreEqual(MatchMethod.Crc32, identity.MatchMethod);
        Assert.AreEqual("By Crc", identity.CanonicalName);
    }

    [TestMethod]
    public void IdentificationLadder_Sha1_ShortCircuitsFilename()
    {
        const string sha1 = "2222222222222222222222222222222222222222";
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.GameGear, "By Sha1 (World)", Sha1: sha1),
            new IndexRow(ConsoleType.GameGear, "Sonic The Hedgehog (World)"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "Sonic The Hedgehog (World).gg",
            Sha1 = sha1,
        });

        Assert.AreEqual(MatchMethod.Sha1, identity.MatchMethod);
        Assert.AreEqual("By Sha1 (World)", identity.CanonicalName);
    }

    [TestMethod]
    public void IdentificationLadder_Filename_IsTheLastResort()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Castlevania (USA) (Rev 1)"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint { FileName = "Castlevania (USA).nes" });

        Assert.AreEqual(MatchMethod.Filename, identity.MatchMethod);
        Assert.AreEqual("Castlevania (USA) (Rev 1)", identity.CanonicalName);
        Assert.AreEqual(ConsoleType.Nes, identity.ConsoleType);
    }

    /// <summary>
    /// 7z reports a CRC of 0 for both "absent" and "genuinely zero", so a 0 must fall through rather
    /// than pin the ROM to whichever DAT row happens to hash to zero.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_ZeroCrc32_IsTreatedAsUnknownAndFallsThrough()
    {
        const string sha1 = "3333333333333333333333333333333333333333";
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Snes, "Row That Hashes To Zero", Crc32: 0u),
            new IndexRow(ConsoleType.Snes, "The Real Game (USA)", Sha1: sha1));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "game.sfc",
            Crc32 = 0u,
            Sha1 = sha1,
        });

        Assert.AreEqual(MatchMethod.Sha1, identity.MatchMethod);
        Assert.AreEqual("The Real Game (USA)", identity.CanonicalName);
    }

    // The NES double lookup

    /// <summary>
    /// The load-bearing case: a real <c>.nes</c> file carries a 16-byte iNES header, No-Intro hashed the
    /// ROM without it, and the only CRC we have came out of an archive's central directory over the file
    /// as stored. NES is 33% of the index at 0% serial coverage, so this lookup is the only thing that
    /// can identify a third of the database.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_NesDoubleLookup_MatchesTheHeaderlessCrc()
    {
        var rom = NesFile(bodyLength: 40_960);
        var wholeFileCrc = Crc32.HashToUInt32(rom);
        var headerlessCrc = Crc32.HashToUInt32(rom.AsSpan(16));

        Assert.AreNotEqual(wholeFileCrc, headerlessCrc, "The fixture must actually distinguish the two.");

        // Only the headerless CRC is in the index; the whole-file lookup has to miss first.
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Metroid (USA)", Crc32: headerlessCrc));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "Metroid (USA).nes",
            Crc32 = wholeFileCrc,
            Size = rom.Length,
            Header = rom[..512],
        });

        Assert.AreEqual(MatchMethod.Crc32, identity.MatchMethod);
        Assert.AreEqual("Metroid (USA)", identity.CanonicalName);
        Assert.AreEqual(ConsoleType.Nes, identity.ConsoleType);
    }

    [TestMethod]
    public void IdentificationLadder_NesDoubleLookup_StillMatchesTheHeaderedCrc()
    {
        var rom = NesFile(bodyLength: 8_192);
        var wholeFileCrc = Crc32.HashToUInt32(rom);

        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Headered Set Entry (USA)", Crc32: wholeFileCrc));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "rom.nes",
            Crc32 = wholeFileCrc,
            Size = rom.Length,
            Header = rom[..512],
        });

        Assert.AreEqual(MatchMethod.Crc32, identity.MatchMethod);
        Assert.AreEqual("Headered Set Entry (USA)", identity.CanonicalName);
    }

    /// <summary>
    /// The CRC algebra the double lookup rests on: the headerless CRC is derived exactly from the
    /// whole-file CRC, with no access to the ROM body. If this were approximate the lookup would be
    /// worse than useless.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_NesDoubleLookup_DerivesTheHeaderlessCrcExactly()
    {
        foreach (var bodyLength in new[] { 1, 15, 16, 17, 255, 4_096, 40_960, 1_048_577 })
        {
            var rom = NesFile(bodyLength);
            var derived = Crc32Arithmetic.TryStripPrefix(
                Crc32.HashToUInt32(rom), rom.AsSpan(0, 16), bodyLength);

            Assert.AreEqual(Crc32.HashToUInt32(rom.AsSpan(16)), derived, $"body length {bodyLength}");
        }
    }

    [TestMethod]
    public void IdentificationLadder_NesDoubleLookup_WithoutSize_DoesNotGuess()
    {
        var rom = NesFile(bodyLength: 4_096);

        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Metroid (USA)", Crc32: Crc32.HashToUInt32(rom.AsSpan(16))));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        // No Size, so the split point is unknown and the derivation is impossible.
        var identity = Identify(index, new RomFingerprint
        {
            FileName = "unmatchable-name.nes",
            Crc32 = Crc32.HashToUInt32(rom),
            Header = rom[..512],
        });

        Assert.AreEqual(MatchMethod.None, identity.MatchMethod);
    }

    // DS / DSi

    /// <summary>
    /// Unitcode 0x02 is a DSi-enhanced hybrid. The 2020 code tested <c>== 0x03</c> and misrouted every
    /// one of them; No-Intro files them under Nintendo DS, so the ladder has to try both partitions.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_DsiHybrid_FindsTheRowFiledUnderNintendoDs()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(
                ConsoleType.NintendoDs,
                "Clubhouse Games Express - Card Classics (USA, Australia) (Rev 1)",
                Serial: "KTRT"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "clubhouse.nds",
            Header = DsHeader("CLUBHOUSE", "KTRT", unitCode: 0x02),
        });

        Assert.AreEqual(MatchMethod.HeaderSerial, identity.MatchMethod);
        Assert.AreEqual("KTRT", identity.Key);
        Assert.AreEqual(
            "Clubhouse Games Express - Card Classics (USA, Australia) (Rev 1)", identity.CanonicalName);

        // The index decides the art partition: a hybrid's box art is the DS box it shipped in.
        Assert.AreEqual(ConsoleType.NintendoDs, identity.ConsoleType);
    }

    /// <summary>
    /// A DS/DSi/GBA title id is a complete art key on its own (GameTDB is keyed on exactly it), so a
    /// header serial resolves even with no index at all. This is what keeps the service useful when
    /// nointro.db is missing.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_HeaderSerial_WithNoIndex_StillProducesAKey()
    {
        var identity = Identify(new NullMetadataIndex("test"), new RomFingerprint
        {
            FileName = "flipnote.nds",
            Header = DsHeader("FLIPNOTE", "KGUV", unitCode: 0x03),
        });

        Assert.AreEqual(MatchMethod.HeaderSerial, identity.MatchMethod);
        Assert.AreEqual("KGUV", identity.Key);
        Assert.AreEqual(ConsoleType.NintendoDsi, identity.ConsoleType);

        // 'V' is the Europe/Australia DSiWare region char the 2020 switch dropped on the floor,
        // silently serving every one of them the EN cover.
        Assert.AreEqual('V', identity.RegionId);
    }

    // Key derivation

    [TestMethod]
    public void IdentificationLadder_DeriveKey_PrefersTheTitleId()
    {
        Assert.AreEqual("AMCE", IdentificationLadder.DeriveKey("AMCE", "Mario Kart DS (USA)"));
        Assert.AreEqual("AMCE", IdentificationLadder.DeriveKey("amce", "Mario Kart DS (USA)"));
        Assert.AreEqual("AMCE", IdentificationLadder.DeriveKey(" AMCE ", null));
    }

    [TestMethod]
    public void IdentificationLadder_DeriveKey_IsStableAcrossCallsAndFormatting()
    {
        var canonical = IdentificationLadder.DeriveKey(null, "Super Mario Bros. (World)");

        Assert.AreEqual(16, canonical.Length);
        Assert.AreEqual(canonical, IdentificationLadder.DeriveKey(null, "Super Mario Bros. (World)"));
        Assert.AreEqual(canonical, IdentificationLadder.DeriveKey(null, "  Super   Mario Bros. (World)  "));
        Assert.AreEqual(canonical, IdentificationLadder.DeriveKey(null, "SUPER MARIO BROS. (WORLD)"));
    }

    /// <summary>
    /// The key is a URL path segment in <c>/v2/art/{platform}/{key}.png</c>, so it must need no escaping
    /// in any form it can take.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_DeriveKey_IsAlwaysUrlPathSafe()
    {
        foreach (var key in new[]
                 {
                     IdentificationLadder.DeriveKey("AMCE", null),
                     IdentificationLadder.DeriveKey(null, "Sonic & Knuckles + Sonic 3 (World)"),
                     IdentificationLadder.DeriveKey("GM 00001009-00", null),
                     IdentificationLadder.DeriveKey("../../etc/passwd", "../../etc/passwd"),
                 })
        {
            Assert.IsTrue(key.Length > 0);
            Assert.IsTrue(key.All(char.IsAsciiLetterOrDigit), key);
        }
    }

    [TestMethod]
    public void IdentificationLadder_DeriveKey_DistinguishesDifferentTitles()
    {
        var a = IdentificationLadder.DeriveKey(null, "Super Mario Bros. (World)");
        var b = IdentificationLadder.DeriveKey(null, "Super Mario Bros. 2 (USA)");

        Assert.AreNotEqual(a, b);
    }

    /// <summary>A serial too long to be a path segment must not silently become one.</summary>
    [TestMethod]
    public void IdentificationLadder_DeriveKey_LongFormSerial_IsDigested()
    {
        var key = IdentificationLadder.DeriveKey("GM 00001009-00", null);

        Assert.AreEqual(16, key.Length);
        Assert.AreNotEqual("GM 00001009-00", key);
    }

    [TestMethod]
    public void IdentificationLadder_DeriveKey_WithNothingToGoOn_IsEmpty()
    {
        Assert.AreEqual(string.Empty, IdentificationLadder.DeriveKey(null, null));
        Assert.AreEqual(string.Empty, IdentificationLadder.DeriveKey("  ", "  "));
    }

    // Batching

    [TestMethod]
    public void IdentificationLadder_IdentifyBatch_DedupesButKeepsEveryTag()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "Metroid (USA)", Crc32: 0xAAAA_AAAAu));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);
        var counting = new CountingIndex(index);

        var fingerprints = new[]
        {
            new RomFingerprint { FileName = "a.nes", Crc32 = 0xAAAA_AAAAu, Tag = "one" },
            new RomFingerprint { FileName = "a.nes", Crc32 = 0xAAAA_AAAAu, Tag = "two" },
            new RomFingerprint { FileName = "a.nes", Crc32 = 0xAAAA_AAAAu, Tag = "three" },
        };

        var results = Ladder(counting).IdentifyBatchAsync(fingerprints).GetAwaiter().GetResult();

        Assert.AreEqual(3, results.Count);
        CollectionAssert.AreEqual(
            new[] { "one", "two", "three" }, results.Select(r => r.Tag).ToArray());
        Assert.IsTrue(results.All(r => r.CanonicalName == "Metroid (USA)"));
        Assert.AreEqual(1, counting.Crc32Lookups, "Identical fingerprints must walk the ladder once.");
    }

    [TestMethod]
    public void IdentificationLadder_IdentifyBatch_DistinctFingerprintsAreNotCollapsed()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.Nes, "First (USA)", Crc32: 1u),
            new IndexRow(ConsoleType.Nes, "Second (USA)", Crc32: 2u));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var results = Ladder(index).IdentifyBatchAsync(
        [
            new RomFingerprint { FileName = "a.nes", Crc32 = 1u },
            new RomFingerprint { FileName = "b.nes", Crc32 = 2u },
        ]).GetAwaiter().GetResult();

        Assert.AreEqual("First (USA)", results[0].CanonicalName);
        Assert.AreEqual("Second (USA)", results[1].CanonicalName);
    }

    // Misses

    /// <summary>
    /// An unmatched ROM still carries whatever the header established, so a UI can say "some DS game"
    /// and offer a correction rather than showing nothing at all.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_NoMatch_StillReportsWhatTheHeaderKnew()
    {
        using var file = NoIntroIndexFile.Create(new IndexRow(ConsoleType.Nes, "Unrelated (USA)"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        // A game code whose 4th char is fine but which no rung can resolve: not in the index, and the
        // serial is not a usable key because it is not four characters.
        var identity = Identify(index, new RomFingerprint
        {
            FileName = "homebrew.gba",
            Tag = "t1",
        });

        Assert.AreEqual(MatchMethod.None, identity.MatchMethod);
        Assert.IsFalse(identity.IsMatched);
        Assert.AreEqual("t1", identity.Tag);
    }

    // Ambiguous serials

    /// <summary>
    /// Flashcart firmware and homebrew spoof a retail title id, so NDS "ASME" is carried by Super Mario
    /// 64 DS, CycloDS Evolution and EDGE alike (all three are real rows in the live No-Intro DS DAT with
    /// these CRC32s). The builder resolves this by nulling any serial that names more than one game, and
    /// the ladder must let that stand: each ROM has to reach its own row by CRC32 and come back with its
    /// own art key. Returning the header's id the moment the serial lookup missed put all three on one
    /// key, one cache entry and one cover.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_AmbiguousHeaderSerial_DoesNotCollapseDistinctGamesOntoOneKey()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.NintendoDs, "Super Mario 64 DS (USA)", Crc32: 0xE632_1562u),
            new IndexRow(ConsoleType.NintendoDs, "CycloDS Evolution (World) (v1.1)", Crc32: 0xC159_0731u),
            new IndexRow(ConsoleType.NintendoDs, "EDGE (World)", Crc32: 0x0DF3_8641u));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        // The serial is absent from every row: that is what the builder does to an ambiguous one.
        var keys = new[] { 0xE632_1562u, 0xC159_0731u, 0x0DF3_8641u }
            .Select(crc => Identify(index, new RomFingerprint
            {
                FileName = "rom.nds",
                Header = DsHeader("SPOOFED", "ASME", unitCode: 0x00),
                Crc32 = crc,
            }))
            .ToArray();

        CollectionAssert.AllItemsAreUnique(
            keys.Select(k => k.Key).ToArray(),
            "three different programs sharing a spoofed title id must not share an art key");
        Assert.IsTrue(keys.All(k => k.MatchMethod == MatchMethod.Crc32),
            "the serial rung must fall through to CRC32 rather than answering from the header alone");
        Assert.AreEqual("Super Mario 64 DS (USA)", keys[0].CanonicalName);
        Assert.AreEqual("EDGE (World)", keys[2].CanonicalName);
    }

    /// <summary>
    /// The other half of the same rule: an <i>unambiguous</i> title id must still come back as the art
    /// key, because GameTDB is keyed on exactly that. This is what keeps DS/DSi/GBA working.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_UnambiguousSerial_StillYieldsTheTitleIdAsTheKey()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.NintendoDs, "New Super Mario Bros. (USA, Australia)", Serial: "A2DE"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "New Super Mario Bros..nds",
            Header = DsHeader("NEWSUPERMARI", "A2DE", unitCode: 0x00),
        });

        Assert.AreEqual("A2DE", identity.Key);
        Assert.AreEqual("A2DE", identity.Serial);
        Assert.AreEqual(MatchMethod.HeaderSerial, identity.MatchMethod);
    }

    /// <summary>
    /// With no index behind it at all, the bare title id is still the answer; that is the whole point
    /// of the fallback, and moving it below the exact rungs must not have cost it.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_SerialUnknownToTheIndex_StillYieldsTheTitleIdAsTheKey()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.NintendoDs, "Some Other Game (USA)", Serial: "AAAA"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var identity = Identify(index, new RomFingerprint
        {
            FileName = "brand new release.nds",
            Header = DsHeader("NEWTITLE", "BXYZ", unitCode: 0x00),
            Crc32 = 0x1234_5678u,
        });

        Assert.AreEqual("BXYZ", identity.Key);
        Assert.AreEqual(MatchMethod.HeaderSerial, identity.MatchMethod);
    }

    /// <summary>
    /// Two items that agree on name but differ only in their header are not the same file, and must not
    /// be collapsed into one answer. With no CRC32 or SHA-1 present the header is the only evidence
    /// there is, so leaving it out of the dedupe key let whichever item came first answer for both.
    /// </summary>
    [TestMethod]
    public void IdentificationLadder_Batch_DoesNotMergeSameNamedItemsWithDifferentHeaders()
    {
        using var file = NoIntroIndexFile.Create(
            new IndexRow(ConsoleType.NintendoDs, "Game One (USA)", Serial: "AAAE"),
            new IndexRow(ConsoleType.NintendoDs, "Game Two (USA)", Serial: "BBBE"));
        using var index = new SqliteMetadataIndex(file.Path, NullLogger.Instance);

        var results = Ladder(index).IdentifyBatchAsync(
        [
            new RomFingerprint { FileName = "rom.nds", Header = DsHeader("ONE", "AAAE", unitCode: 0x00) },
            new RomFingerprint { FileName = "rom.nds", Header = DsHeader("TWO", "BBBE", unitCode: 0x00) },
        ]).GetAwaiter().GetResult();

        Assert.AreEqual("Game One (USA)", results[0].CanonicalName);
        Assert.AreEqual("Game Two (USA)", results[1].CanonicalName);
    }

    // Fixtures

    /// <summary>
    /// A minimal but genuine NDS header: Nintendo logo at 0xC0, title at 0x00, game code at 0x0C
    /// (whose 4th byte at 0x0F is the region char), unitcode at 0x12.
    /// </summary>
    private static byte[] DsHeader(string title, string gameCode, byte unitCode)
    {
        var header = new byte[512];
        Encoding.ASCII.GetBytes(title.PadRight(12).AsSpan(0, 12), header.AsSpan(0x00));
        Encoding.ASCII.GetBytes(gameCode.PadRight(4).AsSpan(0, 4), header.AsSpan(0x0C));
        header[0x12] = unitCode;
        ReadOnlySpan<byte> logo = [0x24, 0xFF, 0xAE, 0x51];
        logo.CopyTo(header.AsSpan(0xC0));
        return header;
    }

    /// <summary>A 16-byte iNES header followed by deterministic pseudo-random ROM data.</summary>
    private static byte[] NesFile(int bodyLength)
    {
        var rom = new byte[16 + bodyLength];
        ReadOnlySpan<byte> magic = [0x4E, 0x45, 0x53, 0x1A];
        magic.CopyTo(rom);
        rom[4] = 2; // PRG banks
        rom[5] = 1; // CHR banks
        new Random(bodyLength).NextBytes(rom.AsSpan(16));
        return rom;
    }

    /// <summary>
    /// A hand-written counting decorator: the house rule is no mocking libraries, and counting one
    /// call needs no framework.
    /// </summary>
    private sealed class CountingIndex(IMetadataIndex inner) : IMetadataIndex
    {
        public int Crc32Lookups { get; private set; }

        public bool TryByCrc32(uint crc32, out IndexEntry entry)
        {
            Crc32Lookups++;
            return inner.TryByCrc32(crc32, out entry);
        }

        public bool TryBySha1(string sha1, out IndexEntry entry) => inner.TryBySha1(sha1, out entry);

        public bool TryBySerial(ConsoleType console, string serial, out IndexEntry entry) =>
            inner.TryBySerial(console, serial, out entry);

        public IndexEntry? SearchByName(ConsoleType console, string name) =>
            inner.SearchByName(console, name);

        public string Version => inner.Version;

        public int RowCount => inner.RowCount;
    }
}
