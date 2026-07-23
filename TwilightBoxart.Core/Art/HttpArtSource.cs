using System.Net;
using Microsoft.Extensions.Logging;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Art;

/// <summary>
/// Shared HTTP plumbing for the art sources: a politeness gate, real status-code handling, and
/// <c>Retry-After</c> back-off. Everything here funnels into "a hit, or null"; a miss is not an
/// exception, per <see cref="IArtSource"/>.
/// </summary>
public abstract class HttpArtSource(
    IHttpClientFactory httpClientFactory,
    ILogger logger)
{
    // Singleton-scoped, so this really is a process-wide cap on how hard we lean on one upstream.
    private readonly SemaphoreSlim _gate = new(ArtSourceLimits.MaxConcurrency);

    // Set when an upstream sends Retry-After. Shared across the source so a 429 for one ROM also backs
    // off the other N requests already queued behind the gate, instead of each rediscovering the limit.
    private long _cooldownUntilTicks;

    /// <summary>Short name used in logs, e.g. "gametdb".</summary>
    protected abstract string SourceName { get; }

    /// <summary>
    /// GETs <paramref name="url"/> and returns the bytes, or null for any outcome that is not usable
    /// art. Misses log at Debug; transport failures and unexpected statuses log at Warning, so the two
    /// are distinguishable in operations (a wall of Warnings means the network is broken, not that the
    /// library is obscure).
    /// </summary>
    protected async Task<ArtBlob?> TryGetAsync(string url, CancellationToken ct)
    {
        var followed = false;
        for (var attempt = 0; ; attempt++)
        {
            // Every sleep happens outside the gate. Holding a slot through a cooldown or a
            // Retry-After backoff would let a single 429 pin one of the few politeness slots for
            // up to MaxRetryAfter, throttling requests the upstream never complained about.
            await WaitOutCooldownAsync(ct);

            AttemptResult result;
            await _gate.WaitAsync(ct);
            try
            {
                result = await AttemptAsync(url, attempt, ct);
            }
            finally
            {
                _gate.Release();
            }

            if (result.Follow is { } target && !followed)
            {
                // One hop only: a symlink pointing at another symlink is a miss, not a chase.
                followed = true;
                url = target;
                continue;
            }

            if (result.Backoff is not { } backoff)
            {
                return result.Blob;
            }

            // Waited out here rather than inside AttemptAsync, so the response and its connection are
            // already released while we sit out the upstream's Retry-After.
            await Task.Delay(backoff, ct);
        }
    }

    /// <summary>
    /// Maps a 200 body that is not an image onto a follow-up URL, for upstreams that serve pointers
    /// in place of files. Null means "genuinely not art". Followed at most once per fetch.
    /// </summary>
    protected virtual string? TryResolveSymlink(string url, byte[] body) => null;

    /// <summary>
    /// One GET. A non-null <see cref="AttemptResult.Backoff"/> means "the upstream asked us to wait and
    /// try again"; otherwise <see cref="AttemptResult.Blob"/> is the final answer, null included.
    /// </summary>
    private async Task<AttemptResult> AttemptAsync(string url, int attempt, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            var client = httpClientFactory.CreateClient(ArtSourceLimits.HttpClientName);
            response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Includes the TaskCanceledException HttpClient raises on its own timeout.
            logger.LogWarning(ex, "{Source}: transport failure for {Url}", SourceName, url);
            return AttemptResult.Miss;
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.NoContent)
            {
                // The expected outcome for most ROMs. The 2020 client reached this conclusion by
                // string-matching "404" inside an exception message, which broke on any
                // localised or reworded message; we read the status code.
                logger.LogDebug("{Source}: no art at {Url} ({Status})", SourceName, url, (int)response.StatusCode);
                return AttemptResult.Miss;
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var backoff = ReadRetryAfter(response.Headers.RetryAfter);
                BeginCooldown(backoff);

                if (attempt >= ArtSourceLimits.MaxRetries)
                {
                    logger.LogWarning(
                        "{Source}: throttled by upstream ({Status}) and out of retries for {Url}",
                        SourceName, (int)response.StatusCode, url);
                    return AttemptResult.Miss;
                }

                logger.LogInformation(
                    "{Source}: throttled ({Status}), backing off {Delay} before retrying {Url}",
                    SourceName, (int)response.StatusCode, backoff, url);
                return new AttemptResult(null, backoff);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "{Source}: unexpected status {Status} for {Url}", SourceName, (int)response.StatusCode, url);
                return AttemptResult.Miss;
            }

            if (response.Content.Headers.ContentLength > ArtSourceLimits.MaxDownloadBytes)
            {
                logger.LogWarning(
                    "{Source}: {Url} is {Length} bytes, over the {Max} byte ceiling",
                    SourceName, url, response.Content.Headers.ContentLength, ArtSourceLimits.MaxDownloadBytes);
                return AttemptResult.Miss;
            }

            // The Content-Length check above is advisory: a chunked response has none, and under
            // ResponseHeadersRead the client's MaxResponseContentBufferSize never applies either.
            // This capped copy is the ceiling that actually holds.
            var data = await ReadCappedAsync(response.Content, ArtSourceLimits.MaxDownloadBytes, ct);
            if (data is null)
            {
                logger.LogWarning(
                    "{Source}: {Url} exceeded the {Max} byte ceiling mid-stream",
                    SourceName, url, ArtSourceLimits.MaxDownloadBytes);
                return AttemptResult.Miss;
            }

            // A 200 carrying an HTML soft-404 would otherwise count as a hit and stop the resolver from
            // trying the next source. Sniff the magic bytes rather than trusting Content-Type.
            if (!ImageSniffer.LooksLikeImage(data))
            {
                if (data.Length == 0)
                {
                    // GameTDB does this for a handful of broken entries: image/jpeg headers, no body.
                    logger.LogWarning("{Source}: {Url} returned a 200 with an empty body", SourceName, url);
                    return AttemptResult.Miss;
                }

                if (TryResolveSymlink(url, data) is { } target)
                {
                    logger.LogDebug("{Source}: {Url} is a symlink to {Target}", SourceName, url, target);
                    return new AttemptResult(null, null, target);
                }

                logger.LogWarning(
                    "{Source}: {Url} returned {Length} bytes that are not a recognised image",
                    SourceName, url, data.Length);
                return AttemptResult.Miss;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            logger.LogDebug("{Source}: hit {Url} ({Length} bytes)", SourceName, url, data.Length);
            return new AttemptResult(new ArtBlob(data, url, contentType), null);
        }
    }

    private readonly record struct AttemptResult(ArtBlob? Blob, TimeSpan? Backoff, string? Follow = null)
    {
        public static readonly AttemptResult Miss = new(null, null);
    }

    /// <summary>Buffers the body, or returns null the moment it grows past <paramref name="maxBytes"/>.</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private TimeSpan ReadRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? header)
    {
        var requested = header switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => TimeSpan.FromSeconds(1),
        };

        if (requested < TimeSpan.Zero)
        {
            requested = TimeSpan.Zero;
        }

        return requested > ArtSourceLimits.MaxRetryAfter ? ArtSourceLimits.MaxRetryAfter : requested;
    }

    private void BeginCooldown(TimeSpan delay)
    {
        var until = DateTime.UtcNow.Add(delay).Ticks;
        long current;
        do
        {
            current = Interlocked.Read(ref _cooldownUntilTicks);
            if (current >= until)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _cooldownUntilTicks, until, current) != current);
    }

    private async Task WaitOutCooldownAsync(CancellationToken ct)
    {
        var remaining = new DateTime(Interlocked.Read(ref _cooldownUntilTicks), DateTimeKind.Utc) - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining > ArtSourceLimits.MaxRetryAfter ? ArtSourceLimits.MaxRetryAfter : remaining, ct);
        }
    }
}
