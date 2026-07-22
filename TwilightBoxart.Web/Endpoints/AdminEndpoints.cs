using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using TwilightBoxart.Pipeline;
using TwilightBoxart.Pipeline.Caching;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>/v2/admin/*</c> - the owner's view: stats, and the "update the No-Intro index" button.
/// </summary>
/// <remarks>
/// A password from the environment and a cookie, nothing more: one operator, one credential, no
/// users table. The comparison is constant-time and the endpoints FAIL CLOSED - no password
/// configured means 503, never an open or default login. No CORS is registered for these routes on
/// purpose, and the cookie is SameSite=Strict, so another site can neither read the stats nor
/// forge a rebuild.
/// </remarks>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v2/admin/login", Login)
            .WithMaxRequestBody(4 * 1024)
            .RequireRateLimiting(RateLimitingExtensions.LoginPolicy)
            .WithName("AdminLogin")
            .WithSummary("Trades the admin password for a session cookie.");

        routes.MapPost("/v2/admin/logout", (Delegate)Logout)
            .RequireAuthorization()
            .WithName("AdminLogout")
            .WithSummary("Ends the admin session.");

        routes.MapGet("/v2/admin/stats", GetStats)
            .RequireAuthorization()
            .WithName("AdminStats")
            .WithSummary("Index, cache, upstream and title-space numbers for the panel.");

        routes.MapPost("/v2/admin/index/rebuild", RebuildIndex)
            .RequireAuthorization()
            .WithName("AdminRebuildIndex")
            .WithSummary("Downloads fresh No-Intro data and rebuilds the index in the background.");
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext context,
        [FromServices] SecuritySettings security,
        [FromServices] ILogger<AdminLog> logger)
    {
        // Fail closed: with no password configured there is nothing to log in to.
        if (security.AdminPassword is null)
        {
            return Results.Problem(
                title: "Admin is disabled",
                detail: "Set the Twilight__Security__AdminPassword environment variable to enable the panel.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Hashed first so both sides are the same length: FixedTimeEquals short-circuits on a
        // length mismatch, which would leak the password's length one guess at a time.
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(request.Password ?? ""));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(security.AdminPassword));
        if (!CryptographicOperations.FixedTimeEquals(supplied, expected))
        {
            logger.LogWarning("Failed admin login from {Ip}", context.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(new ClaimsPrincipal(identity));
        return Results.NoContent();
    }

    private static async Task<IResult> Logout(HttpContext context)
    {
        await context.SignOutAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Everything the panel shows, in one call - it polls this while a build runs, so it must stay
    /// one cheap round trip: counts and cached numbers only, never a directory walk.
    /// </summary>
    private static async Task<IResult> GetStats(
        // The concrete wrapper, not IMetadataIndex: the panel reports on the index FILE this host
        // owns and rebuilds, which is exactly what the wrapper fronts.
        [FromServices] ReloadableMetadataIndex index,
        [FromServices] TwilightSettings settings,
        [FromServices] CacheIndex caches,
        [FromServices] UpstreamMonitor upstream,
        [FromServices] ArtRecordStore records,
        [FromServices] IndexBuildService builds,
        CancellationToken ct)
    {
        var usage = await caches.UsageAsync(ct);
        var titles = await records.CountAsync(ct);

        return Results.Ok(new AdminStats
        {
            Index = IndexHealth.From(index, settings),
            Build = builds.Status,
            Caches = [.. usage.Select(u => new CacheHealth(u.Name, u.Files, u.Bytes, u.BudgetBytes, caches.Scanned))],
            Upstreams = upstream.Snapshot(),
            Titles = titles,
        });
    }

    private static IResult RebuildIndex([FromServices] IndexBuildService builds) =>
        builds.TryStartRebuild()
            ? Results.Accepted(value: builds.Status)
            : Results.Conflict(builds.Status);
}

public sealed record LoginRequest(string? Password);

/// <summary>Log category marker: <c>ILogger&lt;T&gt;</c> cannot name a static endpoints class.</summary>
public sealed class AdminLog;

/// <summary>The panel's single payload. Owner-only, so it may say more than /v2/health does.</summary>
public sealed record AdminStats
{
    public required IndexHealth Index { get; init; }

    public required IndexBuildStatus Build { get; init; }

    public required IReadOnlyList<CacheHealth> Caches { get; init; }

    public required IReadOnlyList<UpstreamHealth> Upstreams { get; init; }

    /// <summary>Rows in the title space - how many distinct games this instance has ever resolved.</summary>
    public required int Titles { get; init; }
}
