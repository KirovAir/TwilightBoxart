using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Game Boy and Game Boy Color. Recognised by the Nintendo logo at 0x104, or by the standard
/// entrypoint at 0x100 for the unlicensed carts that have no logo.
/// </summary>
public sealed class GameBoyHeaderParser : IConsoleHeaderParser
{
    /// <summary>First 4 bytes of the 48-byte Nintendo logo the boot ROM verifies.</summary>
    private static ReadOnlySpan<byte> NintendoLogo => [0xCE, 0xED, 0x66, 0x66];

    /// <summary>
    /// <c>nop; jp $0150</c> - what essentially every toolchain emits at the 0x100 entrypoint.
    /// </summary>
    /// <remarks>
    /// Kept because it is the only thing that catches the Sachen-style unlicensed multicarts, which
    /// ship no Nintendo logo at all. It is a weak 4-byte test on arbitrary code, which is why every
    /// console with a real magic number is checked before this parser runs.
    /// </remarks>
    private static ReadOnlySpan<byte> EntryPoint => [0x00, 0xC3, 0x50, 0x01];

    private const int EntryPointOffset = 0x100;
    private const int LogoOffset = 0x104;
    private const int TitleOffset = 0x134;
    private const int ManufacturerOffset = 0x13F;
    private const int CgbFlagOffset = 0x143;

    /// <summary>Bytes 0x134-0x13E: the title field once a manufacturer code exists after it.</summary>
    private const int ShortTitleLength = 11;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        if (!header.Match(LogoOffset, NintendoLogo) && !header.Match(EntryPointOffset, EntryPoint))
        {
            return null;
        }

        var cgbFlag = header.ByteAt(CgbFlagOffset);

        // 0x80 = runs on DMG and CGB, 0xC0 = CGB only. Any other value is not a flag at all - it is the
        // 16th character of an old DMG-style title.
        //
        // Note this test is sound in one direction only, and that asymmetry is the whole story here.
        // 0x80 and 0xC0 are not printable ASCII, so a byte that IS 0x80/0xC0 cannot be a title character
        // and the CGB determination is safe. The reverse does not hold: a Color cart that simply never
        // set the flag is indistinguishable from a DMG cart by header alone. That is exactly what
        // "Action Replay Online (Europe)" and "GameShark Online (USA)" are - both titled
        // "Action Replay V4", so 0x143 is 0x34, the ASCII '4'. No header-only rule can recover those two;
        // AlternateConsoleType below is how they get a second chance against the index.
        var isCgb = cgbFlag is 0x80 or 0xC0;

        // Title field length, and the ambiguity the 2020 code got wrong in both directions.
        //
        // The field shrank twice over the platform's life: 16 bytes (0x134-0x143) on early DMG carts,
        // then 15 (0x134-0x142) once 0x143 became the CGB flag, then 11 (0x134-0x13E) once 0x13F-0x142
        // became a manufacturer code. Nothing in the header says which layout a cart uses. The 2020 code
        // read 16 for GB - swallowing the CGB flag into the title - and keyed the 11-vs-15 split on the
        // flag being 0xC0 rather than 0x80, which is unrelated to whether a manufacturer code is present.
        //
        // ReadAscii stops at the first non-printable byte, so a NUL-padded title self-terminates and the
        // length passed here is only an upper bound. That leaves one genuinely undecidable case: a full
        // 11-character title with no NUL, followed by a manufacturer code. We resolve it toward a correct
        // title (accepting a 15-char title that has absorbed the code) and emit no serial, because GB and
        // GBC serial coverage in the index is 0.1% and 3.1% - the title is what identification actually
        // runs on, and a truncated title would cost far more than a missing serial.
        var titleLength = isCgb ? 15 : 16;

        string? serial = null;
        if (isCgb && header.ContainsNul(TitleOffset, ShortTitleLength)
                  && header.IsUpperAlphanumeric(ManufacturerOffset, 4))
        {
            // The title provably ended inside the 11-byte field, so 0x13F-0x142 is not title text, and
            // it has the shape of a manufacturer code. Only now is claiming it as a serial safe.
            serial = header.ReadAscii(ManufacturerOffset, 4);
        }

        var (console, alternate) = cgbFlag switch
        {
            // CGB only. Unambiguous.
            0xC0 => (ConsoleType.GameBoyColor, ConsoleType.Unknown),

            // Dual DMG/CGB. The flag describes the silicon, not which No-Intro set the cart is filed in;
            // 7 carts filed under Game Boy in the corpus have 0x80 and were sent to the wrong art DB.
            // GBC first because that is empirically right for the overwhelming majority.
            0x80 => (ConsoleType.GameBoyColor, ConsoleType.GameBoy),

            // No flag. Almost always a real DMG cart, but see the Action Replay case above.
            _ => (ConsoleType.GameBoy, ConsoleType.GameBoyColor),
        };

        return new HeaderDetection
        {
            ConsoleType = console,
            AlternateConsoleType = alternate,
            Title = header.ReadAscii(TitleOffset, titleLength),
            Serial = serial,
        };
    }
}
