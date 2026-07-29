namespace TwilightBoxart.Core.Probe;

/// <summary>
/// Probes a ROM stored under the DS scene's <c>.lz77</c> convention: <c>Game.lz77.sfc</c> is a
/// <c>Game.sfc</c> whose bytes are Nintendo BIOS LZ77 (LZSS, type 0x10) compressed. It decompresses the
/// ROM and hands the raw bytes to <see cref="LooseRomProbe"/>, so a compressed cart identifies
/// byte-for-byte like its uncompressed twin - the CRC32 of the inflated ROM is exactly the one No-Intro
/// recorded.
/// </summary>
/// <remarks>
/// The format is Nintendo LZ77, NOT DEFLATE. nds-bootstrap decompresses precisely these files on-device
/// before launching their SNES / Mega Drive / Master System / Game Gear / PC Engine cores
/// (<c>hb/arm9/source/main.cpp</c>, <c>LZ77_Decompress</c> in <c>lzss.c</c>), so a cover the scanner
/// produces for one really is a cover the menu can use. The pre-2.0 client carried a commented-out
/// <c>DeflateStream</c> stub for <c>.lz77.</c> that could never have matched these files; this is the
/// real decoder, a direct port of that BIOS routine.
///
/// Unlike the on-device decoder, which trusts its input, this one bounds every read and write: a scanner
/// meets truncated and hostile files and must skip them (return null), never crash a scan.
/// </remarks>
public sealed class Lz77RomProbe(long crcByteBudget = LooseRomProbe.DefaultCrcByteBudget) : IRomProbe
{
    /// <summary>The marker between the game name and its real extension: <c>Game.lz77.sfc</c>.</summary>
    private const string Marker = ".lz77";

    /// <summary>The type byte the stream opens with; the low nibble (1) is the LZ77 method.</summary>
    private const byte Lz77Type = 0x10;

    /// <summary>Type byte, then a 24-bit little-endian decompressed length.</summary>
    private const int HeaderSize = 4;

    /// <summary>
    /// Bare ROM extensions that appear <c>.lz77</c>-compressed on a card - exactly the set nds-bootstrap
    /// decompresses (its <c>hb/arm9/source/main.cpp</c>). Kept here rather than in
    /// <see cref="SupportedFiles"/> because it is this probe's private business: a file the menu launches
    /// but we skip is a lost cover, and one we identify but the menu cannot launch is a wasted lookup.
    /// </summary>
    private static readonly HashSet<string> Compressible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sfc", ".smc", ".gen", ".md", ".sms", ".gg", ".pce"
    };

    /// <summary>
    /// Ceiling on the inflated ROM. The header's 24-bit length already caps it near 16 MiB, and every
    /// console using this convention sits well under that (SNES ~6 MB, Mega Drive up to ~12 MB); this is
    /// the guard against a hostile length field.
    /// </summary>
    private const int MaxDecompressedBytes = 16 * 1024 * 1024;

    /// <summary>Largest compressed file we will read in. LZ77 barely expands, so this comfortably clears any real ROM.</summary>
    private const int MaxCompressedBytes = 24 * 1024 * 1024;

    private readonly LooseRomProbe _inner = new(crcByteBudget);

    public bool CanHandle(string path)
    {
        return TryStripMarker(path, out var bare) && Compressible.Contains(Path.GetExtension(bare));
    }

    public async Task<ProbeResult?> ProbeAsync(
        Stream stream, string path, bool wantHeader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The probe reads the whole compressed ROM, so the stream must be seekable.", nameof(stream));
        }

        if (!TryStripMarker(path, out var bareName))
        {
            return null;
        }

        var size = stream.Length;
        if (size is <= HeaderSize or > MaxCompressedBytes)
        {
            return null;
        }

        stream.Seek(0, SeekOrigin.Begin);
        var compressed = new byte[size];
        var read = await stream.ReadAtLeastAsync(compressed, compressed.Length, false, ct);

        if (!TryDecompress(compressed.AsSpan(0, read), out var rom))
        {
            // Not a well-formed LZ77 stream (or truncated / over the size guard). A scan skips it.
            return null;
        }

        await using var inner = new MemoryStream(rom, false);
        var result = await _inner.ProbeAsync(inner, bareName, wantHeader, ct);
        if (result is null)
        {
            return null;
        }

        return result with
        {
            // The name the launcher looks its art up by is the file ON THE CARD ("Game.lz77.sfc"), not the
            // stripped name we probed the bytes as ("Game.sfc"); the two differ only here, and the cover
            // has to match the former or the menu never finds it. Identification already happened off the
            // decompressed bytes, so the marker in this name costs nothing.
            InnerName = Path.GetFileName(path),
            Container = ContainerKind.Lz77,

            // Report the compressed bytes actually read off disk; the inner probe measured the inflated
            // in-memory stream, which never touched it.
            BytesRead = read
        };
    }

    /// <summary>
    /// Turns <c>dir/Game.lz77.sfc</c> into <c>dir/Game.sfc</c>. The console hint, the inner name and the
    /// header rules all come from the real extension underneath the marker. Exposed for testing.
    /// </summary>
    public static bool TryStripMarker(string path, out string bare)
    {
        bare = path;
        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
        {
            return false;
        }

        var stem = path[..^extension.Length];
        if (!stem.EndsWith(Marker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bare = stem[..^Marker.Length] + extension;
        return true;
    }

    /// <summary>
    /// Decompresses a Nintendo LZ77 (type 0x10) blob. Returns false - never throws - for anything that is
    /// not a well-formed stream unpacking to exactly its declared length within the size guard. A direct
    /// port of nds-bootstrap's <c>LZ77_Decompress</c>, with the bounds checks a file scanner needs and
    /// that an on-device loader can do without. Exposed so the token decoding can be asserted directly.
    /// </summary>
    public static bool TryDecompress(ReadOnlySpan<byte> source, out byte[] rom)
    {
        rom = [];
        if (source.Length < HeaderSize || source[0] != Lz77Type)
        {
            return false;
        }

        var length = source[1] | (source[2] << 8) | (source[3] << 16);
        if (length is <= 0 or > MaxDecompressedBytes)
        {
            return false;
        }

        var destination = new byte[length];
        var written = 0;
        var offset = HeaderSize;

        while (written < length)
        {
            if (offset >= source.Length)
            {
                return false; // input ended before the ROM was whole
            }

            var flags = source[offset++];
            for (var bit = 0; bit < 8 && written < length; bit++, flags <<= 1)
            {
                if ((flags & 0x80) == 0)
                {
                    if (offset >= source.Length)
                    {
                        return false;
                    }

                    destination[written++] = source[offset++];
                    continue;
                }

                if (offset + 1 >= source.Length)
                {
                    return false;
                }

                int high = source[offset++];
                int low = source[offset++];
                var distance = (((high & 0x0F) << 8) | low) + 1;
                var runLength = (high >> 4) + 3;

                if (distance > written || written + runLength > length)
                {
                    return false; // back-reference before the start, or past the declared end
                }

                for (var i = 0; i < runLength; i++)
                {
                    destination[written] = destination[written - distance];
                    written++;
                }
            }
        }

        rom = destination;
        return true;
    }
}
