using System.Globalization;
using TwilightBoxart.Core.Index;
using TwilightBoxart.Web.Models;

namespace TwilightBoxart.Web.Services;

/// <summary>
/// The source of a fresh index. A seam rather than a direct <see cref="IndexBuilder"/> call so the
/// tests can exercise the build lifecycle without downloading DAT files from a volunteer-run mirror.
/// </summary>
public interface IIndexSource
{
    /// <summary>Builds a complete index at <paramref name="outputPath"/> and describes it.</summary>
    Task<BuiltIndex> BuildAsync(string outputPath, Action<string> log, CancellationToken ct);
}

public sealed record BuiltIndex(string Version, int RowCount);

/// <summary>
/// The real source: downloads the No-Intro / libretro DAT files and runs the index builder in-process.
/// DAT downloads are cached under the data volume, so a rebuild re-fetches only what changed and a
/// failed run costs the mirror nothing next time.
/// </summary>
public sealed class DatIndexSource(TwilightSettings settings) : IIndexSource
{
    public async Task<BuiltIndex> BuildAsync(string outputPath, Action<string> log, CancellationToken ct)
    {
        var options = new BuildOptions
        {
            OutputPath = outputPath,
            CacheDirectory = Path.Combine(settings.DataPath, "dat-cache"),
            Version = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };

        var result = await new IndexBuilder(options, log).RunAsync(ct);
        return new BuiltIndex(result.Version, result.RowCount);
    }
}

/// <summary>What the admin panel sees of the build lifecycle.</summary>
public sealed record IndexBuildStatus(
    string State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Version,
    int? Rows,
    string? Error,
    IReadOnlyList<string> Log);

/// <summary>
/// Builds the index without a deployment step: once on first boot when the file is absent, and
/// again whenever the admin panel asks. This replaces the old CI pipeline (monthly workflow,
/// release assets, manifest) with the server simply making its own reference data - the index is
/// derived entirely from public DAT files, so there is nothing about it worth shipping.
/// </summary>
/// <remarks>
/// One build at a time, in the background: the server keeps serving throughout, degraded exactly as
/// it already is whenever the index is missing (serial-bearing platforms never need it). The build
/// lands in a temp file and is swapped in atomically via <see cref="ReloadableMetadataIndex"/>, so
/// a failed or cancelled build can never leave a half-written database in place.
/// </remarks>
public sealed class IndexBuildService(
    IIndexSource source,
    ReloadableMetadataIndex index,
    TwilightSettings settings,
    IHostEnvironment environment,
    ILogger<IndexBuildService> logger) : IHostedService
{
    private readonly Lock _gate = new();
    private readonly List<string> _log = [];
    private readonly CancellationTokenSource _stopping = new();
    private Task? _running;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;
    private string? _version;
    private int? _rows;
    private string? _error;

    /// <summary>Kicks a rebuild unless one is already running. Returns false when it was.</summary>
    public bool TryStartRebuild()
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                return false;
            }

            _startedAt = DateTimeOffset.UtcNow;
            _finishedAt = null;
            _error = null;
            _log.Clear();
            _running = Task.Run(() => RunAsync(_stopping.Token));
            return true;
        }
    }

    public IndexBuildStatus Status
    {
        get
        {
            lock (_gate)
            {
                var state = _running is { IsCompleted: false } ? "running"
                    : _error is not null ? "failed"
                    : _finishedAt is not null ? "succeeded"
                    : "idle";
                return new IndexBuildStatus(
                    state, _startedAt, _finishedAt, _version, _rows, _error, [.. _log.TakeLast(20)]);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var buildPath = settings.IndexPath + ".building";
        try
        {
            var built = await source.BuildAsync(buildPath, Append, ct);
            index.Swap(buildPath);

            lock (_gate)
            {
                _version = built.Version;
                _rows = built.RowCount;
                _finishedAt = DateTimeOffset.UtcNow;
            }

            logger.LogInformation("Index build finished: version {Version}, {Rows} rows",
                built.Version, built.RowCount);
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException && ct.IsCancellationRequested;
            lock (_gate)
            {
                _error = cancelled ? "cancelled by shutdown" : ex.Message;
                _finishedAt = DateTimeOffset.UtcNow;
                // A failed build must not keep advertising the previous success's numbers.
                _version = null;
                _rows = null;
            }

            if (cancelled)
            {
                logger.LogInformation("Index build cancelled by shutdown");
            }
            else
            {
                logger.LogError(ex, "Index build failed");
            }

            try
            {
                File.Delete(buildPath);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // A leftover .building file is inert; the next attempt overwrites it.
            }
        }
    }

    private void Append(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_gate)
        {
            _log.Add(line.Trim());
            if (_log.Count > 200)
            {
                _log.RemoveRange(0, _log.Count - 200);
            }
        }
    }

    /// <summary>First boot: no index file means build one, in the background, right now.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Never in the test host: the suite must not touch the network, and the admin tests drive
        // the build lifecycle explicitly through a fake source. Keyed on the environment rather than
        // on a config flag, so "do not build on startup" cannot be set on a real deployment - where
        // it would leave a fresh install permanently indexless and looking broken.
        if (!environment.IsEnvironment("Testing") && !File.Exists(settings.IndexPath))
        {
            logger.LogInformation("No index at {Path}; building one now", settings.IndexPath);
            TryStartRebuild();
        }

        return Task.CompletedTask;
    }

    /// <summary>Cancels a build in flight and waits for it to wind down, up to the host's deadline.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_running is { } running)
        {
            // RunAsync never throws; the race is only against a build slow to observe cancellation.
            await Task.WhenAny(running, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }
}
