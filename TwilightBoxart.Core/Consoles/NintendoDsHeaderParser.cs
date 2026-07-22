using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Nintendo DS and DSi. Recognised by the compressed Nintendo logo at 0xC0, then split by the unitcode
/// byte at 0x12.
/// </summary>
public sealed class NintendoDsHeaderParser : IConsoleHeaderParser
{
    /// <summary>The same logo data the GBA carries at 0x04; on NDS it lives at 0xC0.</summary>
    private static ReadOnlySpan<byte> NintendoLogo => [0x24, 0xFF, 0xAE, 0x51];

    private const int TitleOffset = 0x00;
    private const int GameCodeOffset = 0x0C;
    private const int RegionOffset = 0x0F;
    private const int UnitCodeOffset = 0x12;
    private const int LogoOffset = 0xC0;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        if (!header.Match(LogoOffset, NintendoLogo))
        {
            return null;
        }

        // THE fix in this file. Byte 0x12 is unitcode:
        //   0x00 = NDS only
        //   0x02 = DSi-ENHANCED - a hybrid that boots on both, with DSi-exclusive extras
        //   0x03 = DSi exclusive
        //
        // The 2020 code tested `header[0x12] == 0x03` and therefore routed every single 0x02
        // hybrid to NintendoDs, losing both the correct art partition and the DSiWare placeholder path.
        // Anything with DSi capability is a DSi title for art purposes.
        //
        // An unrecognised unitcode falls through to NDS rather than being rejected: the logo match is
        // already definitive, so a strict test here would only manufacture false negatives.
        var unitCode = header.ByteAt(UnitCodeOffset);
        var isDsiCapable = unitCode is 0x02 or 0x03;

        // A 0x02 hybrid is a DSi title that No-Intro files under "Nintendo - Nintendo DS", so the index
        // lookup has to be prepared to try both. 0x03 is DSi-exclusive and unambiguous in both.
        var alternate = unitCode == 0x02 ? ConsoleType.NintendoDs : ConsoleType.Unknown;

        return new HeaderDetection
        {
            ConsoleType = isDsiCapable ? ConsoleType.NintendoDsi : ConsoleType.NintendoDs,
            AlternateConsoleType = alternate,
            Title = header.ReadAscii(TitleOffset, 12),
            Serial = header.ReadGameCode(GameCodeOffset, 4),

            // Read straight from 0x0F rather than from Serial[3], so a game code that failed validation
            // (homebrew with a partly-binary code) still yields a usable region for the art request.
            RegionId = header.PrintableCharAt(RegionOffset),
        };
    }
}
