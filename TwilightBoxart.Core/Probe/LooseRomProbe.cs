using System.IO.Hashing;

namespace TwilightBoxart.Core.Probe;

/// <summary>
/// Probes a bare ROM file. There is no container header to mine here, so the cheap read is the ROM's
/// own leading bytes - 512 covers every header this project parses, and that alone identifies
/// 97.8% of DS and 87.7% of GBA files by title id.
///
/// A CRC32 costs a full sequential read of the file, so it is taken only when the file is small
/// enough for that to be genuinely cheap. Consoles with no usable serial - NES, SNES, N64, Genesis -
/// have nothing else to match on, so the read buys a great deal when it is affordable.
/// </summary>
/// <param name="crcByteBudget">
/// Largest file this probe will read end-to-end for a CRC32. Anything above it returns
/// <see cref="ProbeResult.Crc32"/> as null and leaves hashing to a caller that has decided it is
/// worth the I/O. Set to 0 to never hash.
/// </param>
public sealed class LooseRomProbe(long crcByteBudget = LooseRomProbe.DefaultCrcByteBudget) : IRomProbe
{
    /// <summary>
    /// 64 MiB covers every Game Boy, GBC, GBA, NES, SNES, Mega Drive and Game Gear ROM outright, and
    /// all but the largest DS carts - a sequential read of that size is a few tens of milliseconds
    /// off any modern disk. Above it the odds shift: those are DS/N64 dumps, which carry a header
    /// serial anyway and never need the hash.
    /// </summary>
    public const long DefaultCrcByteBudget = 64L * 1024 * 1024;

    /// <summary>Read buffer for the CRC pass. Large enough that the syscall count stays negligible.</summary>
    private const int HashBufferBytes = 128 * 1024;

    public bool CanHandle(string path) => SupportedFiles.IsRom(path);

    public async Task<ProbeResult?> ProbeAsync(
        Stream stream, string path, bool wantHeader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The probe reads the head of the file and may rewind to hash it.", nameof(stream));
        }

        var size = stream.Length;
        if (size <= 0)
        {
            return null;
        }

        // The header is read unconditionally: 512 bytes is the cheapest read there is, and skipping
        // it would save nothing measurable while costing every serial-based match.
        stream.Seek(0, SeekOrigin.Begin);
        var header = new byte[(int)Math.Min(IRomProbe.HeaderBytesWanted, size)];
        var headerRead = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (headerRead < header.Length)
        {
            header = header[..headerRead];
        }

        var bytesRead = (long)headerRead;
        uint? crc32 = null;

        if (size <= crcByteBudget)
        {
            crc32 = await ComputeCrc32Async(stream, header, ct);
            bytesRead = size;
        }

        return new ProbeResult
        {
            InnerName = Path.GetFileName(path),
            UncompressedSize = size,
            Crc32 = crc32,
            Header = wantHeader ? header : null,
            Container = ContainerKind.Loose,
            BytesRead = bytesRead,
        };
    }

    /// <summary>
    /// CRC32 over the whole file, reusing the bytes already in hand so the head of the file is not
    /// read twice. Unlike the 7z case a computed 0 is a real digest, so it is returned as-is.
    /// </summary>
    private static async Task<uint> ComputeCrc32Async(Stream stream, byte[] header, CancellationToken ct)
    {
        var crc = new Crc32();
        crc.Append(header);

        stream.Seek(header.Length, SeekOrigin.Begin);
        var buffer = new byte[HashBufferBytes];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }

            crc.Append(buffer.AsSpan(0, read));
        }

        return crc.GetCurrentHashAsUInt32();
    }
}
