using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Mega Drive / Genesis, via the console name field at 0x100.
/// </summary>
/// <remarks>
/// High value for its cost: 93.3% serial coverage in the DAT, and the whole header fits inside the
/// 512-byte window every client already sends.
/// </remarks>
public sealed class MegaDriveHeaderParser : IConsoleHeaderParser
{
    private const int ConsoleNameOffset = 0x100;
    private const int DomesticTitleOffset = 0x120;
    private const int OverseasTitleOffset = 0x150;
    private const int SerialOffset = 0x180;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        // The 16-byte console name is one of "SEGA MEGA DRIVE ", "SEGA GENESIS    ", "SEGA 32X" and
        // friends - but a handful of real dumps (Pico among them) pad it with a leading space, putting
        // "SEGA" at 0x101. Accepting both costs nothing and is the difference between detecting those
        // carts and not.
        var matched = header.MatchAscii(ConsoleNameOffset, "SEGA")
                      || (header.ByteAt(ConsoleNameOffset) == ' ' && header.MatchAscii(ConsoleNameOffset + 1, "SEGA"));

        if (!matched)
        {
            return null;
        }

        // Prefer the overseas title - the index is No-Intro, which names Mega Drive entries in English.
        // Japan-only carts leave it blank or duplicate the domestic one, hence the fallback.
        var title = header.ReadAscii(OverseasTitleOffset, 48) ?? header.ReadAscii(DomesticTitleOffset, 48);

        // 14 bytes, e.g. "GM 00001051-00". Read with ReadAscii rather than ReadGameCode: the field is
        // not alphanumeric - it carries spaces and a hyphen - and that punctuation is part of how the
        // DAT records it.
        var serial = NormalizeSerial(header.ReadAscii(SerialOffset, 14));

        // The region field at 0x1F0 ("JUE") is deliberately not surfaced as RegionId. Its letters share
        // an alphabet with the NDS region char but not a meaning - 'E' is Europe here and USA there - so
        // exposing it through the same property would invite GameTdbRegion to be run over it.
        return new HeaderDetection
        {
            ConsoleType = ConsoleType.MegaDrive,
            Title = title,
            Serial = serial,
        };
    }

    /// <summary>
    /// Rewrites the raw 0x180 field into the form No-Intro records, so it can be handed to
    /// <c>TryBySerial</c> unchanged.
    /// </summary>
    /// <remarks>
    /// The cartridge field is <c>"GM 00001051-00"</c>: a two-letter device type ("GM" game, "AI"
    /// education, "OS", "BR", ...), a space, then the product code padded out to fill 14 bytes
    /// (<c>"GM MK-1563 -00"</c>). The DAT stores only the product code, unpadded: <c>"00001051-00"</c>,
    /// <c>"MK-1563-00"</c>. Without this the raw field never equals a DAT row and rung 1 of the ladder
    /// misses <i>every</i> Mega Drive cart, despite the platform's 93.3% serial coverage.
    ///
    /// Both rules were checked against the whole libretro No-Intro Mega Drive DAT: no serial in it
    /// contains a space, and the five that begin with "GM" all use "GM-" (a product code that happens
    /// to start that way), never "GM ". So requiring the space before stripping cannot eat a real code.
    /// </remarks>
    private static string? NormalizeSerial(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var text = raw.AsSpan();

        // Two ASCII letters then a space is the device-type prefix, never part of the product code.
        if (text.Length > 3 &&
            char.IsAsciiLetter(text[0]) && char.IsAsciiLetter(text[1]) && text[2] == ' ')
        {
            text = text[3..];
        }

        // Interior padding: the code is space-filled to the field width before the revision suffix.
        Span<char> packed = stackalloc char[text.Length];
        var length = 0;
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                packed[length++] = ch;
            }
        }

        return length == 0 ? null : new string(packed[..length]);
    }
}
