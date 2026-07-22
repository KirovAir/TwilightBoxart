using Microsoft.AspNetCore.Mvc;
using TwilightBoxart.Pipeline;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Models;

namespace TwilightBoxart.Web.Endpoints;

/// <summary>
/// <c>POST /v2/identify</c> - batch fingerprints in, identities out.
/// </summary>
/// <remarks>
/// This is the half of the API that collapses an unbounded fingerprint space onto a bounded title
/// space. Every matched identity is written to the art record store on the way
/// out, which is what later lets <c>GET /v2/art/{platform}/{key}.png</c> resolve a name-digest key
/// with nothing but the URL.
/// </remarks>
public static class IdentifyEndpoints
{
    public static void MapIdentifyEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v2/identify", Identify)
            // Enforced in middleware before model binding, so an oversized POST costs a 413 and
            // nothing else - it never becomes an allocation.
            .WithMaxRequestBody(ApiLimits.MaxIdentifyBodyBytes)
            .RequireRateLimiting(RateLimitingExtensions.IdentifyPolicy)
            .RequireCors(CorsExtensions.ApiPostPolicy)
            .RequireApiKey()
            .WithName("Identify")
            .WithSummary("Identify a batch of ROM fingerprints.");
    }

    /// <summary>Identifies a batch of ROM fingerprints and remembers every match.</summary>
    private static async Task<IResult> Identify(
        IdentifyRequest request,
        [FromServices] IRomIdentifier identifier,
        [FromServices] ArtRecordStore records,
        [FromServices] ILogger<IdentifyLog> logger,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "No items",
                Detail = "Supply at least one fingerprint in 'items'.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        if (request.Items.Count > ApiLimits.MaxIdentifyItems)
        {
            // 400 rather than 413: the body was fine, the batch was not, and the client fix is
            // to split it rather than to shrink it.
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Batch too large",
                Detail = $"A batch may contain at most {ApiLimits.MaxIdentifyItems} items; got {request.Items.Count}.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var identities = await identifier.IdentifyBatchAsync(request.Items, ct);

        foreach (var identity in identities)
        {
            await records.RememberIdentityAsync(identity, ct);
        }

        var matched = identities.Count(i => i.IsMatched);
        logger.LogInformation("Identified {Matched}/{Total} fingerprints", matched, identities.Count);

        return Results.Ok(new IdentifyResponse { Items = identities, Matched = matched });
    }
}

/// <summary>Log category marker: <c>ILogger&lt;T&gt;</c> cannot name a static endpoints class.</summary>
public sealed class IdentifyLog;
