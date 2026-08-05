using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using TwilightBoxart.Core.Art;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>POST /v2/render</c> - a user's own image in, launcher-ready box art out.
/// </summary>
/// <remarks>
/// The "add your own cover" path. Deliberately stateless: the image is rendered and returned, and
/// nothing touches the caches, the record store or disk. The shared caches are keyed on titles
/// precisely so no request can mint unbounded entries (see <see cref="ArtEndpoints"/>), and a
/// user's image is per-user by definition, so it must never enter them. The browser keeps the
/// result; the server keeps nothing, which is also the privacy promise the web client makes.
/// </remarks>
public static class RenderEndpoints
{
    public static void MapRenderEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v2/render", Render)
            .WithMaxRequestBody(ApiLimits.MaxRenderBodyBytes)
            .RequireRateLimiting(RateLimitingExtensions.RenderPolicy)
            .RequireCors(CorsExtensions.ApiPostPolicy)
            .RequireApiKey()
            .WithName("Render")
            .WithSummary("Render a caller-supplied image into launcher-ready box art. Stores nothing.");
    }

    /// <summary>Renders the request body through the same renderer every downloaded cover goes through.</summary>
    private static async Task<IResult> Render(
        HttpContext context,
        [FromServices] IBoxartRenderer renderer,
        CancellationToken ct)
    {
        var request = context.Request;
        using var buffer = new MemoryStream((int)Math.Clamp(request.ContentLength ?? 0, 0, ApiLimits.MaxRenderBodyBytes));
        await request.Body.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();

        // Sniffed like any upstream download: only bytes that look like a decodable image reach the
        // renderer, whatever the Content-Type header claimed.
        if (!ImageSniffer.LooksLikeImage(data))
        {
            return EmptyUnsupported(context);
        }

        var options = RenderQuery.From(request.Query, RenderOptions.Default);
        byte[] rendered;
        try
        {
            rendered = renderer.Render(new ArtBlob(data, "upload", request.ContentType ?? "application/octet-stream"), options);
        }
        catch (ImageFormatException)
        {
            // The sniff only reads magic bytes; a corrupt body or a decompression bomb dies here.
            return EmptyUnsupported(context);
        }

        // no-store, unlike the art routes' max-age: this response is one user's image, and there is
        // no shared URL for a cache entry to be right under.
        context.Response.Headers.CacheControl = "no-store";
        return Results.Bytes(rendered, options.ContentType);
    }

    /// <summary>A bodyless 415, for the same reason art misses are bodyless 404s (see <see cref="ArtEndpoints"/>).</summary>
    private static IResult EmptyUnsupported(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
        return Results.Empty;
    }
}
