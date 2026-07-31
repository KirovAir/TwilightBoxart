using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Probe;

/// <summary>
/// What a cheap look at a ROM container yields. For archives this comes from the container's own
/// header, with no decompression: a .zip needs one 4 KiB tail read
/// (116-508 bytes actually used), a .7z needs 32 bytes plus a tail slice.
/// </summary>
public sealed record ProbeResult
{
    /// <summary>The ROM's name inside the container, or the file's own name when loose.</summary>
    public required string InnerName { get; init; }

    /// <summary>Uncompressed size of the ROM in bytes.</summary>
    public required long UncompressedSize { get; init; }

    /// <summary>
    /// CRC32 of the uncompressed ROM, straight from the container header.
    /// Null when unavailable; note 7z reports 0 for both "absent" and "genuinely zero"
    /// (the IsAvailable flag lives on an internal ChecksumDescriptor), so 0 MUST be treated
    /// as unknown and fall through the ladder.
    /// </summary>
    public uint? Crc32 { get; init; }

    /// <summary>First bytes of the ROM, when they were cheap to obtain.</summary>
    public byte[]? Header { get; init; }

    public required ContainerKind Container { get; init; }

    /// <summary>Bytes actually read off disk. Surfaced so the cheap paths stay honest in tests.</summary>
    public long BytesRead { get; init; }
}

public enum ContainerKind
{
    Loose = 0,
    Zip,
    SevenZip,

    /// <summary>A bare ROM under a Nintendo-LZ77 wrapper (<c>Game.lz77.sfc</c>); see <see cref="Lz77RomProbe"/>.</summary>
    Lz77
}

/// <summary>Reads identification data out of a ROM container as cheaply as possible.</summary>
public interface IRomProbe
{
    /// <summary>
    /// Every FRONT-LOADED header ends inside the first 512 bytes, and the number is exact: the
    /// Mega Drive header - the largest one - occupies 0x100..0x1FF, so byte 511 is its last. The
    /// two consoles that keep their header deeper (SNES at 32-64 KB, SMS/GG at up to 32 KB) are
    /// deliberately not chased: they identify by checksum and name instead, and a sample big
    /// enough to reach them would be 64x this size on every wire that carries one.
    /// </summary>
    const int HeaderBytesWanted = 512;

    /// <summary>True when this probe recognises the file (by extension and/or magic).</summary>
    bool CanHandle(string path);

    /// <summary>
    /// Probes the container. <paramref name="wantHeader"/> requests the ROM's leading bytes, which
    /// may cost a bounded partial decompression; leave it false when a CRC32 lookup will do.
    /// </summary>
    Task<ProbeResult?> ProbeAsync(Stream stream, string path, bool wantHeader, CancellationToken ct = default);
}

/// <summary>A console and the ROM extensions a card carries for it.</summary>
public sealed record ConsoleFiles(ConsoleType Console, IReadOnlyList<string> Extensions);

/// <summary>Container extensions the scanner will open, and the ROM extensions it looks for inside.</summary>
public static class SupportedFiles
{
    /// <summary>
    /// Bare ROM extensions mapped to the console each one hints at. This is the single source of truth
    /// for "what is a ROM": <see cref="Rom"/> is its key set, and the identification ladder reads it as
    /// a weak console hint for the fuzzy name search (IdentificationLadder.ConsoleFromExtension).
    ///
    /// NOTE vs the 2020 client: .n64/.z64/.v64 are new (946 N64 files were invisible), and matching must
    /// be case-insensitive (the old zip-entry check was not).
    ///
    /// The list is kept in step with the extension list TWiLightMenu++ itself browses
    /// (romsel_dsimenutheme/arm9/source/main.cpp), because a ROM the menu will launch but we will not
    /// scan is a cover the user can never get. Deliberately NOT included, despite the menu accepting
    /// them: .xex/.atr (Atari 8-bit: a No-Intro DAT exists but there is no libretro-thumbnails
    /// repository, so every lookup would 404), .m5 (Sord M5: neither), .dsk (Amstrad CPC: no
    /// No-Intro DAT, and .dsk is a generic disk image shared with MSX and ZX Spectrum), and
    /// .3ds/.cia/.cxi (an April Fools easter egg in the menu, gated on the system date, whose
    /// launcher only plays a fake boot animation).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ConsoleType> RomExtensions =
        new Dictionary<string, ConsoleType>(StringComparer.OrdinalIgnoreCase)
        {
            [".nes"] = ConsoleType.Nes,
            [".fds"] = ConsoleType.FamicomDiskSystem,
            [".sfc"] = ConsoleType.Snes,
            [".smc"] = ConsoleType.Snes,
            [".snes"] = ConsoleType.Snes,
            [".gb"] = ConsoleType.GameBoy,
            [".sgb"] = ConsoleType.GameBoy,
            [".gbc"] = ConsoleType.GameBoyColor,
            [".gba"] = ConsoleType.GameBoyAdvance,
            [".agb"] = ConsoleType.GameBoyAdvance,
            [".mb"] = ConsoleType.GameBoyAdvance,
            [".nds"] = ConsoleType.NintendoDs,
            [".ds"] = ConsoleType.NintendoDs,
            [".srl"] = ConsoleType.NintendoDs,
            [".ids"] = ConsoleType.NintendoDs,
            [".dsi"] = ConsoleType.NintendoDsi,

            // The menu groups .app with the DS extensions, but that grouping only says "launches via
            // nds-bootstrap" - both DS and DSi titles do. A bare .app on a card is almost always a
            // DSiWare content dump, so that is the better hint. Nothing rests on it either way: the
            // header's unitcode decides DS vs DSi, and AlternateConsoleType covers the hybrids.
            [".app"] = ConsoleType.NintendoDsi,

            [".n64"] = ConsoleType.Nintendo64,
            [".z64"] = ConsoleType.Nintendo64,
            [".v64"] = ConsoleType.Nintendo64,
            [".gg"] = ConsoleType.GameGear,
            [".gen"] = ConsoleType.MegaDrive,
            [".md"] = ConsoleType.MegaDrive,
            [".sms"] = ConsoleType.MasterSystem,
            [".min"] = ConsoleType.PokemonMini,

            // SC-3000 titles live in the same No-Intro set and the same thumbnails repository as the
            // SG-1000, so both extensions point at one console.
            [".sg"] = ConsoleType.Sg1000,
            [".sc"] = ConsoleType.Sg1000,

            [".pce"] = ConsoleType.PcEngine,
            [".ws"] = ConsoleType.WonderSwan,
            [".wsc"] = ConsoleType.WonderSwanColor,
            [".ngp"] = ConsoleType.NeoGeoPocket,
            [".ngc"] = ConsoleType.NeoGeoPocketColor,
            [".a26"] = ConsoleType.Atari2600,
            [".a52"] = ConsoleType.Atari5200,
            [".a78"] = ConsoleType.Atari7800,
            [".col"] = ConsoleType.ColecoVision,
            [".int"] = ConsoleType.Intellivision,

            // Both MSX generations share this extension; see ConsoleType.Msx2 for why they are still
            // two consoles.
            [".msx"] = ConsoleType.Msx
        };

    /// <summary>Bare ROM extensions the scanner looks for. The key set of <see cref="RomExtensions"/>.</summary>
    public static readonly IReadOnlySet<string> Rom =
        RomExtensions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Archive containers. .7z is new: it was absent from the 2020 list, which made the entire
    /// 3,996-file Nintendo DS collection invisible before a single byte was read.
    /// </summary>
    public static readonly IReadOnlySet<string> Archive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z"
    };

    /// <summary>
    /// Everything a scan will open: <see cref="Rom"/> plus <see cref="Archive"/>. This is the set the
    /// server publishes at <c>GET /v2/formats</c> and the fallback a client uses when it cannot reach it.
    /// </summary>
    public static readonly IReadOnlySet<string> Scannable =
        Rom.Concat(Archive).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Directory names that never hold ROMs at any depth, matched case-insensitively: the launchers'
    /// own data (including the covers this tool writes), hiyaCFW's folder, the 3DS SD layout, and OS
    /// metadata folders. Only names specific enough that nobody keeps games under them belong here;
    /// the generic NAND names live in <see cref="SkipRootDirectories"/>. Published as
    /// <c>skipdirs=</c> at <c>GET /v2/formats</c>, so a name learned from the logs reaches shipped
    /// clients without a release.
    /// </summary>
    public static readonly IReadOnlySet<string> SkipDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "_nds", "_pico", "hiya", "Nintendo 3DS",
        "System Volume Information", "$RECYCLE.BIN", ".Trashes", ".Spotlight-V100", ".fseventsd",
        ".TemporaryItems", "found.000"
    };

    /// <summary>
    /// The rest of the DSi NAND layout hiyaCFW and Unlaunch mirror onto the SD card; title/ alone
    /// floods a scan with content .app files that can never identify. Skipped at the scan root
    /// ONLY, because the names are generic: a collection folder someone called "import" or "sys"
    /// deeper down must still be scanned. Published as <c>skiprootdirs=</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> SkipRootDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "title", "ticket", "sys", "shared1", "shared2", "import", "progress", "private"
    };

    /// <summary>
    /// Documentation stems (a file name minus its last extension) that are never ROMs whatever that
    /// extension says: .md is Markdown as well as Mega Drive, so every scene pack README.md scanned
    /// as a ROM and missed twice, once by name and once on the CRC retry. Published as
    /// <c>skipfiles=</c> beside <see cref="SkipDirectories"/>.
    /// </summary>
    public static readonly IReadOnlySet<string> SkipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "license", "licence", "copying", "changelog", "authors", "contributing",
        "notice", "notices", "third_party_notices", "pdf_source_release"
    };

    /// <summary>
    /// No supported console ships a ROM smaller than this (the smallest real dumps are 2 KiB Atari
    /// 2600 carts), so anything under it is a stray text or data file. Production logs showed
    /// 31-byte READMEs being CRC-retried; clients drop such files before fingerprinting.
    /// </summary>
    public const int MinimumRomBytes = 512;

    /// <summary>
    /// Every console a scan can produce a cover for, paired with the extensions a card carries for it.
    /// This is what a user is shown, so it is NOT just the inverse of <see cref="RomExtensions"/>:
    /// MSX2 has no extension of its own (see <see cref="ConsoleType.Msx2"/>) and arrives on .msx like
    /// MSX does, and reading the map alone would list it as unsupported when its covers work fine.
    /// </summary>
    public static readonly IReadOnlyList<ConsoleFiles> ByConsole = BuildByConsole();

    private static IReadOnlyList<ConsoleFiles> BuildByConsole()
    {
        var extensions = RomExtensions
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group]);

        extensions[ConsoleType.Msx2] = extensions[ConsoleType.Msx];

        // An empty list rather than an indexer: a console added to the enum without an extension is a
        // missing row on a page, not a type initializer that takes the server down at startup.
        // ConsoleFileTypesTests pins that it never actually happens.
        return
        [
            .. Enum.GetValues<ConsoleType>()
                .Where(console => console != ConsoleType.Unknown)
                .Select(console => new ConsoleFiles(console, extensions.GetValueOrDefault(console, [])))
        ];
    }

    public static bool IsRom(string path)
    {
        return Rom.Contains(Path.GetExtension(path));
    }

    public static bool IsArchive(string path)
    {
        return Archive.Contains(Path.GetExtension(path));
    }

    public static bool IsScannable(string path)
    {
        return Scannable.Contains(Path.GetExtension(path));
    }

    /// <summary>Documentation wearing a ROM extension; see <see cref="SkipFiles"/>.</summary>
    public static bool IsSkipFile(string path)
    {
        return SkipFiles.Contains(Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Whether this file's header carries a title id good enough to identify it without a checksum.
    /// </summary>
    /// <remarks>
    /// DS and DSi only. Their title id resolves 97.8% and 99.3% of dumps, and they are also the only
    /// files that routinely run past a hundred megabytes, which is the pairing that makes skipping
    /// the hash worth it. GBA carries one too but at 87.7%, and a GBA ROM is small enough that the
    /// hash is free, so it is not worth the miss. Everything else has nothing better than a
    /// checksum. Used by the probes to decide when a full read may be skipped; see
    /// <see cref="Probe.LooseRomProbe"/>.
    /// </remarks>
    public static bool HasTitleId(string path)
    {
        return RomExtensions.TryGetValue(Path.GetExtension(path), out var console)
               && console is ConsoleType.NintendoDs or ConsoleType.NintendoDsi;
    }
}
