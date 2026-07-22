using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// The three byte orders N64 dumps circulate in. The magic at offset 0 is the same 32-bit word in all
/// three; only its byte order differs, which is what makes it a reliable discriminator.
/// </summary>
public enum N64ByteOrder
{
    /// <summary>Not an N64 image.</summary>
    None = 0,

    /// <summary>.z64 - native big-endian, <c>80 37 12 40</c>. What No-Intro hashes.</summary>
    BigEndian,

    /// <summary>.v64 - 16-bit byte-swapped (Doctor V64), <c>37 80 40 12</c>.</summary>
    ByteSwapped,

    /// <summary>.n64 - 32-bit little-endian, <c>40 12 37 80</c>.</summary>
    LittleEndian,
}

/// <summary>
/// What <see cref="ConsoleDetector.Detect(ReadOnlySpan{byte})"/> could establish from a ROM's leading
/// bytes alone.
/// </summary>
/// <remarks>
/// This is deliberately header-only: no index lookup, no filename, no guessing. Anything that needs
/// corroboration is surfaced as a separate property (<see cref="AlternateConsoleType"/>,
/// <see cref="LeadingHeaderBytes"/>) rather than resolved here, so the identification ladder can spend
/// a lookup on it and this stays a pure function of the bytes.
/// </remarks>
public sealed record HeaderDetection
{
    /// <summary>The console the header claims to be for, or <see cref="ConsoleType.Unknown"/>.</summary>
    public required ConsoleType ConsoleType { get; init; }

    /// <summary>Game code / title id from the header ("ASME"), when the header carries a usable one.</summary>
    public string? Serial { get; init; }

    /// <summary>Internal title from the header, padding trimmed, when present.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The NDS/DSi region character at header byte 0x0F. Feed it to <see cref="GameTdbRegion"/>.
    /// </summary>
    /// <remarks>
    /// Only ever set for NDS and DSi. Mega Drive and N64 carry a region byte too, but their letters
    /// mean different things ('E' is Europe on a Mega Drive and USA on a DS), so exposing them through
    /// the same property would invite a caller to run the GameTDB mapping over the wrong alphabet.
    /// GBA's game code does use the DS alphabet - its region letter is the 4th char of <see cref="Serial"/>.
    /// </remarks>
    public char? RegionId { get; init; }

    /// <summary>
    /// A second console worth trying against the index, for the cases where the header is genuinely
    /// ambiguous about which No-Intro set the ROM belongs to. <see cref="ConsoleType.Unknown"/> when
    /// the header is unambiguous.
    /// </summary>
    /// <remarks>
    /// Two real cases, both measured:
    /// <list type="bullet">
    /// <item>A DSi-enhanced hybrid (unitcode 0x02) is a DSi title that No-Intro files under Nintendo DS.</item>
    /// <item>A Game Boy cart's CGB flag says what the silicon supports, not which set No-Intro filed it
    /// in; 7 GB-filed and 2 GBC-filed carts in the corpus disagree with their own flag.</item>
    /// </list>
    /// Try <see cref="ConsoleType"/> first and fall back to this - never the other way round.
    /// </remarks>
    public ConsoleType AlternateConsoleType { get; init; } = ConsoleType.Unknown;

    /// <summary>
    /// Size in bytes of a container/copier header that precedes the ROM data proper: 16 for iNES and
    /// fwNES, 512 for an SNES SMC copier header. Zero when the file starts at the ROM.
    /// </summary>
    /// <remarks>
    /// This is the "double lookup" signal. No-Intro and real-world files disagree about whether these
    /// bytes are inside the hashed region, so a CRC32/SHA-1 lookup must be attempted both over the whole
    /// file and over the file minus this prefix. It matters most for NES: 33% of the index at 0% serial
    /// coverage, so the hash is the only thing that can identify it.
    /// </remarks>
    public int LeadingHeaderBytes { get; init; }

    /// <summary>The detected N64 byte order; <see cref="N64ByteOrder.None"/> for every other console.</summary>
    /// <remarks>
    /// The caller needs this to hash correctly: No-Intro CRCs are over the big-endian .z64 form, so a
    /// .v64 or .n64 image must be converted before its hash will ever match.
    /// </remarks>
    public N64ByteOrder ByteOrder { get; init; }

    /// <summary>True when the header identified a console.</summary>
    public bool IsDetected => ConsoleType != ConsoleType.Unknown;

    /// <summary>The "no header matched" result. Never null, so callers need no null check.</summary>
    public static readonly HeaderDetection None = new() { ConsoleType = ConsoleType.Unknown };
}
