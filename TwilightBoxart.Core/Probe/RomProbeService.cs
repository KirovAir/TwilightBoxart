using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TwilightBoxart.Core.Probe;

/// <summary>
/// Picks the right <see cref="IRomProbe"/> for a file and runs it. Dispatch is by magic number first
/// and extension second, because a ROM library is full of files whose extension lies - a .zip that is
/// really a 7z, a .nds that is really a zip - and the container is cheap to confirm.
///
/// Nothing here throws for a file it cannot read. A scan walks tens of thousands of files and one
/// truncated download must not end it; a malformed container is logged and reported as a miss.
///
/// WIRING: the server identifies from client-supplied fingerprints (POST /v2/identify); this probe's
/// consumer is the desktop client's ScanService, which probes ROMs locally.
/// </summary>
public sealed class RomProbeService : IRomProbe
{
    /// <summary>Enough for both signatures we sniff: 6 bytes of 7z magic, 4 of zip.</summary>
    private const int MagicBytes = 6;

    private static readonly byte[] SevenZipMagic = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] ZipMagic = [0x50, 0x4B];

    private readonly LooseRomProbe _loose;
    private readonly ZipRomProbe _zip = new();
    private readonly SevenZipRomProbe _sevenZip = new();
    private readonly ILogger _logger;

    public RomProbeService(
        ILogger<RomProbeService>? logger = null,
        long looseCrcByteBudget = LooseRomProbe.DefaultCrcByteBudget)
    {
        _logger = logger ?? NullLogger<RomProbeService>.Instance;
        _loose = new LooseRomProbe(looseCrcByteBudget);
    }

    public bool CanHandle(string path) => SupportedFiles.IsScannable(path);

    /// <summary>Opens <paramref name="path"/> for sequential-ish reading and probes it.</summary>
    public async Task<ProbeResult?> ProbeFileAsync(string path, bool wantHeader, CancellationToken ct = default)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 0, FileOptions.Asynchronous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not open {Path} for probing", path);
            return null;
        }

        // One using, one exit. The 2020 client disposed its stream inside the no-match branch and then
        // used it anyway, which is what broke 1,908 zips.
        await using (stream)
        {
            return await ProbeAsync(stream, path, wantHeader, ct);
        }
    }

    public async Task<ProbeResult?> ProbeAsync(
        Stream stream, string path, bool wantHeader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("Probing reads a container's tail, so the stream must be seekable.", nameof(stream));
        }

        var (probe, sniffed) = await SelectProbeAsync(stream, path, ct);
        if (probe is null)
        {
            return null;
        }

        try
        {
            var result = await probe.ProbeAsync(stream, path, wantHeader, ct);

            // The sniff is real I/O and belongs in the tally, or the reported cost of a scan is a
            // lie by exactly the amount we chose not to count.
            return result is null ? null : result with { BytesRead = result.BytesRead + sniffed };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsMalformedContainer(ex))
        {
            // Corrupt or truncated container. One bad file must not end an 18,000-file scan.
            _logger.LogDebug(ex, "Malformed container {Path}; treating as unreadable", path);
            return null;
        }
    }

    /// <summary>
    /// What "this file is not a readable container" looks like coming out of a parser. Deliberately
    /// wide, including <see cref="InvalidOperationException"/>: SharpCompress raises that from deep
    /// inside its 7z header reader on malformed input, and a library scan that aborts on one bad
    /// download is worse than one that skips it. Cancellation is re-thrown before this is consulted.
    /// </summary>
    private static bool IsMalformedContainer(Exception ex) =>
        ex is InvalidDataException or EndOfStreamException or IOException or NotSupportedException
            or InvalidOperationException or FormatException or OverflowException
            or IndexOutOfRangeException or ArgumentException;

    /// <summary>
    /// Chooses a probe, returning it alongside the bytes the sniff cost. Magic wins over extension:
    /// mislabelled archives are common in ROM sets, and the signature is authoritative where the
    /// extension is only a hint.
    /// </summary>
    private async Task<(IRomProbe? Probe, long BytesRead)> SelectProbeAsync(Stream stream, string path, CancellationToken ct)
    {
        var magic = new byte[MagicBytes];
        stream.Seek(0, SeekOrigin.Begin);
        var read = await stream.ReadAtLeastAsync(magic, MagicBytes, throwOnEndOfStream: false, ct);

        if (read >= SevenZipMagic.Length && magic.AsSpan(0, SevenZipMagic.Length).SequenceEqual(SevenZipMagic))
        {
            return (_sevenZip, read);
        }

        if (read >= ZipMagic.Length && magic.AsSpan(0, ZipMagic.Length).SequenceEqual(ZipMagic))
        {
            return (_zip, read);
        }

        // No signature. Fall back to the extension - an archive with leading junk (a self-extracting
        // stub, a partial download) can still be parsed from its tail, and everything else is a bare
        // ROM, which by definition has no container magic of its own.
        if (_sevenZip.CanHandle(path))
        {
            return (_sevenZip, read);
        }

        if (_zip.CanHandle(path))
        {
            return (_zip, read);
        }

        if (_loose.CanHandle(path))
        {
            return (_loose, read);
        }

        _logger.LogDebug("No probe recognises {Path}", path);
        return (null, read);
    }
}
