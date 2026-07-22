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
            [".msx"] = ConsoleType.Msx,
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
        ".zip", ".7z",
    };

    /// <summary>
    /// Everything a scan will open: <see cref="Rom"/> plus <see cref="Archive"/>. This is the set the
    /// server publishes at <c>GET /v2/formats</c> and the fallback a client uses when it cannot reach it.
    /// </summary>
    public static readonly IReadOnlySet<string> Scannable =
        Rom.Concat(Archive).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsRom(string path) => Rom.Contains(Path.GetExtension(path));

    public static bool IsArchive(string path) => Archive.Contains(Path.GetExtension(path));

    public static bool IsScannable(string path) => Scannable.Contains(Path.GetExtension(path));
}
