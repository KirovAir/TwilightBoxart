using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TwilightBoxart.Core;
using TwilightBoxart.Core.Identify;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;

namespace TwilightBoxart.Desktop.Services;

/// <summary>The one seam between the two modes: given a probed ROM, turn it into a rendered PNG.</summary>
public interface IArtBackend : IDisposable
{
    /// <summary>Short human label for the log, e.g. "Local index" or "Backend at http://...".</summary>
    string Describe { get; }

    Task<IReadOnlyList<RomIdentity>> IdentifyAsync(
        IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct);

    /// <summary>The rendered PNG for an identity, or null when no source has art for it.</summary>
    Task<byte[]?> GetArtAsync(RomIdentity identity, RenderOptions options, CancellationToken ct);

    /// <summary>
    /// File extensions this scan should open. Asked once per run, so a desktop build that predates a
    /// newly added console still finds its ROMs.
    /// </summary>
    /// <remarks>
    /// Never throws and never returns empty: an implementation that cannot answer returns
    /// <see cref="SupportedFiles.Scannable"/>, the set compiled into this build. Being one release
    /// behind is a missing cover; scanning nothing is a broken app.
    /// </remarks>
    Task<IReadOnlySet<string>> GetScannableExtensionsAsync(CancellationToken ct);
}

/// <summary>Identifies against a local index and renders locally; no server required. Owns the index.</summary>
public sealed class LocalArtBackend(
    IMetadataIndex index,
    IRomIdentifier identifier,
    IEnumerable<IArtSource> sources,
    IBoxartRenderer renderer) : IArtBackend
{
    private readonly IReadOnlyList<IArtSource> _sources = [.. sources.OrderBy(s => s.Order)];

    public string Describe => "the local index";

    /// <summary>
    /// Local mode identifies with this build's own code, so this build's own list IS the truth here -
    /// there is no server whose opinion could be newer.
    /// </summary>
    public Task<IReadOnlySet<string>> GetScannableExtensionsAsync(CancellationToken ct) =>
        Task.FromResult(SupportedFiles.Scannable);

    public Task<IReadOnlyList<RomIdentity>> IdentifyAsync(
        IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct) =>
        identifier.IdentifyBatchAsync(fingerprints, ct);

    public async Task<byte[]?> GetArtAsync(RomIdentity identity, RenderOptions options, CancellationToken ct)
    {
        // First source that has art wins; HttpArtSource caps its own concurrency, so this needs no
        // throttle of its own. GameTDB (title id) outranks libretro (name) by Order.
        foreach (var source in _sources)
        {
            if (!source.CanHandle(identity))
            {
                continue;
            }

            var blob = await source.TryFetchAsync(identity, ct);
            if (blob is { Data.Length: > 0 })
            {
                return renderer.Render(blob, options);
            }
        }

        return null;
    }

    public void Dispose() => (index as IDisposable)?.Dispose();
}

/// <summary>
/// Thin client over a TwilightBoxart backend: probes stay local, identity and art come from
/// <c>/v2/identify</c> and <c>/v2/art</c>. Shares the server's cache, which is kinder to GameTDB.
/// </summary>
public sealed class RemoteArtBackend : IArtBackend
{
    // Web defaults (camelCase, case-insensitive) match ASP.NET Core; enums travel as their names, which
    // is how the API serialises ConsoleType and MatchMethod.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public RemoteArtBackend(IHttpClientFactory factory, string baseUrl)
    {
        _client = factory.CreateClient();
        // On the client rather than per request, so identify, art and anything added later all carry
        // it and none of them can forget. See ApiKey for what this does and does not achieve.
        _client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKey.HeaderName, ApiKey.Value);
        _baseUrl = (baseUrl ?? "").TrimEnd('/');
    }

    public string Describe => $"the backend at {_baseUrl}";

    /// <summary>
    /// Asks the server which extensions to scan, falling back to this build's list on any failure.
    /// </summary>
    /// <remarks>
    /// The response is <c>key=csv</c> lines rather than JSON, because the DSi client shares this
    /// endpoint and has no JSON parser. An unrecognised key is skipped, not rejected, so the server
    /// can add keys without breaking a client that predates them - which is the point of serving the
    /// list at all.
    /// </remarks>
    public async Task<IReadOnlySet<string>> GetScannableExtensionsAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _client.GetAsync($"{_baseUrl}/v2/formats", ct);
            if (!response.IsSuccessStatusCode)
            {
                return SupportedFiles.Scannable;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf('=');
                if (separator < 0 || line.AsSpan(0, separator).Trim() is not ("rom" or "archive"))
                {
                    continue;
                }

                foreach (var item in line[(separator + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var extension = item.Trim();
                    if (extension.StartsWith('.'))
                    {
                        extensions.Add(extension);
                    }
                }
            }

            // A server that answered with nothing usable is treated as a server that did not answer.
            return extensions.Count > 0 ? extensions : SupportedFiles.Scannable;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return SupportedFiles.Scannable;
        }
    }

    public async Task<IReadOnlyList<RomIdentity>> IdentifyAsync(
        IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct)
    {
        using var response = await _client.PostAsJsonAsync(
            $"{_baseUrl}/v2/identify", new IdentifyRequest { Items = fingerprints }, Json, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdentifyResponse>(Json, ct);
        return result?.Items ?? [];
    }

    public async Task<byte[]?> GetArtAsync(RomIdentity identity, RenderOptions options, CancellationToken ct)
    {
        // The server owns the URL scheme: ArtPath is followed verbatim, never assembled from parts,
        // so this client keeps working even if the route structure changes underneath it.
        if (identity.ArtPath is null)
        {
            return null;
        }

        var url = $"{_baseUrl}{identity.ArtPath}{options.ToQueryString()}";
        using var response = await _client.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Picks the backend for a run automatically: the server when it answers, the local index when it does not.</summary>
public sealed class BackendFactory(
    IServiceProvider services,
    IndexManager indexManager,
    ILoggerFactory loggerFactory,
    IHttpClientFactory httpFactory)
{
    /// <summary>
    /// The server is used whenever it is reachable; a server that is down simply switches the run to the
    /// local index, with no choice for the user to make. Only the local path needs the index.
    /// </summary>
    public async Task<IArtBackend> CreateAsync(AppSettings settings, Action<string> log, CancellationToken ct)
    {
        var url = settings.BackendUrl?.Trim();

        if (!string.IsNullOrEmpty(url) && await IsReachableAsync(url, ct))
        {
            log($"Using the backend at {url}.");
            return new RemoteArtBackend(httpFactory, url);
        }

        if (!string.IsNullOrEmpty(url))
        {
            log($"Backend at {url} is unavailable; matching against the local index instead.");
        }

        // No backend and no index: build one ourselves, exactly as the server would. On failure the
        // Open below still degrades to a null-object index that matches DS/DSi/GBA by header title id.
        if (!indexManager.Exists)
        {
            await indexManager.BuildAsync(log, ct);
        }

        // LocalArtBackend disposes the index when the run ends.
        var index = SqliteMetadataIndex.Open(
            indexManager.IndexPath, loggerFactory.CreateLogger<SqliteMetadataIndex>());
        var identifier = new IdentificationLadder(
            index, loggerFactory.CreateLogger<IdentificationLadder>());

        return new LocalArtBackend(
            index,
            identifier,
            services.GetRequiredService<IEnumerable<IArtSource>>(),
            services.GetRequiredService<IBoxartRenderer>());
    }

    private async Task<bool> IsReachableAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using var client = httpFactory.CreateClient();
            using var response = await client.GetAsync(
                $"{baseUrl.TrimEnd('/')}/v2/health", HttpCompletionOption.ResponseHeadersRead, linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Any failure to reach health (refused, timed out, DNS) means "down": fall back to local.
            return false;
        }
    }
}
