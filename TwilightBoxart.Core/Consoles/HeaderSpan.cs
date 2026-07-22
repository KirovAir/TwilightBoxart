using System.Text;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Bounds-safe primitives for reading ROM headers out of a caller-supplied buffer.
/// </summary>
/// <remarks>
/// Every method here returns a miss rather than throwing. Header buffers arrive over the wire from
/// three different clients and may be empty, truncated, or hostile; the 2020 code read fixed offsets
/// behind a single <c>Length &gt;= 328</c> gate and still managed to throw <see cref="IndexOutOfRangeException"/>
/// on an empty title id. Nothing in this namespace is allowed to throw.
/// </remarks>
internal static class HeaderSpan
{
    /// <summary>Returns the byte at <paramref name="offset"/>, or -1 when it is past the end.</summary>
    public static int ByteAt(this ReadOnlySpan<byte> header, int offset) =>
        (uint)offset < (uint)header.Length ? header[offset] : -1;

    /// <summary>True when <paramref name="magic"/> appears at <paramref name="offset"/> in full.</summary>
    public static bool Match(this ReadOnlySpan<byte> header, int offset, ReadOnlySpan<byte> magic)
    {
        // Written as a subtraction against Length so a buffer shorter than the magic goes negative and
        // the non-negative offset can never be <= it. No overflow: offsets here are all small constants.
        if (offset < 0 || magic.Length == 0 || offset > header.Length - magic.Length)
        {
            return false;
        }

        return header.Slice(offset, magic.Length).SequenceEqual(magic);
    }

    /// <summary>True when the ASCII text <paramref name="magic"/> appears at <paramref name="offset"/>.</summary>
    /// <remarks>
    /// The comparison is byte-exact, so it is culture-invariant by construction; no string casing
    /// happens anywhere in this namespace.
    /// </remarks>
    public static bool MatchAscii(this ReadOnlySpan<byte> header, int offset, string magic)
    {
        if (offset < 0 || magic.Length == 0 || offset > header.Length - magic.Length)
        {
            return false;
        }

        for (var i = 0; i < magic.Length; i++)
        {
            if (header[offset + i] != (byte)magic[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads an ASCII field of at most <paramref name="length"/> bytes, stopping at the first
    /// non-printable byte, and trims the padding. Returns null when nothing usable was there.
    /// </summary>
    /// <remarks>
    /// Cartridge title fields are padded with either NUL or spaces depending on the toolchain that
    /// built them, and a truncated buffer may cut a field in half - all three cases have to collapse
    /// to "as much as was actually readable", never an exception and never a string full of control
    /// characters that would later poison a URL or an FTS5 query.
    /// </remarks>
    public static string? ReadAscii(this ReadOnlySpan<byte> header, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= header.Length)
        {
            return null;
        }

        var slice = header.Slice(offset, Math.Min(length, header.Length - offset));

        var end = 0;
        while (end < slice.Length && slice[end] is >= 0x20 and < 0x7F)
        {
            end++;
        }

        var text = Encoding.ASCII.GetString(slice[..end]).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Reads a fixed-width game code (NDS/GBA/SNES "ASME"-style), rejecting anything that is not a
    /// plausible code so junk never reaches an index lookup.
    /// </summary>
    /// <remarks>
    /// '#' is allowed because homebrew conventionally uses "####", and '#' is also the GameTDB homebrew
    /// region character - rejecting it would drop the one region the homebrew art path depends on.
    /// </remarks>
    public static string? ReadGameCode(this ReadOnlySpan<byte> header, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset > header.Length - length)
        {
            return null;
        }

        var slice = header.Slice(offset, length);
        foreach (var b in slice)
        {
            var ok = b is >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z'
                or >= (byte)'0' and <= (byte)'9'
                or (byte)'#';
            if (!ok)
            {
                return null;
            }
        }

        return Encoding.ASCII.GetString(slice);
    }

    /// <summary>True when every byte in the range is an uppercase letter or a digit.</summary>
    public static bool IsUpperAlphanumeric(this ReadOnlySpan<byte> header, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset > header.Length - length)
        {
            return false;
        }

        foreach (var b in header.Slice(offset, length))
        {
            if (b is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the range contains a NUL - i.e. a padded field provably ends inside it. Returns false
    /// when the range runs off the end of the buffer, because then nothing has been proven.
    /// </summary>
    public static bool ContainsNul(this ReadOnlySpan<byte> header, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset > header.Length - length)
        {
            return false;
        }

        return header.Slice(offset, length).IndexOf((byte)0) >= 0;
    }

    /// <summary>Returns the byte at <paramref name="offset"/> as a char, or null when it is not printable.</summary>
    public static char? PrintableCharAt(this ReadOnlySpan<byte> header, int offset)
    {
        var b = header.ByteAt(offset);
        return b is >= 0x20 and < 0x7F ? (char)b : null;
    }
}
