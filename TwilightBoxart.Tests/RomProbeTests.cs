using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using TwilightBoxart.Core.Probe;

namespace TwilightBoxart.Tests;

[TestClass]
public class RomProbeTests
{
    /// <summary>
    /// A .7z archive inlined so the test needs no fixture files. SharpCompress cannot write 7z
    /// archives, so this is the only way to exercise the format against known ground truth: one
    /// entry named <c>fake.nds</c>, 4,096 bytes, CRC32 0xC1F8B000.
    /// </summary>
    private const string SevenZipFixtureBase64 =
        "N3q8ryccAAQfQzeAPAAAAAAAAABaAAAAAAAAANQR26XgD/8ANF0AJxFHNMqWI9YbndGkCV6Zwa7E" +
        "au/4YN55VJwkdA1AH6/A0x6Fu8SaoCQ3VxSte4qSSEcAAAABBAYAAQk8AAcLAQABISEBAAyQAAAI" +
        "CgEAsPjBAAAFARkLAAAAAAAAAAAAAAAREwBmAGEAawBlAC4AbgBkAHMAAAAZABQKAQDLQcLaNxjd" +
        "ARUGAQAgAAAAAAA=";

    private const uint FixtureRomCrc32 = 0xC1F8B000;

    // loose

    [TestMethod]
    public async Task LooseRomProbe_ReadsHeaderAndCrc32()
    {
        var rom = MakeNdsRom();
        var path = await WriteTempAsync("fake.nds", rom);

        await using var stream = File.OpenRead(path);
        var result = await new LooseRomProbe().ProbeAsync(stream, path, true);

        Assert.IsNotNull(result);
        Assert.AreEqual("fake.nds", result.InnerName);
        Assert.AreEqual(ContainerKind.Loose, result.Container);
        Assert.AreEqual(rom.Length, result.UncompressedSize);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
        Assert.AreEqual(FixtureRomCrc32, result.Crc32); // the fixture's recorded ground truth
        AssertNdsHeader(result.Header);
        Assert.AreEqual(rom.Length, result.BytesRead); // a CRC costs the whole file, and says so
    }

    [TestMethod]
    public async Task LooseRomProbe_OverCrcBudget_SkipsHashAndReadsOnly512Bytes()
    {
        var rom = MakeNdsRom(64 * 1024, 7);
        var path = await WriteTempAsync("big.nds", rom);

        await using var stream = File.OpenRead(path);
        var result = await new LooseRomProbe(0).ProbeAsync(stream, path, true);

        Assert.IsNotNull(result);
        Assert.IsNull(result.Crc32);
        AssertNdsHeader(result.Header);
        Assert.AreEqual(512, result.BytesRead);
    }

    // zip

    [TestMethod]
    public async Task ZipRomProbe_Deflated_ReadsCrc32WithoutDecompressing()
    {
        // 64 KiB of incompressible data, so the archive is comfortably larger than the 4 KiB tail
        // window and the byte count below actually proves something.
        var rom = MakeNdsRom(64 * 1024, 1);
        var zip = BuildZip(("fake.nds", rom, CompressionLevel.Optimal));

        var result = await ProbeZipAsync(zip, "Some Game (USA).zip", false);

        Assert.IsNotNull(result);
        Assert.AreEqual("fake.nds", result.InnerName);
        Assert.AreEqual(ContainerKind.Zip, result.Container);
        Assert.AreEqual(rom.Length, result.UncompressedSize);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
        Assert.IsNull(result.Header);
        Assert.IsTrue(result.BytesRead <= 4096, $"CRC-only path read {result.BytesRead} bytes; one 4 KiB tail slice must be enough");
    }

    [TestMethod]
    public async Task ZipRomProbe_Deflated_WantHeader_InflatesFirst512Bytes()
    {
        var rom = MakeNdsRom(64 * 1024, 1);
        var zip = BuildZip(("fake.nds", rom, CompressionLevel.Optimal));

        var result = await ProbeZipAsync(zip, "game.zip", true);

        Assert.IsNotNull(result);
        AssertNdsHeader(result.Header);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);

        // The header costs a bounded compressed prefix on top of the tail, not the whole 64 KiB entry.
        Assert.IsTrue(result.BytesRead <= 4096 + 30 + 4096, $"header path read {result.BytesRead} bytes");
    }

    [TestMethod]
    public async Task ZipRomProbe_StoredEntry_ReadsHeaderDirectly()
    {
        var rom = MakeNdsRom(64 * 1024, 2);
        var zip = BuildZip(("fake.nds", rom, CompressionLevel.NoCompression));

        var result = await ProbeZipAsync(zip, "game.zip", true);

        Assert.IsNotNull(result);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
        AssertNdsHeader(result.Header);
    }

    [TestMethod]
    public async Task ZipRomProbe_MultipleEntries_PicksFirstRomExtension()
    {
        var rom = MakeNdsRom(8192, 3);
        var zip = BuildZip(
            ("readme.txt", Encoding.UTF8.GetBytes("scene notes"), CompressionLevel.Optimal),
            ("cover.png", new byte[4096], CompressionLevel.Optimal),
            ("fake.nds", rom, CompressionLevel.Optimal),
            ("second.gba", new byte[2048], CompressionLevel.Optimal));

        var result = await ProbeZipAsync(zip, "game.zip", false);

        Assert.IsNotNull(result);
        Assert.AreEqual("fake.nds", result.InnerName);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
    }

    [TestMethod]
    public async Task ZipRomProbe_NestedEntry_ReportsLeafNameNotArchiveName()
    {
        var rom = MakeNdsRom(4096, 4);
        var zip = BuildZip(("Nintendo DS/roms/Deep Game (Europe).nds", rom, CompressionLevel.Optimal));

        var result = await ProbeZipAsync(zip, "Totally Different Archive Name.zip", false);

        Assert.IsNotNull(result);
        Assert.AreEqual("Deep Game (Europe).nds", result.InnerName);
    }

    [TestMethod]
    public async Task ZipRomProbe_NoRomEntry_ReturnsNullAndLeavesStreamUsable()
    {
        var zip = BuildZip(("readme.txt", Encoding.UTF8.GetBytes("nothing to see"), CompressionLevel.Optimal));

        await using var stream = new MemoryStream(zip, false);
        var result = await new ZipRomProbe().ProbeAsync(stream, "empty.zip", true);

        Assert.IsNull(result);

        // Regression guard: the 2020 client disposed the stream on this path and
        // then read from it anyway, which is what broke 1,908 zips.
        Assert.IsTrue(stream.CanRead);
        stream.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(0x50, stream.ReadByte());
    }

    [TestMethod]
    public async Task ZipRomProbe_DsiCdnBlobs_PicksTheDominantEntry()
    {
        // A No-Intro "Nintendo DSi (Digital)" zip: extension-less CDN content next to a ticket and a
        // title-metadata file. 945 of 1,069 files look like this.
        var content = MakeNdsRom(256 * 1024, 5);
        var zip = BuildZip(
            ("tik", new byte[2472], CompressionLevel.Optimal),
            ("tmd.0", new byte[1024], CompressionLevel.Optimal),
            ("00000000", content, CompressionLevel.Optimal));

        var result = await ProbeZipAsync(zip, "Flipnote Studio (Europe, Australia).zip", false);

        Assert.IsNotNull(result);
        Assert.AreEqual("00000000", result.InnerName);
        Assert.AreEqual(Crc32.HashToUInt32(content), result.Crc32);
    }

    [TestMethod]
    public async Task ZipRomProbe_ComparableSizedUnknownEntries_ReturnsNull()
    {
        // No dominance means no guess: two similar blobs are not a CDN drop.
        var zip = BuildZip(
            ("blobA", MakeNdsRom(128 * 1024, 8), CompressionLevel.Optimal),
            ("blobB", MakeNdsRom(100 * 1024, 9), CompressionLevel.Optimal));

        Assert.IsNull(await ProbeZipAsync(zip, "ambiguous.zip", false));
    }

    [TestMethod]
    public async Task ZipRomProbe_TurkishCulture_StillMatchesUppercaseExtensions()
    {
        // The old code lowercased with the current culture, so under tr-TR
        // ".DSI" became ".dsı" and matched nothing. Both the archive extension and the inner entry
        // extension here contain the dotted I that trips it.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var rom = MakeNdsRom(4096, 6);
            var zip = BuildZip(("GAME.DSI", rom, CompressionLevel.Optimal));

            Assert.IsTrue(SupportedFiles.IsArchive("Some Game.ZIP"));

            var result = await ProbeZipAsync(zip, "Some Game.ZIP", false);

            Assert.IsNotNull(result);
            Assert.AreEqual("GAME.DSI", result.InnerName);
            Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public async Task ZipRomProbe_CommentLongerThanTailWindow_StillFindsEndOfCentralDirectory()
    {
        var rom = MakeNdsRom(4096, 10);
        var zip = WithZipComment(BuildZip(("fake.nds", rom, CompressionLevel.Optimal)), 5000);

        var result = await ProbeZipAsync(zip, "commented.zip", false);

        Assert.IsNotNull(result);
        Assert.AreEqual("fake.nds", result.InnerName);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
    }

    // 7z

    [TestMethod]
    public async Task SevenZipRomProbe_ReadsCrc32AndInnerNameFromHeader()
    {
        var archive = Convert.FromBase64String(SevenZipFixtureBase64);

        await using var stream = new MemoryStream(archive, false);
        var result = await new SevenZipRomProbe().ProbeAsync(stream, "Some Game (USA).7z", false);

        Assert.IsNotNull(result);
        Assert.AreEqual("fake.nds", result.InnerName);
        Assert.AreEqual(ContainerKind.SevenZip, result.Container);
        Assert.AreEqual(4096, result.UncompressedSize);
        Assert.AreEqual(FixtureRomCrc32, result.Crc32);
        Assert.AreEqual(Crc32.HashToUInt32(MakeNdsRom()), result.Crc32);
        Assert.IsNull(result.Header);
    }

    [TestMethod]
    public async Task SevenZipRomProbe_WantHeader_DecodesFirst512Bytes()
    {
        var archive = Convert.FromBase64String(SevenZipFixtureBase64);

        await using var stream = new MemoryStream(archive, false);
        var result = await new SevenZipRomProbe().ProbeAsync(stream, "game.7z", true);

        Assert.IsNotNull(result);
        AssertNdsHeader(result.Header);
    }

    [TestMethod]
    public void SevenZipRomProbe_ZeroCrc_IsUnknownNotZero()
    {
        // SharpCompress reports 0 for both "no CRC recorded" and "CRC is
        // genuinely 0", so 0 must fall through the ladder rather than be looked up in the index.
        Assert.IsNull(SevenZipRomProbe.NormalizeCrc(0));
        Assert.AreEqual(0xC1F8B000u, SevenZipRomProbe.NormalizeCrc(0xC1F8B000L));

        // SharpCompress widens the 32-bit digest into a long; a sign-extended value must survive.
        Assert.AreEqual(0xC1F8B000u, SevenZipRomProbe.NormalizeCrc(unchecked((int)0xC1F8B000)));
    }

    // dispatcher

    [TestMethod]
    public async Task RomProbeService_MislabelledArchive_DispatchesOnMagicNotExtension()
    {
        var rom = MakeNdsRom(4096, 11);
        var zip = BuildZip(("fake.nds", rom, CompressionLevel.Optimal));

        // A zip wearing a bare-ROM extension. Extension-first dispatch would read it as a loose ROM
        // and hash the container instead of the game.
        var path = await WriteTempAsync("liar.nds", zip);
        var result = await new RomProbeService().ProbeFileAsync(path, false);

        Assert.IsNotNull(result);
        Assert.AreEqual(ContainerKind.Zip, result.Container);
        Assert.AreEqual("fake.nds", result.InnerName);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
    }

    [TestMethod]
    public async Task RomProbeService_SevenZipUnderZipExtension_IsStillRead()
    {
        var path = await WriteTempAsync("liar.zip", Convert.FromBase64String(SevenZipFixtureBase64));
        var result = await new RomProbeService().ProbeFileAsync(path, false);

        Assert.IsNotNull(result);
        Assert.AreEqual(ContainerKind.SevenZip, result.Container);
        Assert.AreEqual(FixtureRomCrc32, result.Crc32);
    }

    [TestMethod]
    public async Task RomProbeService_LooseRom_IsProbedAndCounted()
    {
        var rom = MakeNdsRom();
        var path = await WriteTempAsync("fake.nds", rom);
        var result = await new RomProbeService().ProbeFileAsync(path, true);

        Assert.IsNotNull(result);
        Assert.AreEqual(ContainerKind.Loose, result.Container);
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
        AssertNdsHeader(result.Header);

        // The magic sniff is real I/O and is included, so the tally exceeds the file by exactly it.
        Assert.AreEqual(rom.Length + 6, result.BytesRead);
    }

    [TestMethod]
    public async Task RomProbeService_CorruptArchive_ReturnsNullWithoutThrowing()
    {
        var junk = new byte[512];
        junk[0] = 0x50;
        junk[1] = 0x4B;
        junk[2] = 0x03;
        junk[3] = 0x04;

        var path = await WriteTempAsync("truncated.zip", junk);
        Assert.IsNull(await new RomProbeService().ProbeFileAsync(path, true));
    }

    [TestMethod]
    public async Task RomProbeService_MissingFile_ReturnsNullWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.zip");

        Assert.IsNull(await new RomProbeService().ProbeFileAsync(path, false));
    }

    // lz77 (Nintendo-LZ77-wrapped SNES/Mega Drive/etc., the ".lz77.<ext>" convention nds-bootstrap loads)

    [TestMethod]
    public async Task Lz77RomProbe_DecompressesAndIdentifiesAsThePlainRom()
    {
        // A non-multiple-of-8 length exercises the partial final flag group and the exact-length stop.
        var rom = MakeRawRom(5003, 21);
        var packed = Lz77PackLiterals(rom);

        await using var stream = new MemoryStream(packed, false);
        var result = await new Lz77RomProbe().ProbeAsync(stream, "game.lz77.sfc", true);

        Assert.IsNotNull(result);
        Assert.AreEqual(ContainerKind.Lz77, result.Container);

        // The identifying hash is of the INFLATED ROM - byte-identical to the plain .sfc, so it matches
        // the very No-Intro entry the uncompressed cart would.
        Assert.AreEqual(Crc32.HashToUInt32(rom), result.Crc32);
        Assert.AreEqual(rom.Length, result.UncompressedSize);

        // The cover has to be named for the file ON THE CARD, not the stripped inner name, or the menu
        // never finds it.
        Assert.AreEqual("game.lz77.sfc", result.InnerName);

        // The tally is the compressed bytes actually read, not the larger inflated stream.
        Assert.AreEqual(packed.Length, result.BytesRead);
        Assert.IsNotNull(result.Header);
    }

    [TestMethod]
    public async Task RomProbeService_RoutesLz77Files_AndLeavesPlainRomsAlone()
    {
        var rom = MakeRawRom(4096, 22);

        // A compressed Mega Drive ROM routes to the LZ77 probe and identifies off the inflated bytes.
        var lz77Path = await WriteTempAsync("game.lz77.gen", Lz77PackLiterals(rom));
        var compressed = await new RomProbeService().ProbeFileAsync(lz77Path, false);

        Assert.IsNotNull(compressed);
        Assert.AreEqual(ContainerKind.Lz77, compressed.Container);
        Assert.AreEqual(Crc32.HashToUInt32(rom), compressed.Crc32);
        Assert.AreEqual("game.lz77.gen", compressed.InnerName);

        // The SAME bytes as a plain .gen still route to the loose probe, untouched by the new branch:
        // the two produce the same CRC, so the compressed cart resolves to the same game as the plain one.
        var plainPath = await WriteTempAsync("game.gen", rom);
        var plain = await new RomProbeService().ProbeFileAsync(plainPath, false);

        Assert.IsNotNull(plain);
        Assert.AreEqual(ContainerKind.Loose, plain.Container);
        Assert.AreEqual(Crc32.HashToUInt32(rom), plain.Crc32);
    }

    [TestMethod]
    public void Lz77RomProbe_TryDecompress_DecodesLiteralsAndBackReferences()
    {
        // All literals: header (length 5), one flag byte of zeros, then the five bytes.
        byte[] literals = [0x10, 0x05, 0x00, 0x00, 0x00, (byte)'H', (byte)'E', (byte)'L', (byte)'L', (byte)'O'];
        Assert.IsTrue(Lz77RomProbe.TryDecompress(literals, out var hello));
        Assert.AreEqual("HELLO", Encoding.ASCII.GetString(hello));

        // "AAAA": a literal 'A', then a 3-byte back-reference at distance 1 (flag bit 1 set = 0x40).
        byte[] backref = [0x10, 0x04, 0x00, 0x00, 0x40, (byte)'A', 0x00, 0x00];
        Assert.IsTrue(Lz77RomProbe.TryDecompress(backref, out var aaaa));
        Assert.AreEqual("AAAA", Encoding.ASCII.GetString(aaaa));
    }

    [TestMethod]
    public void Lz77RomProbe_TryDecompress_RejectsMalformedStreams()
    {
        // Wrong type byte.
        Assert.IsFalse(Lz77RomProbe.TryDecompress([0x11, 0x04, 0x00, 0x00, 0x00, 1, 2, 3, 4], out _));
        // Declares eight bytes, but the input runs out first.
        Assert.IsFalse(Lz77RomProbe.TryDecompress([0x10, 0x08, 0x00, 0x00, 0x00, 1, 2, 3], out _));
        // First token is a back-reference, with nothing decoded to point back at.
        Assert.IsFalse(Lz77RomProbe.TryDecompress([0x10, 0x04, 0x00, 0x00, 0x80, 0x00, 0x00], out _));
        // Too short to even hold a header.
        Assert.IsFalse(Lz77RomProbe.TryDecompress([0x10, 0x04], out _));
    }

    [TestMethod]
    public async Task RomProbeService_CorruptLz77_ReturnsNullWithoutThrowing()
    {
        // The right name, but the bytes are not a valid LZ77 stream. A scan skips it, never throws.
        var junk = new byte[1024];
        junk[0] = 0xFF;
        var path = await WriteTempAsync("broken.lz77.sfc", junk);

        Assert.IsNull(await new RomProbeService().ProbeFileAsync(path, true));
    }

    // entry selection

    [TestMethod]
    public void ArchiveEntrySelector_RomExtensionBeatsALargerBlob()
    {
        var pick = ArchiveEntrySelector.Select(
        [
            new ArchiveEntryCandidate(0, "huge_blob", 100 * 1024 * 1024),
            new ArchiveEntryCandidate(1, "game.gba", 4096)
        ]);

        Assert.AreEqual("game.gba", pick?.Name);
    }

    [TestMethod]
    public void ArchiveEntrySelector_JunkStemDoesNotOutrankTheGame()
    {
        // .md is Mega Drive by extension, but a README stored before the game is still not the ROM.
        var pick = ArchiveEntrySelector.Select(
        [
            new ArchiveEntryCandidate(0, "README.md", 4096),
            new ArchiveEntryCandidate(1, "game.gen", 512 * 1024)
        ]);

        Assert.AreEqual("game.gen", pick?.Name);
    }

    [TestMethod]
    public void ArchiveEntrySelector_SingleUnnamedBlob_IsAccepted()
    {
        var pick = ArchiveEntrySelector.Select([new ArchiveEntryCandidate(0, "00000000", 512 * 1024)]);

        Assert.AreEqual("00000000", pick?.Name);
    }

    [TestMethod]
    public void ArchiveEntrySelector_TinyLoneBlob_IsRejected()
    {
        Assert.IsNull(ArchiveEntrySelector.Select([new ArchiveEntryCandidate(0, "tik", 2472)]));
    }

    [TestMethod]
    public void ArchiveEntrySelector_DirectoriesAreSkipped()
    {
        var pick = ArchiveEntrySelector.Select(
        [
            new ArchiveEntryCandidate(0, "roms/", 0),
            new ArchiveEntryCandidate(1, "roms/game.NES", 40960)
        ]);

        Assert.AreEqual("roms/game.NES", pick?.Name);
    }

    [TestMethod]
    public void ArchiveEntrySelector_ZeroByteEntry_IsSkipped()
    {
        // A zip records a real CRC32 of 0 for an empty entry, so an empty "ROM" would otherwise put a
        // genuine-looking 0x00000000 into the index lookup.
        Assert.IsNull(ArchiveEntrySelector.Select([new ArchiveEntryCandidate(0, "game.nds", 0)]));

        var pick = ArchiveEntrySelector.Select(
        [
            new ArchiveEntryCandidate(0, "empty.nds", 0),
            new ArchiveEntryCandidate(1, "real.nds", 4096)
        ]);

        Assert.AreEqual("real.nds", pick?.Name);
    }

    [TestMethod]
    public void ArchiveEntrySelector_LargeNonRomFile_IsNotMistakenForAContentBlob()
    {
        Assert.IsNull(ArchiveEntrySelector.Select([new ArchiveEntryCandidate(0, "scans.jpg", 4 * 1024 * 1024)]));
    }

    // helpers

    /// <summary>
    /// A synthetic NDS ROM: 12-char title at 0x00, 4-char game code at 0x0C, NDS magic at 0xC0.
    /// With the default arguments it is byte-identical to the entry inside the .7z fixture above,
    /// so its CRC32 is the published 0xC1F8B000.
    /// </summary>
    private static byte[] MakeNdsRom(int size = 4096, int seed = 0)
    {
        var rom = new byte[size];
        if (seed != 0)
        {
            new Random(seed).NextBytes(rom);
        }

        Encoding.ASCII.GetBytes("NEWSUPERMARI").CopyTo(rom, 0x00);
        Encoding.ASCII.GetBytes("A2DE").CopyTo(rom, 0x0C);
        BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(0xC0), 0x24FFAE51);
        return rom;
    }

    private static void AssertNdsHeader(byte[]? header)
    {
        Assert.IsNotNull(header);
        Assert.AreEqual(512, header.Length);
        Assert.AreEqual("NEWSUPERMARI", Encoding.ASCII.GetString(header, 0x00, 12));
        Assert.AreEqual("A2DE", Encoding.ASCII.GetString(header, 0x0C, 4));
        Assert.AreEqual(0x24FFAE51u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0xC0)));
    }

    /// <summary>Deterministic random bytes standing in for a headerless ROM (SNES/Mega Drive identify by CRC32).</summary>
    private static byte[] MakeRawRom(int size, int seed)
    {
        var rom = new byte[size];
        new Random(seed).NextBytes(rom);
        return rom;
    }

    /// <summary>
    /// Wraps bytes in a valid Nintendo LZ77 (type 0x10) stream using literal tokens only - the simplest
    /// stream the decoder must accept, and enough to prove the round trip end to end. The back-reference
    /// path is asserted separately against hand-built vectors.
    /// </summary>
    private static byte[] Lz77PackLiterals(byte[] data)
    {
        var output = new List<byte>(data.Length + data.Length / 8 + 4)
        {
            0x10,
            (byte)data.Length,
            (byte)(data.Length >> 8),
            (byte)(data.Length >> 16)
        };

        for (var i = 0; i < data.Length; i += 8)
        {
            output.Add(0x00); // eight literal flags; the decoder stops at the declared length
            for (var j = i; j < i + 8 && j < data.Length; j++)
            {
                output.Add(data[j]);
            }
        }

        return output.ToArray();
    }

    private static byte[] BuildZip(params (string Name, byte[] Data, CompressionLevel Level)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (name, data, level) in entries)
            {
                using var entry = archive.CreateEntry(name, level).Open();
                entry.Write(data);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Bolts a zero-filled comment onto a freshly built zip. <see cref="ZipArchive"/> cannot write
    /// one, and a comment longer than the probe's tail window is the case that forces the EOCD rescan.
    /// </summary>
    private static byte[] WithZipComment(byte[] zip, int commentLength)
    {
        const int eocdSize = 22;
        var padded = new byte[zip.Length + commentLength];
        zip.CopyTo(padded, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(padded.AsSpan(zip.Length - eocdSize + 20), (ushort)commentLength);
        return padded;
    }

    private static async Task<ProbeResult?> ProbeZipAsync(byte[] zip, string path, bool wantHeader)
    {
        await using var stream = new MemoryStream(zip, false);
        return await new ZipRomProbe().ProbeAsync(stream, path, wantHeader);
    }

    /// <summary>
    /// Temp directories created by <see cref="WriteTempAsync"/>, removed in <see cref="Cleanup"/>.
    /// </summary>
    /// <remarks>
    /// Tracked rather than deleted per-file because each call makes its own directory: callers used to
    /// delete only the FILE, leaking one directory per invocation on every test run, forever.
    /// </remarks>
    private readonly List<string> _tempDirectories = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
                // A file still mapped by a probe that failed mid-test is not worth failing the run over.
            }
        }

        _tempDirectories.Clear();
    }

    private async Task<string> WriteTempAsync(string name, byte[] content)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twlprobe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
