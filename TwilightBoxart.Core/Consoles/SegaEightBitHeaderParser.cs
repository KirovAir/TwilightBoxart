using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Master System and Game Gear, via the "TMR SEGA" header. The two share a header format and are told
/// apart by its region nibble.
/// </summary>
/// <remarks>
/// <b>Buffer limitation.</b> The header lives at 0x7FF0, 0x3FF0 or 0x1FF0, so this parser needs at
/// least 8 KiB to detect anything and 32 KiB to check the canonical location. The 512-byte window the
/// clients send is not enough and this parser will simply return null for it - deliberately, rather
/// than guessing. The header is also optional on early Japanese carts, which carry none at all; those
/// are index lookups, not header detections.
/// </remarks>
public sealed class SegaEightBitHeaderParser : IConsoleHeaderParser
{
    private static ReadOnlySpan<byte> TmrSega => [0x54, 0x4D, 0x52, 0x20, 0x53, 0x45, 0x47, 0x41]; // "TMR SEGA"

    /// <summary>
    /// Canonical location first. A cart large enough for 0x7FF0 has its real header there; the smaller
    /// offsets exist for 8 KiB and 16 KiB ROMs, and checking them first would let arbitrary ROM data at
    /// 0x1FF0 win over a genuine header further in.
    /// </summary>
    private static ReadOnlySpan<int> HeaderOffsets => [0x7FF0, 0x3FF0, 0x1FF0];

    private const int ProductCodeOffset = 0x0C;
    private const int VersionOffset = 0x0E;
    private const int RegionOffset = 0x0F;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        var found = -1;
        foreach (var offset in HeaderOffsets)
        {
            if (header.Match(offset, TmrSega))
            {
                found = offset;
                break;
            }
        }

        if (found < 0)
        {
            return null;
        }

        // High nibble of 0x0F: 3 = SMS Japan, 4 = SMS Export, 5 = GG Japan, 6 = GG Export,
        // 7 = GG International. Low nibble is the ROM size, which we do not need.
        var region = header.ByteAt(found + RegionOffset);
        var console = (region >> 4) switch
        {
            5 or 6 or 7 => ConsoleType.GameGear,

            // 3 and 4 are Master System; so is anything unrecognised. The magic already established this
            // is a Sega 8-bit ROM, and Master System is by far the larger set, so an unknown region code
            // is better served by the more likely platform than by dropping the detection entirely.
            _ => ConsoleType.MasterSystem,
        };

        return new HeaderDetection
        {
            ConsoleType = console,
            Serial = ReadProductCode(header, found),
        };
    }

    /// <summary>
    /// Reads the 5-digit BCD product code spanning 0x0C-0x0E (the high nibble of 0x0E is its most
    /// significant digit; the low nibble is the version).
    /// </summary>
    /// <remarks>
    /// Surfaced because it is free, but note it is not a lookup key: Master System and Game Gear both
    /// sit at 0% serial coverage in the DAT, so these platforms identify by hash or
    /// by name. Treat this as diagnostic.
    /// </remarks>
    private static string? ReadProductCode(ReadOnlySpan<byte> header, int headerOffset)
    {
        var low = header.ByteAt(headerOffset + ProductCodeOffset);
        var high = header.ByteAt(headerOffset + ProductCodeOffset + 1);
        var top = header.ByteAt(headerOffset + VersionOffset);

        if (low < 0 || high < 0 || top < 0)
        {
            return null;
        }

        // Each byte holds two BCD digits, so hex formatting prints them directly.
        return $"{top >> 4:X1}{high:X2}{low:X2}";
    }
}
