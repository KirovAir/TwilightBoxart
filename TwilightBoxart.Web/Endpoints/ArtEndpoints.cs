using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TwilightBoxart.Pipeline;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>/v2/art/{platform}/{key}.png</c> - the canonical, cacheable art URL.
/// </summary>
/// <remarks>
/// Keyed on the TITLE, never on the fingerprint. That is the structural fix for the cache-key
/// explosion the old backend had, where every distinct 512-byte header minted its own cache file.
/// It also means the URL is stable and shareable: the same game always has the same art URL, so a
/// CDN or a browser cache actually earns its keep.
/// </remarks>
public static class ArtEndpoints
{
    public static void MapArtEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v2/art/{platform}/{key}.png", GetCanonicalArt)
            .RequireRateLimiting(RateLimitingExtensions.ArtPolicy)
            .RequireCors(CorsExtensions.PublicGetPolicy)
            .RequireApiKey()
            .WithName("GetArtPng")
            .WithSummary("Box art for a resolved art key. The canonical, cacheable URL.");

        routes.MapGet("/v2/art.png", GetArtByFingerprint)
            .RequireRateLimiting(RateLimitingExtensions.ResolvePolicy)
            .RequireCors(CorsExtensions.PublicGetPolicy)
            .RequireApiKey()
            .WithName("GetArtByFingerprint")
            .WithSummary("Box art from a file name and header sample, in one request.");
    }

    /// <summary>Box art for a resolved art key - the canonical, cacheable URL.</summary>
    private static async Task<IResult> GetCanonicalArt(
        string platform,
        string key,
        HttpContext context,
        [FromServices] ArtPipeline pipeline,
        CancellationToken ct)
    {
        if (!TryResolve(platform, key, out var console))
        {
            return EmptyNotFound(context);
        }

        var options = RenderQuery.From(context.Request.Query, RenderOptions.Default);
        var art = await pipeline.TryGetAsync(console, key, options, ct);
        if (art is null)
        {
            return EmptyNotFound(context);
        }

        // The ETag is the original's hash, so it changes exactly when the art does, while staying
        // stable across every render variant of the same source. The render parameters are already
        // part of the URL.
        var etag = new EntityTagHeaderValue($"\"{art.Sha256[..32]}\"");
        var typed = context.Request.GetTypedHeaders();
        if (typed.IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false)))
        {
            context.Response.Headers.ETag = etag.ToString();
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        context.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Bytes(art.Png, "image/png", entityTag: etag);
    }

    /// <summary>
    /// Identify-and-deliver in one GET, for clients that cannot afford the two-phase protocol: a DS
    /// sends a file name and the ROM's first bytes and gets art in a single request, with no JSON
    /// to parse, no console to name and no header to understand.
    /// </summary>
    /// <remarks>
    /// Serves bytes directly rather than 302-ing to the canonical URL: a redirect costs a second
    /// round trip, which hurts precisely the slow clients this exists for. Content-Location carries
    /// the canonical URL instead, so a capable client learns the cacheable form for next time.
    /// </remarks>
    private static async Task<IResult> GetArtByFingerprint(
        HttpContext context,
        [FromServices] ArtPipeline pipeline,
        [FromServices] IRomIdentifier identifier,
        [FromServices] ActivityMonitor activity,
        CancellationToken ct)
    {
        var query = context.Request.Query;
        var identity = await ResolveIdentityAsync(query, identifier, ct);

        // One lookup per request on this route. Counted here because the middleware only sees
        // the status code, which cannot tell "unidentified" from "identified but no art".
        activity.RecordIdentify(Activity.ClientLabel(context), 1, identity is null ? 0 : 1);

        if (identity is null)
        {
            return EmptyNotFound(context);
        }

        var options = RenderQuery.From(query, RenderOptions.Default);
        var art = await pipeline.TryGetAsync(identity, options, ct);
        if (art is null)
        {
            return EmptyNotFound(context);
        }

        context.Response.Headers.CacheControl = "public, max-age=86400";
        // Content-Location carries ONLY render parameters. The fingerprint the caller arrived with
        // is deliberately dropped: the canonical URL is keyed on the title, so every client that
        // identifies the same game converges on one cacheable URL. Carrying the fingerprint through
        // would recreate the cache-key explosion of the old backend.
        context.Response.Headers.ContentLocation = $"{identity.ArtPath}{options.ToQueryString()}";
        return Results.Bytes(art.Png, "image/png");
    }

    /// <summary>
    /// Resolves <c>?name=</c> / <c>?header=</c> to an identity through the same identification
    /// ladder as <c>POST /v2/identify</c>. The caller sends raw facts only - no console, no serial,
    /// no format knowledge - because anything a client had to parse would freeze at whatever
    /// version it shipped with.
    /// </summary>
    private static async Task<RomIdentity?> ResolveIdentityAsync(
        IQueryCollection query, IRomIdentifier identifier, CancellationToken ct)
    {
        var name = query["name"].ToString().Trim();
        if (name.Length > ApiLimits.MaxTextLength)
        {
            return null;
        }

        var header = TryDecodeHeader(query["header"]);

        // Nothing to go on at all.
        if (name.Length == 0 && header is null)
        {
            return null;
        }

        var identity = await identifier.IdentifyAsync(
            new RomFingerprint { FileName = name, Header = header }, ct);
        return identity.IsMatched && ArtKey.IsValid(identity.Key) ? identity : null;
    }

    /// <summary>
    /// Decodes a base64 header sample (the ROM's leading bytes). Oversized input is rejected
    /// rather than truncated: every parser works within 512 bytes, so more can only be an attempt
    /// to make the request expensive. Shared with the legacy shim, whose Header field is the same
    /// encoding.
    /// </summary>
    internal static byte[]? TryDecodeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
        {
            return null;
        }

        Span<byte> buffer = stackalloc byte[768];
        return Convert.TryFromBase64String(value, buffer, out var written) && written > 0
            ? buffer[..written].ToArray()
            : null;
    }

    /// <summary>
    /// A 404 with no body at all. Image routes must never return a JSON error page: a constrained
    /// client writes whatever bytes arrive straight to disk, so an error body becomes a corrupt file
    /// the user cannot explain. An empty 404 is unambiguous to every client, including v0.7.
    /// </summary>
    internal static IResult EmptyNotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Results.Empty;
    }

    /// <summary>
    /// Maps the route values onto a console, rejecting anything that is not a known platform (slug
    /// or enum name) or a well-formed key. The key allowlist is the path-traversal boundary - see
    /// <see cref="ArtKey"/>.
    /// </summary>
    private static bool TryResolve(string platform, string key, out ConsoleType console)
    {
        console = ConsoleTypeExtensions.FromRouteValue(platform);
        return console != ConsoleType.Unknown && ArtKey.IsValid(key);
    }
}
