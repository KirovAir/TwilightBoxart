using System.Text;
using TwilightBoxart.Core.Consoles;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests;

[TestClass]
public class ConsoleDetectorTests
{
    // Header synthesis. Real ROMs are not committed - every buffer below is built from the documented
    // offsets, so a test failure points at the offset table rather than at a missing fixture file.

    private static byte[] Buffer(int length) => new byte[length];

    private static void Write(byte[] buffer, int offset, params byte[] bytes) =>
        bytes.CopyTo(buffer, offset);

    private static void WriteAscii(byte[] buffer, int offset, string text) =>
        Encoding.ASCII.GetBytes(text).CopyTo(buffer, offset);

    /// <param name="title">The title field written at 0x134.</param>
    /// <param name="cgbFlag">The byte at 0x143: 0x80 dual, 0xC0 CGB-only, anything else DMG.</param>
    /// <param name="manufacturer">The 4-char manufacturer code at 0x13F, or null to leave it blank.</param>
    private static byte[] GameBoyHeader(string title, byte cgbFlag = 0x00, string? manufacturer = null)
    {
        var buffer = Buffer(0x150);
        Write(buffer, 0x100, 0x00, 0xC3, 0x50, 0x01);
        Write(buffer, 0x104, 0xCE, 0xED, 0x66, 0x66);

        // The flag goes down before the title on purpose: a 16-character DMG title occupies 0x143, so
        // letting it overwrite the flag reproduces exactly the layout collision this test file exists
        // to pin down.
        buffer[0x143] = cgbFlag;
        WriteAscii(buffer, 0x134, title);
        if (manufacturer is not null)
        {
            WriteAscii(buffer, 0x13F, manufacturer);
        }

        return buffer;
    }

    private static byte[] GbaHeader(string title, string gameCode)
    {
        var buffer = Buffer(0x200);
        Write(buffer, 0x04, 0x24, 0xFF, 0xAE, 0x51);
        WriteAscii(buffer, 0xA0, title);
        WriteAscii(buffer, 0xAC, gameCode);
        buffer[0xB2] = 0x96;
        return buffer;
    }

    private static byte[] NdsHeader(string title, string gameCode, byte unitCode)
    {
        var buffer = Buffer(0x200);
        WriteAscii(buffer, 0x00, title);
        WriteAscii(buffer, 0x0C, gameCode);
        buffer[0x12] = unitCode;
        Write(buffer, 0xC0, 0x24, 0xFF, 0xAE, 0x51);
        return buffer;
    }

    private static byte[] SnesHeader(string title, int baseOffset, byte mapMode, int smcHeader = 0)
    {
        var buffer = Buffer(baseOffset + smcHeader + 0x40);
        var header = baseOffset + smcHeader;
        WriteAscii(buffer, header, title.PadRight(21));
        buffer[header + 0x15] = mapMode;
        buffer[header + 0x17] = 0x0A;               // 1 MiB
        buffer[header + 0x1A] = 0x33;               // extended header present
        WriteAscii(buffer, header - 0x0E, "ABCD");  // game code, in the extended header
        Write(buffer, header + 0x1C, 0x34, 0x12);   // complement
        Write(buffer, header + 0x1E, 0xCB, 0xED);   // checksum: 0x1234 ^ 0xEDCB == 0xFFFF
        Write(buffer, header + 0x3C, 0x00, 0x80);   // reset vector 0x8000
        return buffer;
    }

    private static byte[] SegaEightBitHeader(int headerOffset, byte regionNibble)
    {
        var buffer = Buffer(headerOffset + 0x10);
        WriteAscii(buffer, headerOffset, "TMR SEGA");
        buffer[headerOffset + 0x0C] = 0x34;                     // BCD digits 3,4
        buffer[headerOffset + 0x0D] = 0x12;                     // BCD digits 1,2
        buffer[headerOffset + 0x0E] = 0x50;                     // top BCD digit 5, version 0
        buffer[headerOffset + 0x0F] = (byte)(regionNibble << 4);
        return buffer;
    }

    // One test per console.

    [TestMethod]
    public void Detect_GameBoy_ReadsTheFullSixteenByteTitle()
    {
        // A DMG cart predates the CGB flag, so all 16 bytes of 0x134-0x143 are title.
        var result = ConsoleDetector.Detect(GameBoyHeader("SUPER MARIOLAND"));

        Assert.AreEqual(ConsoleType.GameBoy, result.ConsoleType);
        Assert.AreEqual("SUPER MARIOLAND", result.Title);
    }

    [TestMethod]
    public void Detect_GameBoy_AcceptsTheEntrypointHeuristicWithoutTheNintendoLogo()
    {
        // The Sachen unlicensed multicarts ship no logo at all. Without this fallback they are 12 of
        // the 13 undetected GBC files in the corpus.
        var buffer = GameBoyHeader("31 IN 1");
        Write(buffer, 0x104, 0x7C, 0x00, 0x37, 0x20); // real bytes from "31 in 1 (Taiwan) (Sachen)"

        var result = ConsoleDetector.Detect(buffer);

        Assert.AreEqual(ConsoleType.GameBoy, result.ConsoleType);
        Assert.AreEqual("31 IN 1", result.Title);
    }

    [TestMethod]
    public void Detect_GameBoyColor_ColorOnlyFlagIsUnambiguous()
    {
        var result = ConsoleDetector.Detect(GameBoyHeader("POKEMON CRYS", 0xC0));

        Assert.AreEqual(ConsoleType.GameBoyColor, result.ConsoleType);
        Assert.AreEqual(ConsoleType.Unknown, result.AlternateConsoleType,
            "0xC0 means CGB-only. There is no second platform to try.");
    }

    [TestMethod]
    public void Detect_GameBoyColor_DualFlagOffersGameBoyAsTheAlternate()
    {
        // 7 carts filed under Game Boy in the corpus carry 0x80 and were sent to the GBC art DB with no
        // way back. GBC stays the primary because that is right for the large majority; the alternate is
        // what gives the other 7 a second lookup.
        var result = ConsoleDetector.Detect(GameBoyHeader("ZELDA DX", 0x80));

        Assert.AreEqual(ConsoleType.GameBoyColor, result.ConsoleType);
        Assert.AreEqual(ConsoleType.GameBoy, result.AlternateConsoleType);
    }

    [TestMethod]
    public void Detect_GameBoyAdvance_ReadsTitleAndGameCode()
    {
        var result = ConsoleDetector.Detect(GbaHeader("POKEMON EMER", "BPEE"));

        Assert.AreEqual(ConsoleType.GameBoyAdvance, result.ConsoleType);
        Assert.AreEqual("POKEMON EMER", result.Title);
        Assert.AreEqual("BPEE", result.Serial);
    }

    [TestMethod]
    public void Detect_NintendoDs_ReadsTitleGameCodeAndRegion()
    {
        var result = ConsoleDetector.Detect(NdsHeader("NEWSUPERMARI", "A2DE", unitCode: 0x00));

        Assert.AreEqual(ConsoleType.NintendoDs, result.ConsoleType);
        Assert.AreEqual("NEWSUPERMARI", result.Title);
        Assert.AreEqual("A2DE", result.Serial);
        Assert.AreEqual('E', result.RegionId);
    }

    [TestMethod]
    public void Detect_Nes_ReportsTheSixteenByteInesHeader()
    {
        var buffer = Buffer(64);
        Write(buffer, 0, 0x4E, 0x45, 0x53, 0x1A);

        var result = ConsoleDetector.Detect(buffer);

        Assert.AreEqual(ConsoleType.Nes, result.ConsoleType);
        Assert.AreEqual(16, result.LeadingHeaderBytes,
            "NES is 33% of the index at 0% serial coverage, so the hash lookup is the only path - and it "
            + "has to be tried both with and without these 16 bytes.");
    }

    [TestMethod]
    public void Detect_FamicomDiskSystem_ReadsTheGameCodeWithAndWithoutTheFwNesWrapper()
    {
        var bare = Buffer(64);
        Write(bare, 0, 0x01, 0x2A, 0x4E, 0x49, 0x4E);
        WriteAscii(bare, 0x10, "ZEL");

        var bareResult = ConsoleDetector.Detect(bare);
        Assert.AreEqual(ConsoleType.FamicomDiskSystem, bareResult.ConsoleType);
        Assert.AreEqual("ZEL", bareResult.Serial);
        Assert.AreEqual(0, bareResult.LeadingHeaderBytes);

        var wrapped = Buffer(80);
        WriteAscii(wrapped, 0, "FDS");
        wrapped[3] = 0x1A;
        Write(wrapped, 16, 0x01, 0x2A, 0x4E, 0x49, 0x4E);
        WriteAscii(wrapped, 16 + 0x10, "ZEL");

        var wrappedResult = ConsoleDetector.Detect(wrapped);
        Assert.AreEqual(ConsoleType.FamicomDiskSystem, wrappedResult.ConsoleType);
        Assert.AreEqual("ZEL", wrappedResult.Serial);
        Assert.AreEqual(16, wrappedResult.LeadingHeaderBytes);
    }

    [TestMethod]
    public void Detect_Nintendo64_ReportsByteOrderAndDescramblesTheTitle()
    {
        // Build the canonical .z64 form once, then derive the other two orders from it, so the test
        // proves the descrambling rather than restating it.
        var z64 = Buffer(0x40);
        Write(z64, 0, 0x80, 0x37, 0x12, 0x40);
        WriteAscii(z64, 0x20, "SUPER MARIO 64");
        WriteAscii(z64, 0x3B, "NSME");

        var v64 = new byte[z64.Length];
        for (var i = 0; i < z64.Length; i++)
        {
            v64[i] = z64[i ^ 1];
        }

        var n64 = new byte[z64.Length];
        for (var i = 0; i < z64.Length; i++)
        {
            n64[i] = z64[(i & ~3) | (3 - (i & 3))];
        }

        foreach (var (buffer, expectedOrder) in new[]
                 {
                     (z64, N64ByteOrder.BigEndian),
                     (v64, N64ByteOrder.ByteSwapped),
                     (n64, N64ByteOrder.LittleEndian),
                 })
        {
            var result = ConsoleDetector.Detect(buffer);

            Assert.AreEqual(ConsoleType.Nintendo64, result.ConsoleType, $"{expectedOrder}");
            Assert.AreEqual(expectedOrder, result.ByteOrder);
            Assert.AreEqual("SUPER MARIO 64", result.Title, $"{expectedOrder} title was not descrambled");
            Assert.AreEqual("NSME", result.Serial, $"{expectedOrder} serial was not descrambled");
        }
    }

    [TestMethod]
    public void Detect_MegaDrive_PrefersTheOverseasTitleAndReadsTheSerial()
    {
        var buffer = Buffer(0x200);
        WriteAscii(buffer, 0x100, "SEGA MEGA DRIVE ");
        WriteAscii(buffer, 0x120, "SONIC THE HEDGEHOG (JP)");
        WriteAscii(buffer, 0x150, "SONIC THE HEDGEHOG");
        WriteAscii(buffer, 0x180, "GM 00001051-00");

        var result = ConsoleDetector.Detect(buffer);

        Assert.AreEqual(ConsoleType.MegaDrive, result.ConsoleType);
        Assert.AreEqual("SONIC THE HEDGEHOG", result.Title, "the index names Mega Drive entries in English");
        Assert.AreEqual("00001051-00", result.Serial,
            "the DAT records the bare product code, so the 'GM ' device-type prefix must be stripped "
            + "before the serial reaches TryBySerial - Sonic 2 really is serial \"00001051-00\" in the "
            + "libretro No-Intro Mega Drive DAT.");
        Assert.IsNull(result.RegionId,
            "Mega Drive region letters are a different alphabet from the NDS ones and must not be fed "
            + "to GameTdbRegion.");
    }

    /// <summary>
    /// The interior padding case, and the one code shape that must survive untouched. Both forms are
    /// taken from the real libretro No-Intro Mega Drive DAT.
    /// </summary>
    [TestMethod]
    [DataRow("GM MK-1563 -00", "MK-1563-00", DisplayName = "device-type prefix and interior padding")]
    [DataRow("GM T-48073 -00", "T-48073-00", DisplayName = "third-party product code")]
    [DataRow("GM-MK-1198--00", "GM-MK-1198--00", DisplayName = "'GM-' is a product code, not a prefix")]
    [DataRow("AI 00004052-00", "00004052-00", DisplayName = "education-title device type")]
    public void Detect_MegaDriveSerial_IsNormalisedToTheFormTheDatRecords(string field, string expected)
    {
        var buffer = Buffer(0x200);
        WriteAscii(buffer, 0x100, "SEGA MEGA DRIVE ");
        WriteAscii(buffer, 0x180, field);

        Assert.AreEqual(expected, ConsoleDetector.Detect(buffer).Serial);
    }

    [TestMethod]
    public void Detect_MegaDrive_AcceptsTheLeadingSpaceVariant()
    {
        var buffer = Buffer(0x200);
        WriteAscii(buffer, 0x100, " SEGA PICO      ");
        WriteAscii(buffer, 0x150, "A PICO GAME");

        Assert.AreEqual(ConsoleType.MegaDrive, ConsoleDetector.Detect(buffer).ConsoleType);
    }

    [TestMethod]
    public void Detect_MasterSystemAndGameGear_SplitOnTheRegionNibble()
    {
        Assert.AreEqual(ConsoleType.MasterSystem,
            ConsoleDetector.Detect(SegaEightBitHeader(0x7FF0, 0x4)).ConsoleType, "SMS Export");
        Assert.AreEqual(ConsoleType.MasterSystem,
            ConsoleDetector.Detect(SegaEightBitHeader(0x7FF0, 0x3)).ConsoleType, "SMS Japan");
        Assert.AreEqual(ConsoleType.GameGear,
            ConsoleDetector.Detect(SegaEightBitHeader(0x7FF0, 0x6)).ConsoleType, "GG Export");
        Assert.AreEqual(ConsoleType.GameGear,
            ConsoleDetector.Detect(SegaEightBitHeader(0x7FF0, 0x7)).ConsoleType, "GG International");

        // The 8 KiB and 16 KiB fallback locations.
        Assert.AreEqual(ConsoleType.MasterSystem,
            ConsoleDetector.Detect(SegaEightBitHeader(0x1FF0, 0x4)).ConsoleType);
        Assert.AreEqual(ConsoleType.MasterSystem,
            ConsoleDetector.Detect(SegaEightBitHeader(0x3FF0, 0x4)).ConsoleType);

        Assert.AreEqual("51234", ConsoleDetector.Detect(SegaEightBitHeader(0x7FF0, 0x4)).Serial);
    }

    [TestMethod]
    public void Detect_Snes_FindsLoRomHiRomAndExHiRom()
    {
        var loRom = ConsoleDetector.Detect(SnesHeader("SUPER METROID", 0x7FC0, 0x20));
        Assert.AreEqual(ConsoleType.Snes, loRom.ConsoleType);
        Assert.AreEqual("SUPER METROID", loRom.Title);
        Assert.AreEqual("ABCD", loRom.Serial);

        Assert.AreEqual(ConsoleType.Snes,
            ConsoleDetector.Detect(SnesHeader("HIROM GAME", 0xFFC0, 0x21)).ConsoleType);
        Assert.AreEqual(ConsoleType.Snes,
            ConsoleDetector.Detect(SnesHeader("EXHIROM GAME", 0x40FFC0, 0x35)).ConsoleType);
    }

    [TestMethod]
    public void Detect_Snes_ReportsAnSmcCopierHeader()
    {
        var result = ConsoleDetector.Detect(SnesHeader("SUPER METROID", 0x7FC0, 0x20, smcHeader: 512));

        Assert.AreEqual(ConsoleType.Snes, result.ConsoleType);
        Assert.AreEqual(512, result.LeadingHeaderBytes,
            "the copier header shifts the hashed region, so the index lookup has to be tried both ways");
    }

    [TestMethod]
    public void Detect_Snes_DegradesToNotDetectedOnTheFiveHundredAndTwelveByteClientWindow()
    {
        // Documented limitation, asserted so nobody later "fixes" it into a guess: the SNES internal
        // header is at 0x7FC0 at the earliest, so the window every client sends cannot reach it. The
        // right answer is no detection - SNES identifies by hash.
        var full = SnesHeader("SUPER METROID", 0x7FC0, 0x20);

        var truncated = ConsoleDetector.Detect(full.AsSpan(0, 512));

        Assert.IsFalse(truncated.IsDetected);
    }

    // Regression tests for the specific bugs this rewrite exists to fix.

    [TestMethod]
    public void Detect_UnitCodeTwo_IsDsiNotNds()
    {
        // THE bug. Byte 0x12 is unitcode: 0x00 NDS, 0x02 DSi-enhanced hybrid, 0x03 DSi-only. The 2020
        // code tested `== 0x03`, so every 0x02 hybrid was routed to NintendoDs and lost both the right
        // art partition and the DSiWare placeholder. Real examples: KADJ, KTRT, KTPK.
        var result = ConsoleDetector.Detect(NdsHeader("DECODE", "KADJ", unitCode: 0x02));

        Assert.AreEqual(ConsoleType.NintendoDsi, result.ConsoleType);
        Assert.AreEqual(ConsoleType.NintendoDs, result.AlternateConsoleType,
            "a hybrid is a DSi title that No-Intro files under Nintendo DS, so both need trying");
    }

    [TestMethod]
    public void Detect_UnitCodeThree_IsDsiOnly()
    {
        var result = ConsoleDetector.Detect(NdsHeader("FLIPNOTE", "KGUV", unitCode: 0x03));

        Assert.AreEqual(ConsoleType.NintendoDsi, result.ConsoleType);
        Assert.AreEqual(ConsoleType.Unknown, result.AlternateConsoleType);
    }

    [TestMethod]
    public void Detect_UnitCodeZero_StaysNds()
    {
        var result = ConsoleDetector.Detect(NdsHeader("PICTOCHAT", "HNEA", unitCode: 0x00));

        Assert.AreEqual(ConsoleType.NintendoDs, result.ConsoleType);
        Assert.AreEqual(ConsoleType.Unknown, result.AlternateConsoleType);
    }

    [TestMethod]
    public void GameTdbRegion_HandlesTheFourCharactersTheOldSwitchMissed()
    {
        // V, C, X and A silently fell through to EN - 21 of the 124 detected DSi files. V alone is 17,
        // and it is the standard Europe/Australia DSiWare region character (Flipnote Studio, Dark Void
        // Zero, Fieldrunners). TryMap is the point: "explicitly EN" is now distinguishable from
        // "unknown, defaulted to EN".
        foreach (var regionId in "VCXA")
        {
            Assert.IsTrue(GameTdbRegion.TryMap(regionId, out _),
                $"region '{regionId}' must be handled explicitly, not silently defaulted");
        }

        Assert.AreEqual("EN", GameTdbRegion.From('V'), "Europe/Australia DSiWare");
        Assert.AreEqual("EN", GameTdbRegion.From('A'));
        Assert.AreEqual("EN", GameTdbRegion.From('X'));
        Assert.AreEqual("ZH", GameTdbRegion.From('C'), "China / iQue DS");
    }

    [TestMethod]
    public void GameTdbRegion_MapsTheDocumentedAlphabet()
    {
        Assert.AreEqual("US", GameTdbRegion.From('E'));
        Assert.AreEqual("US", GameTdbRegion.From('T'));
        Assert.AreEqual("JA", GameTdbRegion.From('J'));
        Assert.AreEqual("KO", GameTdbRegion.From('K'));
        Assert.AreEqual("EN", GameTdbRegion.From('O'));
        Assert.AreEqual("EN", GameTdbRegion.From('P'));
        Assert.AreEqual("EN", GameTdbRegion.From('U'));
        Assert.AreEqual("DE", GameTdbRegion.From('D'));
        Assert.AreEqual("FR", GameTdbRegion.From('F'));
        Assert.AreEqual("NL", GameTdbRegion.From('H'));
        Assert.AreEqual("IT", GameTdbRegion.From('I'));
        Assert.AreEqual("RU", GameTdbRegion.From('R'));
        Assert.AreEqual("ES", GameTdbRegion.From('S'));
        Assert.AreEqual("HB", GameTdbRegion.From('#'));
    }

    [TestMethod]
    public void GameTdbRegion_DefaultsToEnglishForAnUnknownCharacter()
    {
        Assert.IsFalse(GameTdbRegion.TryMap('Q', out _));
        Assert.AreEqual("EN", GameTdbRegion.From('Q'));
        Assert.AreEqual("EN", GameTdbRegion.From(null));
    }

    [TestMethod]
    public void GameTdbRegion_IsCultureInvariant()
    {
        // ToUpper() under tr-TR maps 'i' to a dotted capital and would miss the 'I' -> IT entry. This is
        // the same class of bug as the 2020 client's culture-sensitive .ZIP handling.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.AreEqual("IT", GameTdbRegion.From('i'));
            Assert.AreEqual("IT", GameTdbRegion.From('I'));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Detect_ActionReplayStyleTitle_IsReportedAsGameBoyWithAColorAlternate()
    {
        // "Action Replay Online (Europe)" and "GameShark Online (USA)" are filed under GBC but their
        // headers are pure DMG: byte 0x143 is 0x34, the ASCII '4' ending the 16-char title "Action
        // Replay V4". No header-only rule can call these Color, so the honest answer is GameBoy plus an
        // alternate for the index to try.
        var buffer = GameBoyHeader("Action Replay V4");
        Assert.AreEqual((byte)0x34, buffer[0x143], "the fixture must reproduce the real 0x143 value");

        var result = ConsoleDetector.Detect(buffer);

        Assert.AreEqual(ConsoleType.GameBoy, result.ConsoleType);
        Assert.AreEqual("Action Replay V4", result.Title,
            "the 2020 code read 16 bytes here and got this right; the CGB path read 15 and did not");
        Assert.AreEqual(ConsoleType.GameBoyColor, result.AlternateConsoleType,
            "an unset CGB flag does not prove a cart is not a Color cart");
    }

    [TestMethod]
    public void Detect_GameBoyColor_ClaimsAManufacturerCodeOnlyWhenTheTitleProvablyEndedFirst()
    {
        // Unambiguous: the title is NUL-terminated inside 0x134-0x13E, so 0x13F-0x142 cannot be title.
        var terminated = ConsoleDetector.Detect(GameBoyHeader("POKEMON", 0xC0, manufacturer: "AXQE"));
        Assert.AreEqual("POKEMON", terminated.Title);
        Assert.AreEqual("AXQE", terminated.Serial);

        // Ambiguous: 15 printable characters with no NUL. "LAND" at 0x13F has the exact shape of a
        // manufacturer code, but claiming it would truncate the title to "SUPER MARIO". We keep the
        // title whole and emit no serial - GB/GBC serial coverage in the index is 0.1%/3.1%, so the
        // title is what identification actually runs on.
        var ambiguous = ConsoleDetector.Detect(GameBoyHeader("SUPER MARIOLAND", 0x80));
        Assert.AreEqual("SUPER MARIOLAND", ambiguous.Title);
        Assert.IsNull(ambiguous.Serial);
    }

    [TestMethod]
    public void Detect_GbaIsCheckedBeforeNds_SoNeitherCanClaimTheOther()
    {
        // Both use the same 4-byte Nintendo logo, at 0x04 (GBA) and 0xC0 (NDS). GBA runs first because
        // that direction is structurally safe: 0x04 on an NDS ROM is inside the ASCII title field, which
        // cannot hold 0xFF/0xAE. Assert both directions so a reorder is caught.
        Assert.AreEqual(ConsoleType.NintendoDs,
            ConsoleDetector.Detect(NdsHeader("NEWSUPERMARI", "A2DE", 0x00)).ConsoleType);
        Assert.AreEqual(ConsoleType.GameBoyAdvance,
            ConsoleDetector.Detect(GbaHeader("POKEMON EMER", "BPEE")).ConsoleType);
    }

    [TestMethod]
    public void Detect_OffsetZeroMagicsWinOverTheGameBoyEntrypointHeuristic()
    {
        // The Game Boy fallback is 4 bytes of arbitrary code at 0x100, so a NES ROM whose PRG data
        // happens to hold `00 C3 50 01` there would be claimed by it. NES has a definitive magic at
        // offset 0 and therefore has to run first.
        var nes = Buffer(0x150);
        Write(nes, 0, 0x4E, 0x45, 0x53, 0x1A);
        Write(nes, 0x100, 0x00, 0xC3, 0x50, 0x01);

        Assert.AreEqual(ConsoleType.Nes, ConsoleDetector.Detect(nes).ConsoleType);
    }

    [TestMethod]
    public void Detect_MegaDriveWinsOverAStrayTmrSegaInItsRomData()
    {
        // "TMR SEGA" is 8 ASCII bytes 32 KiB into the file - entirely possible inside a Mega Drive ROM's
        // data. The Mega Drive header at 0x100 is definitive, so it must be checked first.
        var buffer = Buffer(0x8000);
        WriteAscii(buffer, 0x100, "SEGA MEGA DRIVE ");
        WriteAscii(buffer, 0x150, "SOME GAME");
        WriteAscii(buffer, 0x7FF0, "TMR SEGA");

        Assert.AreEqual(ConsoleType.MegaDrive, ConsoleDetector.Detect(buffer).ConsoleType);
    }

    [TestMethod]
    public void Detect_EmptyBuffer_DoesNotThrow()
    {
        Assert.IsFalse(ConsoleDetector.Detect(ReadOnlySpan<byte>.Empty).IsDetected);
        Assert.IsFalse(ConsoleDetector.Detect(default).IsDetected);
    }

    [TestMethod]
    public void Detect_TruncatedHeader_DoesNotThrowAtAnyLength()
    {
        // The 2020 code gated on `Length >= 328` and still threw IndexOutOfRange on an empty title id.
        // Every prefix of a valid header of every console must degrade, never throw.
        byte[][] sources =
        [
            GameBoyHeader("SUPER MARIOLAND", 0xC0, "AXQE"),
            GbaHeader("POKEMON EMER", "BPEE"),
            NdsHeader("NEWSUPERMARI", "A2DE", 0x02),
            SnesHeader("SUPER METROID", 0x7FC0, 0x20),
            SegaEightBitHeader(0x7FF0, 0x6),
        ];

        foreach (var source in sources)
        {
            for (var length = 0; length <= source.Length; length++)
            {
                // Assert.IsNotNull rather than a bare call so the loop cannot be optimised into nothing;
                // the real assertion is that no exception escapes.
                Assert.IsNotNull(ConsoleDetector.Detect(source.AsSpan(0, length)));
            }
        }
    }

    [TestMethod]
    public void Detect_GarbageBuffers_AreNotDetectedAndDoNotThrow()
    {
        // Deterministic pseudo-random noise: nothing here should look like a console, and in particular
        // the scored SNES parser must not claim it.
        var random = new Random(20260720);
        for (var trial = 0; trial < 200; trial++)
        {
            var buffer = new byte[random.Next(0, 0x9000)];
            random.NextBytes(buffer);

            var result = ConsoleDetector.Detect(buffer);

            Assert.IsFalse(result.IsDetected,
                $"random {buffer.Length}-byte buffer was detected as {result.ConsoleType}");
        }
    }

    [TestMethod]
    public void Detect_AllZeroBuffer_IsNotDetected()
    {
        // The degenerate case the SNES scorer has to reject: an all-zero window has a checksum pair that
        // is arithmetically consistent and a title of 21 NULs. The printable-title gate is what stops it.
        Assert.IsFalse(ConsoleDetector.Detect(Buffer(0x10000)).IsDetected);
    }

    [TestMethod]
    public void DefaultParsers_KeepTheScoredSnesParserLast()
    {
        // DefaultParsers is consumed as-is. SNES scores rather than matches, so every parser with
        // real evidence must get to claim the buffer before it.
        Assert.IsInstanceOfType(ConsoleDetector.DefaultParsers[^1], typeof(SnesHeaderParser));
    }
}
