using Microsoft.Net.Http.Headers;
using TwilightBoxart.Core.Probe;
using TwilightBoxart.Web.Extensions;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>GET /v2/formats</c> - which file extensions are worth scanning, so a client does not have to
/// know.
/// </summary>
/// <remarks>
/// This exists because the clients are the hardest thing in the system to update. The DSi homebrew
/// is a binary a user flashed to a card once and will probably never replace; the desktop app is a
/// download people keep for years. Every extension baked into one of those is a fact that goes stale
/// the day a new console is added here, and it goes stale SILENTLY, as a game the menu happily
/// launches but the scanner walks straight past.
///
/// <para>
/// Serving it instead means adding a console is a server-side change alone. An old client that never
/// learned to ask keeps working off its built-in list, exactly as well as it does today; a client
/// that does ask picks up new systems the moment the server has them, with no reflash.
/// </para>
/// </remarks>
public static class FormatsEndpoints
{
    /// <summary>
    /// Deliberately NOT JSON. The DSi client has no JSON parser and its own source says so ("this
    /// client parses nothing"): it downloads a URL to a file and reads bytes. One <c>key=csv</c> pair
    /// per line is two lines of C to consume (find the key, read to the newline, split on commas),
    /// and is just as trivial in C#. It also degrades well: a client that does not recognise a future
    /// key skips the line instead of failing to parse the document.
    /// </summary>
    private static readonly string Body =
        $"rom={string.Join(',', SupportedFiles.Rom.Order(StringComparer.Ordinal))}\n" +
        $"archive={string.Join(',', SupportedFiles.Archive.Order(StringComparer.Ordinal))}\n";

    public static void MapFormatsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v2/formats", GetFormats)
            .RequireRateLimiting(RateLimitingExtensions.ArtPolicy)
            .RequireCors(CorsExtensions.PublicGetPolicy)
            .RequireApiKey()
            .WithName("GetFormats")
            .WithSummary("File extensions worth scanning, as key=csv lines.");
    }

    /// <summary>
    /// The extension lists. Constant for the life of the process (compiled-in data, not state),
    /// so it is built once and served from cache thereafter.
    /// </summary>
    /// <remarks>
    /// No ETag: the body is a few hundred bytes, so a conditional request would spend a round trip to
    /// save less than the request itself costs. <c>Cache-Control</c> does the useful work by letting a
    /// client skip the call entirely, and a day is short enough that a newly deployed console shows up
    /// without anyone intervening.
    /// </remarks>
    private static IResult GetFormats(HttpContext context)
    {
        context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(1),
        };

        return Results.Text(Body, "text/plain; charset=utf-8");
    }
}
