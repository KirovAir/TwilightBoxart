using System.Security.Cryptography;
using TwilightBoxart.Data.Entities;
using TwilightBoxart.Data.Extensions;
using TwilightBoxart.Pipeline.Caching;

namespace TwilightBoxart.Pipeline;

/// <summary>A rendered PNG plus the hash of the original it came from.</summary>
public sealed record RenderedArt(byte[] Png, string Sha256);

/// <summary>
/// Identity key -> upstream art -> cached original -> cached render -> PNG bytes.
/// </summary>
/// <remarks>
/// Keying on the TITLE rather than on the fingerprint is the fix for a 2020 defect: it bounds the
/// cache-key space, which is what removes the disk-exhaustion DoS in BoxartRequest.FilenameHash.
/// The render key embeds the original's SHA-256, so new source bytes invalidate every size and
/// border variant of the title without any explicit invalidation pass.
/// </remarks>
public sealed class ArtPipeline(
    ArtCaches caches,
    CacheIndex cacheIndex,
    ArtRecordStore records,
    ArtFetcher fetcher,
    IBoxartRenderer renderer,
    IMetadataIndex index,
    SingleFlight singleFlight,
    ILogger<ArtPipeline> logger)
{
    /// <summary>
    /// Resolves and renders art for a title. Returns null for a genuine miss - callers turn that into
    /// a 404, never a 500. The 2020 backend threw a bare Exception on an unknown ROM type and the
    /// controller only caught NoMatchException, so every unmatched N64 zip produced an HTTP 500.
    /// </summary>
    public Task<RenderedArt?> TryGetAsync(
        ConsoleType console, string key, RenderOptions options, CancellationToken ct = default) =>
        TryGetAsync(console, key, resolved: null, options, ct);

    /// <summary>
    /// Same, carrying the identity the caller just resolved. The canonical name the ladder produced
    /// is what keeps the name-addressed sources reachable for a cold name-keyed title (GB, NES, ..).
    /// </summary>
    public Task<RenderedArt?> TryGetAsync(
        RomIdentity identity, RenderOptions options, CancellationToken ct = default) =>
        TryGetAsync(identity.ConsoleType, identity.Key, identity, options, ct);

    private async Task<RenderedArt?> TryGetAsync(
        ConsoleType console, string key, RomIdentity? resolved, RenderOptions options, CancellationToken ct)
    {
        // Never trust the query string: the 2020 backend skipped this clamp, leaving width completely
        // unclamped and one request away from an OOM.
        options = options.Normalized();

        // Fold case here, where every art route converges. The record table collates NOCASE but the
        // filesystem does not, so "ASME" and "asme" would otherwise share one row while writing two
        // render files, the second of which no row accounts for: never counted, never evicted, and a
        // guaranteed unique-index violation when the startup reconcile tries to adopt it.
        key = ArtKey.Normalize(key);

        var record = await EnsureOriginalAsync(console, key, resolved, ct);
        if (record?.Sha256 is null)
        {
            return null;
        }

        var sha = record.Sha256;
        var renderPath = ArtCaches.RenderPath(console, key, sha, options);
        var cached = await cacheIndex.TryReadAsync(caches.Renders, renderPath, ct);
        if (cached is not null)
        {
            return new RenderedArt(cached, sha);
        }

        var png = await singleFlight.RunAsync<byte[]?>($"render:{renderPath}", async shared =>
        {
            var again = await cacheIndex.TryReadAsync(caches.Renders, renderPath, shared);
            if (again is not null)
            {
                return again;
            }

            var originalPath = ArtCaches.OriginalPath(sha);
            var original = await cacheIndex.TryReadAsync(caches.Originals, originalPath, shared);
            if (original is null)
            {
                // The original was evicted (or removed) out from under a live record. Drop the
                // pointer so the next request refetches rather than 404ing forever.
                logger.LogInformation("Original {Sha} for {Console}/{Key} is gone; forcing a refetch",
                    sha[..8], console.Slug(), key);
                await records.UpsertAsync(console, key, r =>
                {
                    r.Sha256 = null;
                    r.MissUntil = null;
                }, shared);
                return null;
            }

            byte[] rendered;
            try
            {
                rendered = renderer.Render(
                    new ArtBlob(original, record.SourceUrl ?? "", record.ContentType ?? "image/png"),
                    options);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A cached original that will not decode - truncated by an old bug, or an upstream that
                // served HTML with an image content type - must be a miss, not a 500. This class exists
                // precisely so callers never see an exception (see the summary above); letting the
                // renderer's ImageFormatException through would reproduce the 2020 backend's behaviour
                // of turning an unmatched ROM into an HTTP 500.
                logger.LogWarning(ex, "Render failed for {Console}/{Key}; treating as a miss", console.Slug(), key);
                return null;
            }

            // The source hash is stored on the render's own row so the relationship between the two
            // layers is queryable, not just encoded in a path.
            await cacheIndex.WriteAsync(caches.Renders, renderPath, rendered, sha, shared);
            return rendered;
        }, ct);

        return png is null ? null : new RenderedArt(png, sha);
    }

    private async Task<ArtRecord?> EnsureOriginalAsync(
        ConsoleType console, string key, RomIdentity? resolved, CancellationToken ct)
    {
        var record = await records.TryGetRecordAsync(console, key, ct);
        if (record?.Sha256 is not null)
        {
            return record;
        }

        if (record?.MissUntil is { } until && until > DateTime.UtcNow &&
            !ResolvedKnowsMore(resolved, record))
        {
            return null;
        }

        return await singleFlight.RunAsync<ArtRecord?>($"fetch:{console.Slug()}/{key}", async shared =>
        {
            var current = await records.TryGetRecordAsync(console, key, shared);
            if (current?.Sha256 is not null)
            {
                return current;
            }

            if (current?.MissUntil is { } stillMissing && stillMissing > DateTime.UtcNow &&
                !ResolvedKnowsMore(resolved, current))
            {
                return null;
            }

            var identity = BuildIdentity(console, key, current, resolved);

            // A serial that merely parrots the key (BuildIdentity's last resort) is re-derived for
            // free on every request. Persisting it would make the row immune to PruneUnresolvedAsync,
            // which is the only thing keeping unknown-key traffic from growing that table forever.
            var learnedSerial = string.Equals(identity.Serial, key, StringComparison.OrdinalIgnoreCase)
                ? null
                : identity.Serial;

            var fetched = await fetcher.TryFetchAsync(identity, shared);
            if (fetched is null)
            {
                var missed = await records.UpsertAsync(console, key, r =>
                {
                    // Remember what this attempt knew: a later retry from the key-only route can
                    // then still address the name-keyed sources.
                    r.Serial ??= learnedSerial;
                    r.CanonicalName ??= identity.CanonicalName;
                    r.MissUntil = DateTime.UtcNow + CacheSettings.NegativeCacheDuration;
                }, shared);
                logger.LogDebug("No art for {Console}/{Key}; backing off until {Until}",
                    console.Slug(), key, missed.MissUntil);
                return null;
            }

            var sha = Convert.ToHexStringLower(SHA256.HashData(fetched.Blob.Data));
            await cacheIndex.WriteAsync(caches.Originals, ArtCaches.OriginalPath(sha), fetched.Blob.Data, sha, shared);

            return await records.UpsertAsync(console, key, r =>
            {
                r.Serial ??= learnedSerial;
                r.CanonicalName ??= identity.CanonicalName;
                r.Sha256 = sha;
                r.SourceUrl = fetched.Blob.SourceUrl;
                r.ContentType = fetched.Blob.ContentType;
                r.Source = fetched.Source;
                r.MissUntil = null;
            }, shared);
        }, ct);
    }

    /// <summary>
    /// Reconstructs enough of a <see cref="RomIdentity"/> for the art sources to work from just
    /// <c>{platform}/{key}</c>. Sources of truth, most authoritative first: the stored record
    /// (written by identify), the identity the caller resolved for this request, the generated index
    /// by serial, then the key itself.
    /// </summary>
    private RomIdentity BuildIdentity(
        ConsoleType console, string key, ArtRecord? record, RomIdentity? resolved)
    {
        var serial = record?.Serial ?? resolved?.Serial;
        var canonicalName = record?.CanonicalName ?? resolved?.CanonicalName;

        // A key that is not a name digest IS the title id, which is exactly what GameTDB addresses by.
        if (serial is null && !ArtKey.IsNameDigest(key))
        {
            serial = key;
        }

        if (canonicalName is null && serial is not null)
        {
            try
            {
                if (index.TryBySerial(console, serial, out var entry))
                {
                    canonicalName = entry.Name;
                }
            }
            catch (Exception ex)
            {
                // The index is a generated file that may be absent on a first run. Missing art is a
                // far better outcome than a failed request.
                logger.LogDebug(ex, "Index lookup failed for {Console}/{Serial}", console.Slug(), serial);
            }
        }

        return new RomIdentity
        {
            ConsoleType = console,
            Key = key,
            Serial = serial,
            CanonicalName = canonicalName,
            Title = record?.Title,
            RegionId = record?.RegionId is { Length: > 0 } region ? region[0] : null,
            MatchMethod = serial is not null ? MatchMethod.HeaderSerial : MatchMethod.Filename,
        };
    }

    /// <summary>
    /// True when the caller's resolved identity carries a canonical name the stored record lacks. A
    /// miss recorded identity-blind (the key-only route, before anything identified the title) must
    /// not hold back a request that can actually address the name-keyed sources.
    /// </summary>
    private static bool ResolvedKnowsMore(RomIdentity? resolved, ArtRecord record) =>
        resolved?.CanonicalName is { Length: > 0 } && string.IsNullOrEmpty(record.CanonicalName);
}
