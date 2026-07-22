using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Nintendo 64, via the byte-order magic at offset 0. Reports which of the three byte orders the dump
/// is in, and reads the title and cartridge id through that byte order.
/// </summary>
public sealed class Nintendo64HeaderParser : IConsoleHeaderParser
{
    private static ReadOnlySpan<byte> BigEndianMagic => [0x80, 0x37, 0x12, 0x40];    // .z64
    private static ReadOnlySpan<byte> ByteSwappedMagic => [0x37, 0x80, 0x40, 0x12];  // .v64
    private static ReadOnlySpan<byte> LittleEndianMagic => [0x40, 0x12, 0x37, 0x80]; // .n64

    /// <summary>Logical offset of the 20-byte internal name.</summary>
    private const int TitleOffset = 0x20;

    /// <summary>Logical offset of the 4-byte cartridge id: media type, two-char code, region.</summary>
    private const int GameCodeOffset = 0x3B;

    /// <summary>Enough of the header to cover the cartridge id at 0x3B-0x3E.</summary>
    private const int HeaderLength = 0x40;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        var order = header switch
        {
            _ when header.Match(0, BigEndianMagic) => N64ByteOrder.BigEndian,
            _ when header.Match(0, ByteSwappedMagic) => N64ByteOrder.ByteSwapped,
            _ when header.Match(0, LittleEndianMagic) => N64ByteOrder.LittleEndian,
            _ => N64ByteOrder.None,
        };

        if (order == N64ByteOrder.None)
        {
            return null;
        }

        // Descramble the header into big-endian order before reading any text out of it. A .v64's title
        // reads as "US EPRAM RIOB" without this - byte pairs transposed - which would poison a name
        // match rather than merely miss it.
        Span<byte> linear = stackalloc byte[HeaderLength];
        for (var i = 0; i < HeaderLength; i++)
        {
            var b = ReadLogical(header, order, i);
            linear[i] = b < 0 ? (byte)0 : (byte)b;
        }

        ReadOnlySpan<byte> descrambled = linear;

        return new HeaderDetection
        {
            ConsoleType = ConsoleType.Nintendo64,
            ByteOrder = order,
            Title = descrambled.ReadAscii(TitleOffset, 20),
            Serial = descrambled.ReadGameCode(GameCodeOffset, 4),
        };
    }

    /// <summary>
    /// Maps a logical (big-endian) offset to its physical index under <paramref name="order"/>, and
    /// returns that byte - or -1 when it is past the end of the buffer.
    /// </summary>
    private static int ReadLogical(ReadOnlySpan<byte> header, N64ByteOrder order, int logicalOffset)
    {
        var physical = order switch
        {
            N64ByteOrder.BigEndian => logicalOffset,

            // 16-bit swap: adjacent bytes transposed, so flipping the low bit of the index undoes it.
            N64ByteOrder.ByteSwapped => logicalOffset ^ 1,

            // 32-bit swap: each aligned group of 4 reversed.
            N64ByteOrder.LittleEndian => (logicalOffset & ~3) | (3 - (logicalOffset & 3)),

            _ => -1,
        };

        return header.ByteAt(physical);
    }
}
