using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TwilightBoxart.Core.Probe;

/// <summary>
/// Reads a ROM's inner name, uncompressed size and CRC32 straight out of a zip's central directory,
/// decompressing nothing. One 4 KiB tail read covers 100% of the measured corpus - 116 bytes minimum,
/// 140 median, 508 maximum actually consumed.
///
/// Hand-rolled rather than delegated to a library on purpose. The parse is short, it removes a
/// dependency from the hot path, and - decisively - every stream-based zip reader measured pulls a
/// full 64 KiB buffer whether it needs it or not, which would turn <see cref="ProbeResult.BytesRead"/>
/// into a fiction. ZIP64 sentinel values and the oversized-comment EOCD rescan are both handled.
/// </summary>
public sealed class ZipRomProbe : IRomProbe
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint LocalHeaderSignature = 0x04034B50;

    private const int EndOfCentralDirectorySize = 22;
    private const int CentralHeaderSize = 46;
    private const int LocalHeaderSize = 30;

    private const ushort MethodStored = 0;
    private const ushort MethodDeflate = 8;

    private const ushort FlagEncrypted = 0x0001;

    /// <summary>One slice off the end of the file. Sized to cover the entire measured corpus in a single read.</summary>
    private const int TailWindow = 4096;

    /// <summary>A zip comment may be 65,535 bytes, so this is the largest window an EOCD can legally hide in.</summary>
    private const int MaxEndOfCentralDirectoryWindow = 65535 + EndOfCentralDirectorySize;

    /// <summary>
    /// Compressed prefix fed to the inflater when the header is wanted. Deflate never expands, so
    /// 4 KiB in guarantees at least ~4 KiB out - eight times the 512 bytes we need.
    /// </summary>
    private const int DeflatePrefixBytes = 4096;

    /// <summary>
    /// Refuse to allocate for a central directory larger than this. A 42,000-entry No-Intro set is
    /// under 4 MB, so anything past 64 MB is a corrupt or hostile header rather than a real archive.
    /// </summary>
    private const int MaxCentralDirectoryBytes = 64 * 1024 * 1024;

    public bool CanHandle(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    public async Task<ProbeResult?> ProbeAsync(
        Stream stream, string path, bool wantHeader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("A zip is read from its tail inwards, so the stream must be seekable.", nameof(stream));
        }

        var reader = new RangeReader(stream);
        var entries = await ReadCentralDirectoryAsync(reader, ct);
        if (entries is null)
        {
            return null;
        }

        var candidates = new ArchiveEntryCandidate[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            candidates[i] = new ArchiveEntryCandidate(i, entries[i].Name, entries[i].UncompressedSize);
        }

        if (ArchiveEntrySelector.Select(candidates) is not { } pick)
        {
            // No member looks like a ROM. Return a miss - the 2020 client instead fell out of its
            // `using` block and handed the now-disposed FileStream to the loose-file path, which is
            // what broke 1,908 zips.
            return null;
        }

        var entry = entries[pick.Index];
        var header = wantHeader ? await ReadEntryHeaderAsync(reader, entry, ct) : null;

        return new ProbeResult
        {
            // Always the inner entry name, never the archive's - the 2020 client sent the archive
            // name and turned every match into an HTTP 500.
            InnerName = ArchiveEntrySelector.LeafName(entry.Name),
            UncompressedSize = entry.UncompressedSize,
            Crc32 = entry.Crc32,
            Header = header,
            Container = ContainerKind.Zip,
            BytesRead = reader.BytesRead,
        };
    }

    private static async Task<List<CentralEntry>?> ReadCentralDirectoryAsync(RangeReader reader, CancellationToken ct)
    {
        var fileLength = reader.Length;
        if (fileLength < EndOfCentralDirectorySize)
        {
            return null;
        }

        var tailLength = (int)Math.Min(TailWindow, fileLength);
        var tailStart = fileLength - tailLength;
        var tail = await reader.ReadAsync(tailStart, tailLength, ct);
        var eocd = FindLastSignature(tail, EndOfCentralDirectorySignature);

        if (eocd < 0)
        {
            // The zip comment ran past our tail window; rescan the largest window an EOCD can legally
            // occupy before giving up.
            tailLength = (int)Math.Min(MaxEndOfCentralDirectoryWindow, fileLength);
            tailStart = fileLength - tailLength;
            tail = await reader.ReadAsync(tailStart, tailLength, ct);
            eocd = FindLastSignature(tail, EndOfCentralDirectorySignature);
            if (eocd < 0)
            {
                return null;
            }
        }

        // FindLastSignature only guarantees 4 bytes; the reads below need 20. A file whose last
        // 4-19 bytes happen to contain "PK" - a download truncated just past the EOCD
        // signature is the realistic case - would otherwise throw out of a probe that documents
        // itself as tolerant of truncation.
        if (eocd > tail.Length - EndOfCentralDirectorySize)
        {
            return null;
        }

        var eocdSpan = tail.AsSpan(eocd);
        long entryCount = BinaryPrimitives.ReadUInt16LittleEndian(eocdSpan[10..]);
        long directorySize = BinaryPrimitives.ReadUInt32LittleEndian(eocdSpan[12..]);
        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocdSpan[16..]);

        if (entryCount == 0xFFFF || directorySize == 0xFFFFFFFF || directoryOffset == 0xFFFFFFFF)
        {
            var zip64 = await ReadZip64EndOfCentralDirectoryAsync(reader, tail, eocd, ct);
            if (zip64 is not { } record)
            {
                return null;
            }

            (entryCount, directorySize, directoryOffset) = record;
        }

        if (directorySize is <= 0 or > MaxCentralDirectoryBytes ||
            directoryOffset < 0 || directoryOffset + directorySize > fileLength)
        {
            return null;
        }

        // The central directory is normally already inside the tail slice we hold, so the common case
        // costs no second read at all.
        byte[] directory;
        int directoryStart;
        if (directoryOffset >= tailStart && directoryOffset + directorySize <= tailStart + tail.Length)
        {
            directory = tail;
            directoryStart = (int)(directoryOffset - tailStart);
        }
        else
        {
            directory = await reader.ReadAsync(directoryOffset, (int)directorySize, ct);
            directoryStart = 0;
        }

        var entries = new List<CentralEntry>();
        var position = directoryStart;
        var end = directoryStart + (int)directorySize;

        for (long i = 0; i < entryCount && position + CentralHeaderSize <= end; i++)
        {
            var record = directory.AsSpan(position, end - position);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != CentralHeaderSignature)
            {
                break;
            }

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var crc32 = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            long compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            long uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[24..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(record[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[32..]);
            long localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[42..]);

            var total = CentralHeaderSize + nameLength + extraLength + commentLength;
            if (position + total > end)
            {
                break;
            }

            var name = DecodeName(record.Slice(CentralHeaderSize, nameLength));
            var extra = record.Slice(CentralHeaderSize + nameLength, extraLength);
            ApplyZip64Extra(extra, ref uncompressedSize, ref compressedSize, ref localHeaderOffset);

            entries.Add(new CentralEntry(
                name, crc32, compressedSize, uncompressedSize, method, flags, localHeaderOffset));

            position += total;
        }

        return entries.Count == 0 ? null : entries;
    }

    private static async Task<(long Entries, long Size, long Offset)?> ReadZip64EndOfCentralDirectoryAsync(
        RangeReader reader, byte[] tail, int eocd, CancellationToken ct)
    {
        var locator = FindLastSignature(tail.AsSpan(0, eocd), Zip64LocatorSignature);
        if (locator < 0)
        {
            return null;
        }

        var recordOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(locator + 8));
        if (recordOffset < 0 || recordOffset + 56 > reader.Length)
        {
            return null;
        }

        var record = await reader.ReadAsync(recordOffset, 56, ct);
        if (record.Length < 56)
        {
            return null;
        }

        return (
            (long)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32)),
            (long)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(40)),
            (long)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(48)));
    }

    /// <summary>
    /// Replaces any 0xFFFFFFFF sentinel with the real 64-bit value from the ZIP64 extended
    /// information extra field (header id 0x0001). The fields appear in a fixed order and only the
    /// ones that were sentinels are present, so they must be consumed in that order.
    /// </summary>
    private static void ApplyZip64Extra(
        ReadOnlySpan<byte> extra, ref long uncompressedSize, ref long compressedSize, ref long localHeaderOffset)
    {
        if (uncompressedSize != 0xFFFFFFFF && compressedSize != 0xFFFFFFFF && localHeaderOffset != 0xFFFFFFFF)
        {
            return;
        }

        for (var position = 0; position + 4 <= extra.Length;)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(extra[position..]);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(position + 2)..]);
            var body = position + 4;
            if (body + size > extra.Length)
            {
                return;
            }

            if (id == 0x0001)
            {
                var cursor = body;
                if (uncompressedSize == 0xFFFFFFFF && cursor + 8 <= body + size)
                {
                    uncompressedSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(extra[cursor..]);
                    cursor += 8;
                }

                if (compressedSize == 0xFFFFFFFF && cursor + 8 <= body + size)
                {
                    compressedSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(extra[cursor..]);
                    cursor += 8;
                }

                if (localHeaderOffset == 0xFFFFFFFF && cursor + 8 <= body + size)
                {
                    localHeaderOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(extra[cursor..]);
                }

                return;
            }

            position = body + size;
        }
    }

    /// <summary>
    /// Pulls the first <see cref="IRomProbe.HeaderBytesWanted"/> bytes of one entry. A stored entry is a plain
    /// read; a deflated one inflates a bounded compressed prefix, which measured at 0.2 ms. Returns
    /// null rather than throwing when the entry is encrypted, uses a method we do not implement, or
    /// the data is malformed - the caller still has a usable CRC32 in that case.
    /// </summary>
    private static async Task<byte[]?> ReadEntryHeaderAsync(RangeReader reader, CentralEntry entry, CancellationToken ct)
    {
        if ((entry.Flags & FlagEncrypted) != 0 || entry.UncompressedSize <= 0)
        {
            return null;
        }

        var local = await reader.ReadAsync(entry.LocalHeaderOffset, LocalHeaderSize, ct);
        if (local.Length < LocalHeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(local) != LocalHeaderSignature)
        {
            return null;
        }

        // The local header's own name and extra lengths may differ from the central directory's, so
        // the data offset has to be derived from the local copy.
        var dataOffset = entry.LocalHeaderOffset + LocalHeaderSize
            + BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(26))
            + BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(28));

        var wanted = (int)Math.Min(IRomProbe.HeaderBytesWanted, entry.UncompressedSize);

        if (entry.Method == MethodStored)
        {
            var stored = await reader.ReadAsync(dataOffset, wanted, ct);
            return stored.Length == wanted ? stored : null;
        }

        if (entry.Method != MethodDeflate || entry.CompressedSize <= 0)
        {
            return null;
        }

        var prefix = await reader.ReadAsync(dataOffset, (int)Math.Min(DeflatePrefixBytes, entry.CompressedSize), ct);
        var output = new byte[wanted];
        var filled = 0;

        try
        {
            await using var compressed = new MemoryStream(prefix, writable: false);
            await using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
            while (filled < wanted)
            {
                var read = await inflater.ReadAsync(output.AsMemory(filled, wanted - filled), ct);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }
        }
        catch (InvalidDataException)
        {
            // Expected: we deliberately hand the inflater a truncated stream. Whatever came out
            // before it noticed is still valid ROM data.
        }

        return filled == wanted ? output : null;
    }

    /// <summary>
    /// General-purpose bit 11 says the name is UTF-8; otherwise the spec says CP437. We decode UTF-8
    /// either way: names in the corpus are pure ASCII (where the two encodings agree), plenty of
    /// real-world tools write UTF-8 without setting the flag, and CP437 is not even a registered
    /// encoding on .NET without an extra provider. Invalid sequences become U+FFFD rather than throwing.
    /// </summary>
    private static string DecodeName(ReadOnlySpan<byte> raw) => Encoding.UTF8.GetString(raw);

    private static int FindLastSignature(ReadOnlySpan<byte> buffer, uint signature)
    {
        for (var i = buffer.Length - 4; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[i..]) == signature)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed record CentralEntry(
        string Name,
        uint Crc32,
        long CompressedSize,
        long UncompressedSize,
        ushort Method,
        ushort Flags,
        long LocalHeaderOffset);

    /// <summary>
    /// Absolute-offset reads over a seekable stream, tallying every byte that actually crossed the
    /// boundary so <see cref="ProbeResult.BytesRead"/> is a measurement rather than an estimate.
    /// </summary>
    private sealed class RangeReader(Stream stream)
    {
        public long BytesRead { get; private set; }

        public long Length => stream.Length;

        public async Task<byte[]> ReadAsync(long offset, int length, CancellationToken ct)
        {
            if (offset < 0)
            {
                length += (int)offset;
                offset = 0;
            }

            length = (int)Math.Min(length, Math.Max(0, stream.Length - offset));
            if (length <= 0)
            {
                return [];
            }

            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[length];
            var read = await stream.ReadAtLeastAsync(buffer, length, throwOnEndOfStream: false, ct);
            BytesRead += read;
            return read == length ? buffer : buffer[..read];
        }
    }
}
