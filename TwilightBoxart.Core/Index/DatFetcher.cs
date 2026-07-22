using System.IO.Compression;
using System.Net;
using System.Text;

namespace TwilightBoxart.Core.Index;

/// <summary>A DAT's bytes plus where they came from, for the build log.</summary>
public sealed record FetchedDat(string Origin, string Text);

/// <summary>
/// Obtains DAT text, from a local directory or over HTTP, and unwraps whatever container it arrives in.
/// Nothing here is hardcoded to one host: the URL comes from <see cref="DatSource.ResolveUrl"/> so a
/// build can be pointed at a different mirror, an internal cache, or a checked-out copy without a code
/// change.
/// </summary>
public sealed class DatFetcher : IDisposable
{
    // An honest User-Agent so a mirror operator can find us rather than silently block us.
    private static string UserAgent => About.UserAgent;

    private readonly HttpClient _http;
    private readonly string? _cacheDirectory;
    private readonly TimeSpan _retryDelayUnit;

    public DatFetcher(string? cacheDirectory = null, HttpMessageHandler? handler = null, TimeSpan? retryDelayUnit = null)
    {
        _cacheDirectory = cacheDirectory;
        _retryDelayUnit = retryDelayUnit ?? TimeSpan.FromSeconds(2);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        if (_cacheDirectory is not null)
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    /// <summary>
    /// Obtains a DAT, revalidating any cached copy against the mirror. The cache directory is an HTTP
    /// cache, not a snapshot: a fresh 200 replaces the cached DAT, a 304 (via the stored ETag) or an
    /// unreachable mirror serves from it, and only a failure with nothing cached fails the source.
    /// That split is what makes the admin panel's rebuild genuinely pick up a new No-Intro drop, while
    /// a mirror outage still costs a long-lived server nothing but a log line.
    /// Returns null on a 404 with no cached copy, so an optional source can be skipped.
    /// </summary>
    public async Task<FetchedDat?> FetchAsync(DatSource source, string baseUrlTemplate, CancellationToken ct = default)
    {
        var url = source.ResolveUrl(baseUrlTemplate);
        var cachePath = _cacheDirectory is null ? null : Path.Combine(_cacheDirectory, CacheFileName(source.Name));
        var hasCache = cachePath is not null && File.Exists(cachePath);
        var etagPath = cachePath is null ? null : cachePath + ".etag";
        var etag = hasCache && File.Exists(etagPath) ? (await File.ReadAllTextAsync(etagPath!, ct)).Trim() : null;

        HttpResponseMessage response;
        try
        {
            response = await SendWithRetriesAsync(url, etag, ct);
        }
        catch (Exception ex) when (hasCache && ex is HttpRequestException or TaskCanceledException &&
                                   !ct.IsCancellationRequested)
        {
            return await FromCacheAsync(cachePath!, "mirror unreachable", ct);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified && hasCache)
            {
                return await FromCacheAsync(cachePath!, "not modified", ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Mirrors reorganise; last month's data beats no data, so the cache also answers for a
                // DAT that has gone missing upstream.
                if (hasCache)
                {
                    return await FromCacheAsync(cachePath!, $"HTTP {(int)response.StatusCode}", ct);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (cachePath is not null)
            {
                // Through a temp file: a cancelled build must not leave a half-written DAT that the
                // next run trusts. The ETag sidecar rides along - and is removed when the
                // mirror stopped sending one, so a stale tag can never validate new bytes.
                var temp = AtomicFile.TempPathFor(cachePath);
                await File.WriteAllBytesAsync(temp, bytes, ct);
                AtomicFile.Commit(temp, cachePath);

                var responseTag = response.Headers.ETag?.ToString();
                if (responseTag is not null)
                {
                    await AtomicFile.WriteAsync(etagPath!, Encoding.UTF8.GetBytes(responseTag), ct);
                }
                else
                {
                    AtomicFile.TryDelete(etagPath!);
                }
            }

            return new FetchedDat(url, Decode(Unwrap(bytes)));
        }
    }

    private static async Task<FetchedDat> FromCacheAsync(string cachePath, string why, CancellationToken ct) =>
        new($"cache:{Path.GetFileName(cachePath)} ({why})",
            Decode(Unwrap(await File.ReadAllBytesAsync(cachePath, ct))));

    /// <summary>Enumerates DAT-shaped files in a directory, in a stable order.</summary>
    public static IEnumerable<string> EnumerateLocalDats(string directory)
    {
        // Ordinal extension matching; see ArchiveEntrySelector for the Turkish-locale story.
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dat", ".xml", ".zip" };

        return Directory.EnumerateFiles(directory)
            .Where(p => wanted.Contains(Path.GetExtension(p)))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal);
    }

    /// <summary>Reads a local DAT, transparently unwrapping a zip or gzip container.</summary>
    public static FetchedDat ReadLocal(string path) =>
        new(path, Decode(Unwrap(File.ReadAllBytes(path))));

    /// <summary>One GET with the conditional header, retried on transport errors and 5xx responses.</summary>
    private async Task<HttpResponseMessage> SendWithRetriesAsync(string url, string? etag, CancellationToken ct)
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            try
            {
                var response = await _http.SendAsync(request, ct);
                if ((int)response.StatusCode >= 500 && attempt < attempts)
                {
                    response.Dispose();
                    await Task.Delay(_retryDelayUnit * attempt, ct);
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (attempt < attempts && ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // A rebuild should not fail on one flaky GET.
                await Task.Delay(_retryDelayUnit * attempt, ct);
            }
        }
    }

    private static byte[] Unwrap(byte[] payload)
    {
        if (payload.Length >= 4 && payload[0] == 'P' && payload[1] == 'K' && payload[2] == 3 && payload[3] == 4)
        {
            using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);

            // The *inner* entry is the document, never the archive's own name.
            var entry = archive.Entries.FirstOrDefault(e =>
                            Path.GetExtension(e.FullName).Equals(".dat", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(e.FullName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault()
                        ?? throw new InvalidDataException("Downloaded zip contained no entries.");

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            return buffer.ToArray();
        }

        if (payload.Length >= 2 && payload[0] == 0x1F && payload[1] == 0x8B)
        {
            using var gzip = new GZipStream(new MemoryStream(payload), CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            gzip.CopyTo(buffer);
            return buffer.ToArray();
        }

        return payload;
    }

    /// <summary>
    /// Decodes as UTF-8, falling back to Latin-1. A handful of older DATs are Latin-1 and contain
    /// accented titles; decoding those as UTF-8 replaces the byte with U+FFFD, which then becomes part
    /// of a name the art lookup will never match.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string CacheFileName(string sourceName)
    {
        var chars = sourceName.ToCharArray();
        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars) + ".dat";
    }

    public void Dispose() => _http.Dispose();
}
