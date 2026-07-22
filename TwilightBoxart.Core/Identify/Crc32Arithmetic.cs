using System.IO.Hashing;

namespace TwilightBoxart.Core.Identify;

/// <summary>
/// The one piece of CRC-32 algebra the identification ladder needs: recovering the CRC of a file's
/// tail from the CRC of the whole file plus its leading bytes.
///
/// This exists for the NES double lookup. No-Intro and real <c>.nes</c> files disagree about whether
/// the 16-byte iNES header is part of the hashed bytes, and NES is 33% of the index at 0% serial
/// coverage, so a headerless-CRC lookup is worth a lot. The catch is that the
/// only CRC we have came out of the archive's central directory, and we never decompressed the ROM, so
/// there is nothing to re-hash. Recomputing would cost a full LZMA decode of the whole set.
///
/// It does not have to. CRC-32 is affine over GF(2): for a message A||B,
/// <c>crc(A||B) = shift(crc(A), |B|) XOR crc(B)</c>, where <c>shift</c> advances a CRC register past
/// |B| zero bytes. This is exactly zlib's <c>crc32_combine</c> (whose final step is a plain XOR), so
/// it inverts directly to <c>crc(B) = crc(A||B) XOR shift(crc(A), |B|)</c>. A 16-byte header we already
/// hold plus the uncompressed length gives the answer exactly, for free.
/// </summary>
public static class Crc32Arithmetic
{
    // CRC-32/ISO-HDLC in reflected form, i.e. the operator for advancing the register one zero bit.
    private const uint ReflectedPolynomial = 0xEDB88320u;

    private const int Bits = 32;

    /// <summary>
    /// Returns the CRC-32 of the bytes that follow <paramref name="prefix"/>, given the CRC of the
    /// whole stream. Exact, not an approximation.
    /// </summary>
    /// <param name="whole">CRC-32 of <c>prefix || suffix</c>, e.g. from a zip central directory.</param>
    /// <param name="prefix">The leading bytes to remove: the 16-byte iNES header.</param>
    /// <param name="suffixLength">Length in bytes of the part that remains.</param>
    /// <returns>The suffix's CRC-32, or null when the inputs cannot describe a real split.</returns>
    public static uint? TryStripPrefix(uint whole, ReadOnlySpan<byte> prefix, long suffixLength)
    {
        if (prefix.IsEmpty || suffixLength <= 0)
        {
            return null;
        }

        return whole ^ ShiftZeros(Crc32.HashToUInt32(prefix), suffixLength);
    }

    /// <summary>
    /// Advances a CRC register as if <paramref name="lengthBytes"/> zero bytes had been appended,
    /// by repeated squaring of the GF(2) bit-advance operator: O(log n), independent of file size.
    /// </summary>
    internal static uint ShiftZeros(uint crc, long lengthBytes)
    {
        if (lengthBytes <= 0)
        {
            return crc;
        }

        Span<uint> even = stackalloc uint[Bits];
        Span<uint> odd = stackalloc uint[Bits];

        // odd = the operator for a single zero bit.
        odd[0] = ReflectedPolynomial;
        var row = 1u;
        for (var n = 1; n < Bits; n++)
        {
            odd[n] = row;
            row <<= 1;
        }

        Square(even, odd); // 2 bits
        Square(odd, even); // 4 bits

        // From here each Square doubles the operator's span, so the first one in the loop is 8 bits =
        // one zero byte, and lengthBytes is consumed one binary digit per iteration.
        do
        {
            Square(even, odd);
            if ((lengthBytes & 1) != 0)
            {
                crc = Times(even, crc);
            }

            lengthBytes >>= 1;
            if (lengthBytes == 0)
            {
                break;
            }

            Square(odd, even);
            if ((lengthBytes & 1) != 0)
            {
                crc = Times(odd, crc);
            }

            lengthBytes >>= 1;
        }
        while (lengthBytes != 0);

        return crc;
    }

    /// <summary>Composes a GF(2) matrix with itself, giving the operator for twice as many bits.</summary>
    private static void Square(Span<uint> square, ReadOnlySpan<uint> matrix)
    {
        for (var n = 0; n < Bits; n++)
        {
            square[n] = Times(matrix, matrix[n]);
        }
    }

    /// <summary>Multiplies a GF(2) matrix by a vector: XOR the rows the set bits select.</summary>
    private static uint Times(ReadOnlySpan<uint> matrix, uint vector)
    {
        var sum = 0u;
        for (var i = 0; i < Bits && vector != 0; i++, vector >>= 1)
        {
            if ((vector & 1) != 0)
            {
                sum ^= matrix[i];
            }
        }

        return sum;
    }
}
