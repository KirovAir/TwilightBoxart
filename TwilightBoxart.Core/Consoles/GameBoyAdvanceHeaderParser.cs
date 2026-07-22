using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Game Boy Advance. Recognised by the compressed Nintendo logo at 0x04.
/// </summary>
public sealed class GameBoyAdvanceHeaderParser : IConsoleHeaderParser
{
    /// <summary>
    /// First 4 bytes of the 156-byte Nintendo logo the BIOS verifies. Byte-identical to the NDS logo -
    /// only the offset differs (0x04 here, 0xC0 on NDS), which is what <see cref="ConsoleDetector"/>'s
    /// ordering turns on.
    /// </summary>
    private static ReadOnlySpan<byte> NintendoLogo => [0x24, 0xFF, 0xAE, 0x51];

    private const int LogoOffset = 0x04;
    private const int TitleOffset = 0xA0;
    private const int GameCodeOffset = 0xAC;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        if (!header.Match(LogoOffset, NintendoLogo))
        {
            return null;
        }

        // Deliberately not requiring header[0xB2] == 0x96. It is a genuine fixed value and would be free
        // extra confidence, but homebrew that skipped gbafix omits it, and the logo match at 0x04 measured
        // 99.42% over 2,925 real files with no misdetections. Tightening this can only
        // lose true positives; the false-positive risk is handled by ordering, not by this byte.
        return new HeaderDetection
        {
            ConsoleType = ConsoleType.GameBoyAdvance,
            Title = header.ReadAscii(TitleOffset, 12),

            // The 4th character is a region letter from the same alphabet as NDS, but RegionId is left
            // unset: it is documented as the NDS/DSi field and GBA art comes from libretro by name, not
            // from GameTDB by region. Callers that want it can read Serial[3].
            Serial = header.ReadGameCode(GameCodeOffset, 4),
        };
    }
}
