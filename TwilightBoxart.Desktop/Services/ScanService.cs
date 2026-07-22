using System.Globalization;
using Microsoft.Extensions.Logging;
using TwilightBoxart.Core;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;

namespace TwilightBoxart.Desktop.Services;

public sealed record ScanRequest(string RootFolder, string BoxartDir, RenderOptions Render, bool Overwrite, int Concurrency);

public sealed record ScanCounters(int Found, int Identified, int Written, int Skipped, int Missed);

/// <summary>A progress tick: the current counters, and optionally a log line and/or a status change.</summary>
public sealed record ScanUpdate(ScanCounters Counters, string? Log = null, string? Status = null);

/// <summary>
/// The run, mode-agnostic: collect ROMs, probe them locally, hand fingerprints to an
/// <see cref="IArtBackend"/> for identity, fetch and write the art it returns. The whole card is walked
/// with <c>Path.Combine</c> and forward-slash-free paths, so it behaves the same on Windows, macOS and
/// Linux.
/// </summary>
public sealed class ScanService(RomProbeService prober, ILogger<ScanService> logger)
{
    /// <summary>The batch ceiling POST /v2/identify enforces; harmless (and free) for the local backend.</summary>
    private const int IdentifyChunk = 500;

    /// <summary>Where the art goes under a card root. The one spelling of the TWiLightMenu path.</summary>
    public static string BoxartDirectory(string root) => Path.Combine(root, "_nds", "TWiLightMenu", "boxart");

    public async Task RunAsync(
        IArtBackend backend, ScanRequest request, IProgress<ScanUpdate> progress, CancellationToken ct)
    {
        var counters = new Counters();
        var boxartDir = request.BoxartDir;

        progress.Report(new ScanUpdate(counters.Snapshot(),
            Status: "Scanning...", Log: $"Reading art from {backend.Describe}."));

        // 1. Collect. What counts as a ROM comes from the backend so this build finding no covers for
        // a console added after it shipped is a server-side fix, not a re-download. Never fails: see
        // IArtBackend.GetScannableExtensionsAsync.
        var scannable = await backend.GetScannableExtensionsAsync(ct);
        var files = CollectFiles(request.RootFolder, boxartDir, scannable);
        counters.Found = files.Count;
        progress.Report(new ScanUpdate(counters.Snapshot(), Log: $"Found {files.Count:N0} games and archives."));
        if (files.Count == 0)
        {
            progress.Report(new ScanUpdate(counters.Snapshot(), Status: "No games found under that folder."));
            return;
        }

        // 2. Probe. Local reads, so a little more parallelism than the outbound cap pays off.
        var items = new Item[files.Count];
        var probeOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
            CancellationToken = ct,
        };
        await Parallel.ForAsync(0, files.Count, probeOptions, async (i, c) =>
        {
            var path = files[i];
            var item = new Item { FileName = Path.GetFileName(path) };
            try
            {
                item.Probe = await prober.ProbeFileAsync(path, wantHeader: true, c);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An unreadable file is simply counted as missed below.
            }

            if (item.Probe is null)
            {
                counters.IncMissed();
            }

            items[i] = item;
            if (counters.IncProcessed() % 50 == 0)
            {
                progress.Report(new ScanUpdate(counters.Snapshot()));
            }
        });
        progress.Report(new ScanUpdate(counters.Snapshot(), Log: "Identifying..."));

        // 3. Identify, batched, correlated back by tag.
        var probed = items.Where(i => i.Probe is not null).ToList();
        var fingerprints = new List<RomFingerprint>(probed.Count);
        for (var i = 0; i < probed.Count; i++)
        {
            probed[i].Tag = i.ToString(CultureInfo.InvariantCulture);
            fingerprints.Add(ToFingerprint(probed[i]));
        }

        var identities = await IdentifyInChunksAsync(backend, fingerprints, ct);
        var byTag = new Dictionary<string, RomIdentity>(StringComparer.Ordinal);
        foreach (var id in identities)
        {
            if (id.Tag is { } tag)
            {
                byTag[tag] = id;
            }
        }

        foreach (var item in probed)
        {
            if (item.Tag is { } tag && byTag.TryGetValue(tag, out var id) && id.IsMatched)
            {
                item.Identity = id;
                counters.IncIdentified();
            }
            else
            {
                counters.IncMissed();
            }
        }
        progress.Report(new ScanUpdate(counters.Snapshot(), Log: $"Identified {counters.Identified:N0}. Fetching art..."));

        // 4. Fetch and write. Outbound is capped at the requested concurrency to stay a good neighbour.
        var matched = probed.Where(i => i.Identity is not null).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var artOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, request.Concurrency),
            CancellationToken = ct,
        };
        await Parallel.ForEachAsync(matched, artOptions, async (item, c) =>
        {
            var innerIsRom = item.Probe!.InnerName is { Length: > 0 } inner && SupportedFiles.IsRom(inner);
            var outName = SafeName.OutputFileName(item.FileName, item.Probe.InnerName, innerIsRom);
            var outPath = Path.Combine(boxartDir, outName);

            // Two ROMs in different folders can share a name; the art is identical, so fetch once.
            lock (seen)
            {
                if (!seen.Add(outName))
                {
                    counters.IncSkipped();
                    Tick(counters, progress);
                    return;
                }
            }

            if (!request.Overwrite && File.Exists(outPath))
            {
                counters.IncSkipped();
                Tick(counters, progress);
                return;
            }

            byte[]? png;
            try
            {
                png = await backend.GetArtAsync(item.Identity!, request.Render, c);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Art fetch failed for {Name}", outName);
                counters.IncMissed();
                progress.Report(new ScanUpdate(counters.Snapshot(), Log: $"{outName}: {ex.Message}"));
                return;
            }

            if (png is null)
            {
                counters.IncMissed();
                Tick(counters, progress);
                return;
            }

            // A failed write is that file's problem, not the scan's: a card that fills up at cover
            // 200 of 5,000 must still get the other 4,800 attempted (and reported one by one).
            try
            {
                await AtomicFile.WriteAsync(outPath, png, c);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Write failed for {Name}", outName);
                counters.IncMissed();
                progress.Report(new ScanUpdate(counters.Snapshot(), Log: $"{outName}: {ex.Message}"));
                return;
            }

            counters.IncWritten();
            Tick(counters, progress);
        });

        var final = counters.Snapshot();
        progress.Report(new ScanUpdate(final,
            Status: $"Done. {final.Written:N0} written, {final.Skipped:N0} already there, {final.Missed:N0} without art.",
            Log: "Finished."));
    }

    /// <summary>Reports a snapshot every 25th processed item, so the UI updates without a flood of ticks.</summary>
    private static void Tick(Counters counters, IProgress<ScanUpdate> progress)
    {
        if (counters.IncProcessed() % 25 == 0)
        {
            progress.Report(new ScanUpdate(counters.Snapshot()));
        }
    }

    /// <summary>
    /// Walks <paramref name="root"/> for files worth probing, skipping the output folder itself.
    /// <paramref name="scannable"/> is normally the backend's answer; null uses this build's own
    /// list, which is what the tests and any caller with no backend in hand want.
    /// </summary>
    internal static List<string> CollectFiles(
        string root, string boxartDir, IReadOnlySet<string>? scannable = null)
    {
        scannable ??= SupportedFiles.Scannable;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        // Trailing separator, so a sibling like "boxart-old" is not mistaken for the output folder.
        var boxartRoot = Path.TrimEndingDirectorySeparator(boxartDir);
        var boxartPrefix = boxartRoot + Path.DirectorySeparatorChar;

        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            var name = Path.GetFileName(path);

            // AppleDouble resource forks share the ROM's extension but are not ROMs.
            if (name.StartsWith("._", StringComparison.Ordinal))
            {
                continue;
            }

            if (!scannable.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            // Never scan our own output back in.
            if (path.StartsWith(boxartPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, boxartRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(path);
        }

        return result;
    }

    private static RomFingerprint ToFingerprint(Item item)
    {
        var probe = item.Probe!;
        var name = probe.InnerName is { Length: > 0 } inner ? inner : item.FileName;
        return new RomFingerprint
        {
            FileName = name,
            Crc32 = probe.Crc32,
            Header = probe.Header,
            Size = probe.UncompressedSize,
            Tag = item.Tag,
        };
    }

    private static async Task<List<RomIdentity>> IdentifyInChunksAsync(
        IArtBackend backend, List<RomFingerprint> fingerprints, CancellationToken ct)
    {
        var all = new List<RomIdentity>(fingerprints.Count);
        for (var i = 0; i < fingerprints.Count; i += IdentifyChunk)
        {
            var slice = fingerprints.GetRange(i, Math.Min(IdentifyChunk, fingerprints.Count - i));
            all.AddRange(await backend.IdentifyAsync(slice, ct));
        }

        return all;
    }

    private sealed class Item
    {
        public required string FileName { get; init; }
        public ProbeResult? Probe { get; set; }
        public RomIdentity? Identity { get; set; }
        public string? Tag { get; set; }
    }

    private sealed class Counters
    {
        private int _processed;
        private int _identified;
        private int _written;
        private int _skipped;
        private int _missed;

        public int Found { get; set; }

        public int Identified => Volatile.Read(ref _identified);

        /// <summary>Progress cadence only: one deterministic count of delivered items, never displayed.</summary>
        public int IncProcessed() => Interlocked.Increment(ref _processed);

        public void IncIdentified() => Interlocked.Increment(ref _identified);

        public void IncWritten() => Interlocked.Increment(ref _written);

        public void IncSkipped() => Interlocked.Increment(ref _skipped);

        public void IncMissed() => Interlocked.Increment(ref _missed);

        public ScanCounters Snapshot() => new(
            Found,
            Volatile.Read(ref _identified),
            Volatile.Read(ref _written),
            Volatile.Read(ref _skipped),
            Volatile.Read(ref _missed));
    }
}
