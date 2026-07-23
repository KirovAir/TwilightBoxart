using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TwilightBoxart.Pipeline;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>POST /api</c> - the v0.7 protocol, served for real again.
/// </summary>
/// <remarks>
/// Every installed v0.7 client still POSTs form data here on every scan - six years after the old
/// backend died - which is the definitive proof that clients in the wild never update. This route
/// used to answer 410 Gone, on the theory that v0.7 sends the ARCHIVE file name (its bug B4) and
/// would therefore mostly miss. That theory predates the header-first redesign: v0.7 also sends
/// the inner ROM's first 512 bytes as base64 and the inner ROM's whole-file SHA-1 (the old
/// client's FileMetaData.FromStream reads them from the decompressed zip entry), and those are the
/// two strongest rungs of today's identification ladder. The file name it got wrong was the
/// weakest evidence all along, so the fleet identifies better through this shim than it ever did
/// through the backend it was written for.
/// </remarks>
public static class LegacyEndpoints
{
    public static void MapLegacyEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api", ServeLegacyClient)
            .WithMaxRequestBody(ApiLimits.MaxLegacyBodyBytes)
            .RequireRateLimiting(RateLimitingExtensions.ResolvePolicy)
            .WithName("LegacyApi")
            .WithSummary("The v0.7 protocol: form fields in, PNG bytes out.");
    }

    /// <summary>Identify from the v0.7 form fields and serve the PNG, exactly as the 2020 backend did.</summary>
    private static async Task<IResult> ServeLegacyClient(
        HttpContext context,
        [FromServices] IRomIdentifier identifier,
        [FromServices] ArtPipeline pipeline,
        [FromServices] SecuritySettings security,
        [FromServices] ActivityMonitor activity,
        CancellationToken ct)
    {
        // A CORS-simple POST, so browsers send it cross-origin without a preflight. DS clients and
        // curl send no Origin header and pass; a foreign web page driving a visitor's browser here
        // is refused before any identify work happens.
        var origin = context.Request.Headers.Origin.ToString().TrimEnd('/');
        if (origin.Length > 0
            && !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}",
                StringComparison.OrdinalIgnoreCase)
            && !security.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(ct);

        if (!LooksLikeV07Client(form))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var name = form["Filename"].ToString().Trim();
        if (name.Length > ApiLimits.MaxTextLength)
        {
            // Drop the field, not the request: the header and hash can still carry the day.
            name = "";
        }

        var sha1 = form["Sha1"].ToString().Trim().ToLowerInvariant();
        var header = ArtEndpoints.TryDecodeHeader(form["Header"]);

        if (name.Length == 0 && sha1.Length == 0 && header is null)
        {
            return ArtEndpoints.EmptyNotFound(context);
        }

        var identity = await identifier.IdentifyAsync(new RomFingerprint
        {
            FileName = name,
            Sha1 = sha1.Length == 40 && sha1.All(Uri.IsHexDigit) ? sha1 : null,
            Header = header,
        }, ct);

        var matched = identity.IsMatched && ArtKey.IsValid(identity.Key);
        activity.RecordIdentify(Activity.ClientLabel(context), 1, matched ? 1 : 0);

        if (!matched)
        {
            return ArtEndpoints.EmptyNotFound(context);
        }

        var options = LegacyRenderOptions(form, RenderOptions.Default);
        var art = await pipeline.TryGetAsync(identity, options, ct);

        // Raw bytes or a bodiless 404: v0.7 writes whatever arrives straight to the SD card.
        return art is null
            ? ArtEndpoints.EmptyNotFound(context)
            : Results.Bytes(art.Png, "image/png");
    }

    /// <summary>
    /// The fields v0.7 ALWAYS sends, whatever the ROM. Its <c>BoxartCrawler</c> builds one fixed
    /// nine-element FormUrlEncodedContent per file with no conditionals, so these are present on
    /// every request that client has ever made - including the ones where Sha1 or Header come back
    /// empty because the file could not be read.
    /// </summary>
    private static readonly string[] V07Signature =
        ["BoxartWidth", "BoxartHeight", "BoxartBorderStyle", "BoxartBorderThickness"];

    /// <summary>
    /// The closest thing to a credential a 2020 binary can offer: the exact shape of its own form.
    /// </summary>
    /// <remarks>
    /// This endpoint cannot require <see cref="Core.ApiKey"/> - the clients that use it were compiled six
    /// years ago and will never send a new header. What they DO send is a fixed field set, so
    /// requiring it costs a real v0.7 client nothing and costs anyone pointing a generic scraper at
    /// <c>/api</c> the trouble of first reading a client that is not obviously still alive. That is
    /// a speed bump, not a gate, and it is deliberately all this claims to be: the rate limiter is
    /// what actually bounds the damage here.
    /// </remarks>
    private static bool LooksLikeV07Client(IFormCollection form) =>
        V07Signature.All(form.ContainsKey);

    /// <summary>
    /// Maps the v0.7 form fields onto <see cref="RenderOptions"/>. Anything absent or unparseable
    /// keeps the server default - a six-year-old client has no error channel worth speaking to, so
    /// the only useful behaviour is best-effort. Clamping happens in the pipeline like everywhere
    /// else.
    /// </summary>
    private static RenderOptions LegacyRenderOptions(IFormCollection form, RenderOptions options)
    {
        if (int.TryParse(form["BoxartWidth"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
        {
            options = options with { Width = width };
        }

        if (int.TryParse(form["BoxartHeight"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            options = options with { Height = height };
        }

        if (bool.TryParse(form["KeepAspectRatio"], out var keepAspectRatio))
        {
            options = options with { KeepAspectRatio = keepAspectRatio };
        }

        // v0.7 spells these "NintendoDSi" / "Nintendo3DS"; ignoreCase folds them onto the current
        // names, and the ordinals have never changed either way.
        if (Enum.TryParse<BoxartBorderStyle>(form["BoxartBorderStyle"], ignoreCase: true, out var style))
        {
            options = options with { BorderStyle = style };
        }

        if (int.TryParse(form["BoxartBorderThickness"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var thickness))
        {
            options = options with { BorderThickness = thickness };
        }

        if (RenderOptions.ParseColor(form["BoxartBorderColor"]) is { } argb)
        {
            options = options with { BorderColor = argb };
        }

        return options;
    }
}
