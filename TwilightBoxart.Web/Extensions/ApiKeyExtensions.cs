using TwilightBoxart.Core;

namespace TwilightBoxart.Web.Extensions;

/// <summary>
/// Requires <see cref="ApiKey.HeaderName"/> on the <c>/v2</c> endpoints.
/// </summary>
/// <remarks>
/// An endpoint filter rather than middleware on a path prefix, so the gate is stated on the route it
/// guards and a new endpoint has to opt in rather than inherit it by living under the right URL. See
/// <see cref="ApiKey"/> for what this does and does not achieve: it is a "you were written against
/// this API" check, not authentication.
/// </remarks>
public static class ApiKeyExtensions
{
    /// <summary>
    /// Rejects the request unless it carries the key. The response has NO body: these routes serve
    /// PNG bytes to clients that write whatever arrives straight to an SD card, and a problem-details
    /// document would land there as a corrupt .png.
    /// </summary>
    public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var supplied = context.HttpContext.Request.Headers[ApiKey.HeaderName].ToString();

            // Ordinal, fixed-value comparison. Not a constant-time one on purpose: the value is public
            // (it is in the repository), so there is no secret here for a timing attack to recover,
            // and pretending otherwise would misrepresent what this check is.
            if (!string.Equals(supplied, ApiKey.Value, StringComparison.Ordinal))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Results.Empty;
            }

            return await next(context);
        });

        return builder;
    }
}
