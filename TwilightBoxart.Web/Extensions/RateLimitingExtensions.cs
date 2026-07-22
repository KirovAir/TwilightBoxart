using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TwilightBoxart.Web.Extensions;

/// <summary>
/// Per-IP fixed-window rate limits: generous, but present.
/// </summary>
/// <remarks>
/// The 2020 endpoint was unauthenticated and unthrottled, which is what made the cache-key explosion
/// a practical disk-exhaustion attack rather than a theoretical one. Title-keyed caching removes the
/// amplification; this removes the volume.
///
/// The budgets are sized against the real workload rather than picked round: a full library scan is
/// ~18,000 art requests, a DS pulls up to 500 covers per session, and identify is batched at 500
/// fingerprints per call, so 60 identify calls a minute already covers 30,000 ROMs a minute.
///
/// Partitioned on <see cref="ConnectionInfo.RemoteIpAddress"/>, NOT on a forwarded header. Trusting
/// X-Forwarded-For without a configured proxy allowlist would let any client pick its own bucket.
/// </remarks>
public static class RateLimitingExtensions
{
    public const string IdentifyPolicy = "identify";
    public const string ArtPolicy = "art";
    /// <summary>Constrained clients that fetch one image per request rather than batching.</summary>
    public const string ResolvePolicy = "resolve";
    /// <summary>Tight, because every request is a password guess.</summary>
    public const string LoginPolicy = "login";

    public static IServiceCollection AddTwilightRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                return ValueTask.CompletedTask;
            };

            // Backstop for anything not carrying an explicit policy, including the legacy /api route.
            // Leases chain, so this must exceed every per-endpoint policy or it becomes the real limit.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => Partition(context, "global", permitLimit: 2000));

            Add(options, IdentifyPolicy, permitLimit: 60);
            Add(options, ArtPolicy, permitLimit: 1800);
            Add(options, ResolvePolicy, permitLimit: 1200);
            Add(options, LoginPolicy, permitLimit: 10);
        });

    private static void Add(RateLimiterOptions options, string name, int permitLimit) =>
        options.AddPolicy(name, context => Partition(context, name, permitLimit));

    private static RateLimitPartition<string> Partition(HttpContext context, string name, int permitLimit)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{name}:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }
}
