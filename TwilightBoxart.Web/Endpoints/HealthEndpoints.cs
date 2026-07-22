using Microsoft.AspNetCore.Mvc;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>GET /v2/health</c> - is this instance up, and is it whole.
/// </summary>
/// <remarks>
/// Two fields and nothing else, on purpose. This used to publish the index version, per-layer cache
/// occupancy and per-upstream counters, and not one consumer ever read them: its only caller is the
/// desktop client deciding between backend and local mode, and that reads the status CODE. The admin
/// panel, which does want the detail, has always had its own owner-only <c>/v2/admin/stats</c>
/// carrying the same records plus the title count. So the body was an unauthenticated readout of
/// cache byte counts and upstream error strings serving nobody, and it is gone.
///
/// <para>
/// What survives is the one thing the 2020 failure argued for: when the art host stopped resolving,
/// the old tool said "Finished scan." and a 0% match rate looked exactly like a total outage.
/// <c>degraded</c> is that distinction, and it is cheap enough to keep unauthenticated.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v2/health", GetHealth)
            .RequireCors(CorsExtensions.PublicGetPolicy)
            .WithName("Health")
            .WithSummary("Whether this instance is up, and whether it has an index.");
    }

    /// <summary>
    /// Always 200: a degraded instance still serves cached art, and a load balancer pulling it out
    /// of rotation for a missing index would turn a partial outage into a full one. Callers that
    /// only need "is it there" can stop at the status code, which is what the desktop client does.
    /// </summary>
    private static IResult GetHealth(
        [FromServices] IMetadataIndex index,
        [FromServices] TwilightSettings settings) =>
        Results.Ok(new HealthResponse
        {
            Status = IndexHealth.From(index, settings).Available ? "ok" : "degraded",
        });
}
