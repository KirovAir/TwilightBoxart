using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>GET /v2/index/{file}</c> - the generated No-Intro index, for the desktop client's Local mode.
/// </summary>
/// <remarks>
/// Serving our own copy keeps a self-hosted or air-gapped deployment self-contained: without it the
/// desktop client would have to fetch the index from GitHub. Public data, ~5 MB, and served from the
/// data volume rather than wwwroot.
/// </remarks>
public static class IndexEndpoints
{
    public static void MapIndexEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet($"/v2/index/{TwilightSettings.IndexFileName}", DownloadIndex)
            .RequireRateLimiting(RateLimitingExtensions.ArtPolicy)
            .RequireCors(CorsExtensions.PublicGetPolicy)
            .RequireApiKey()
            .WithName("DownloadIndex")
            .WithSummary("The generated No-Intro index file itself.");
    }

    /// <summary>
    /// The index file, with range support so an interrupted download can resume, and the build stamp
    /// as an ETag so a desktop refresh against an unchanged index costs a 304 instead of the full
    /// ~5 MB body.
    /// </summary>
    private static IResult DownloadIndex(
        [FromServices] TwilightSettings settings,
        [FromServices] ReloadableMetadataIndex index)
    {
        if (!File.Exists(settings.IndexPath))
        {
            return Results.NotFound();
        }

        // Shared with write and delete so an admin rebuild can swap the file underneath an active
        // download; a plain Results.File(path) holds a handle that makes the swap's File.Move fail
        // on Windows.
        var stream = new FileStream(settings.IndexPath, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Write | FileShare.Delete);
        return Results.File(stream, "application/vnd.sqlite3", TwilightSettings.IndexFileName,
            enableRangeProcessing: true,
            entityTag: new EntityTagHeaderValue($"\"{index.Version}\""));
    }
}
