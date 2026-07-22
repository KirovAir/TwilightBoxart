using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// NES, via the iNES / NES 2.0 container header at offset 0.
/// </summary>
/// <remarks>
/// This detects the <i>container</i>, not the game: an iNES header carries a mapper number and sizes,
/// no title and no serial. NES is 33% of the index at 0% serial coverage, so identification is entirely
/// a hash lookup - which is why <see cref="HeaderDetection.LeadingHeaderBytes"/> matters more here than
/// anywhere else. See <see cref="TryParse"/>.
/// </remarks>
public sealed class NesHeaderParser : IConsoleHeaderParser
{
    /// <summary>"NES" followed by the MS-DOS EOF character.</summary>
    private static ReadOnlySpan<byte> InesMagic => [0x4E, 0x45, 0x53, 0x1A];

    /// <summary>iNES and NES 2.0 both prefix the ROM with exactly 16 bytes.</summary>
    private const int InesHeaderLength = 16;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        if (!header.Match(0, InesMagic))
        {
            return null;
        }

        // LeadingHeaderBytes = 16 is the whole point of this parser. No-Intro hashes the ROM data and
        // real-world .nes files carry the container header, so a CRC32 or SHA-1 taken over the file as
        // it sits on disk will not match the index. The identification ladder must try the lookup BOTH
        // over the whole file and over the file minus these 16 bytes.
        //
        // A headerless .nes has no magic and is not detected here at all - correctly, since there is
        // nothing in the bytes to detect. It falls through to the extension and hash paths.
        return new HeaderDetection
        {
            ConsoleType = ConsoleType.Nes,
            LeadingHeaderBytes = InesHeaderLength,
        };
    }
}
