using TwilightBoxart.Web.Models;

namespace TwilightBoxart.Web.Extensions;

/// <summary>
/// Two CORS policies, split by whether the request mutates anything.
/// </summary>
/// <remarks>
/// GETs under /v2 are public data served without credentials, so <c>Access-Control-Allow-Origin: *</c>
/// costs nothing; POSTs write to disk and get an origin allowlist. Preflights are cached for 24
/// hours, so a browser identifying 18,000 ROMs in batches does not pay a round-trip per batch.
/// </remarks>
public static class CorsExtensions
{
    /// <summary>Anonymous, credential-free reads. Any origin.</summary>
    public const string PublicGetPolicy = "public-get";

    /// <summary>Writes: identify. Allowlisted origins only.</summary>
    public const string ApiPostPolicy = "api-post";

    private static readonly TimeSpan PreflightMaxAge = TimeSpan.FromSeconds(86400);

    public static IServiceCollection AddTwilightCors(this IServiceCollection services, SecuritySettings security) =>
        services.AddCors(options =>
        {
            options.AddPolicy(PublicGetPolicy, policy => policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .WithMethods("GET", "HEAD", "OPTIONS")
                .SetPreflightMaxAge(PreflightMaxAge));

            options.AddPolicy(ApiPostPolicy, policy =>
            {
                policy.WithMethods("POST", "OPTIONS")
                    .AllowAnyHeader()
                    .SetPreflightMaxAge(PreflightMaxAge);

                if (security.AllowedOrigins.Length > 0)
                {
                    policy.WithOrigins(security.AllowedOrigins);
                }

                // No allowlist configured means no cross-origin POSTs are permitted. Deliberately not
                // AllowAnyOrigin: these endpoints write to disk, and same-origin callers are unaffected.
            });
        });
}
