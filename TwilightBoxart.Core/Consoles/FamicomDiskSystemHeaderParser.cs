using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Famicom Disk System, via the disk info block at offset 0 - optionally behind a 16-byte fwNES header.
/// </summary>
public sealed class FamicomDiskSystemHeaderParser : IConsoleHeaderParser
{
    /// <summary>
    /// Block type 0x01 followed by the start of the literal "*NINTENDO-HVC*" verification string.
    /// </summary>
    private static ReadOnlySpan<byte> DiskInfoMagic => [0x01, 0x2A, 0x4E, 0x49, 0x4E];

    /// <summary>"FDS" + EOF - the fwNES wrapper, the FDS equivalent of an iNES header.</summary>
    private static ReadOnlySpan<byte> FwNesMagic => [0x46, 0x44, 0x53, 0x1A];

    private const int FwNesHeaderLength = 16;

    /// <summary>Offset within the disk info block of the 3-character game name.</summary>
    private const int GameNameOffset = 0x10;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        // Same double-lookup problem as iNES: the wrapper may or may not be inside the hashed bytes.
        var wrapper = header.Match(0, FwNesMagic) ? FwNesHeaderLength : 0;
        if (!header.Match(wrapper, DiskInfoMagic))
        {
            return null;
        }

        // The 3-character game name (e.g. "ZEL" for Zelda), followed by a game-type character. There is
        // no title string anywhere in an FDS header - this abbreviation is all the disk carries.
        //
        // The disk stores only the bare code, but No-Intro writes FDS serials with a manufacturer
        // prefix ("FMC-ZEL"). The index builder reduces those to the bare code (DatParser.NormalizeSerial)
        // so the stored serial is what this returns and a straight equality lookup matches. Without it the
        // FDS serial rung would miss every row despite the platform's 86.7% serial coverage.
        return new HeaderDetection
        {
            ConsoleType = ConsoleType.FamicomDiskSystem,
            Serial = header.ReadGameCode(wrapper + GameNameOffset, 3),
            LeadingHeaderBytes = wrapper,
        };
    }
}
