using TwilightBoxart.Core;
using TwilightBoxart.Core.Index;

namespace TwilightBoxart.Desktop.Services;

/// <summary>
/// Obtains the No-Intro index that Local mode identifies against: downloaded from the backend when
/// one is reachable (it shares the server's copy), built locally from the public DAT data when not.
/// Backend mode never needs it.
/// </summary>
public sealed class IndexManager
{
    public string IndexPath { get; } = Path.Combine(AppPaths.DataDirectory, "nointro.db");

    public bool Exists => File.Exists(IndexPath);

    /// <summary>
    /// Replaces the index with a fresh copy: the backend's when one is named and answers, a local
    /// build otherwise. Both paths are atomic, so a failed refresh leaves the current index
    /// untouched rather than half-replaced.
    /// </summary>
    public async Task<bool> RefreshAsync(
        string? backendUrl, IHttpClientFactory factory, Action<string> log, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(backendUrl) && await DownloadAsync(backendUrl, factory, log, ct))
        {
            return true;
        }

        return await BuildAsync(log, ct);
    }

    /// <summary>Builds the index right here, with the same builder the backend runs in-process; the DAT downloads cache beside it.</summary>
    public async Task<bool> BuildAsync(Action<string> log, CancellationToken ct)
    {
        log("Building the game index from the public No-Intro data..");
        try
        {
            var options = new BuildOptions
            {
                OutputPath = IndexPath,
                CacheDirectory = Path.Combine(AppPaths.DataDirectory, "dat-cache"),
            };

            var result = await new IndexBuilder(options, log).RunAsync(ct);
            log($"Game index built: {result.RowCount:N0} games known.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Header-serial platforms (DS/DSi/GBA) still match with no index at all, so a failed
            // build degrades the scan rather than stopping it.
            log($"Could not build the game index: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads the index from the backend and swaps it into place atomically, so a half-finished
    /// download can never leave a truncated database that identification would then read as complete.
    /// </summary>
    public async Task<bool> DownloadAsync(
        string backendUrl, IHttpClientFactory factory, Action<string> log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            log("No backend URL set, so there is nowhere to download the index from.");
            return false;
        }

        var url = backendUrl.TrimEnd('/') + "/v2/index/nointro.db";
        log($"Downloading index from {url} ...");

        // Same sidecar idiom as DatFetcher: the ETag from the last successful download rides next to
        // the file, so an unchanged index costs a 304 instead of the full ~5 MB body.
        var etagPath = IndexPath + ".etag";

        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(ApiKey.HeaderName, ApiKey.Value);
            if (File.Exists(IndexPath) && File.Exists(etagPath))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(etagPath).Trim());
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                log("Index already up to date.");
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                log($"Index download failed: HTTP {(int)response.StatusCode}.");
                return false;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            // A captive portal or misrouted proxy answers 200 with an HTML page; committing that
            // would replace a working index with garbage. The magic string is cheap and decisive.
            if (bytes.Length < 100 || !bytes.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8))
            {
                log("Index download failed: the response is not a database. Kept the current index.");
                return false;
            }

            await AtomicFile.WriteAsync(IndexPath, bytes, ct);

            var etag = response.Headers.ETag?.ToString();
            if (etag is not null)
            {
                await AtomicFile.WriteAsync(etagPath, System.Text.Encoding.UTF8.GetBytes(etag), ct);
            }
            else
            {
                AtomicFile.TryDelete(etagPath);
            }

            log($"Index saved ({bytes.Length / (1024.0 * 1024.0):0.0} MiB).");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"Index download failed: {ex.Message}");
            return false;
        }
    }
}
