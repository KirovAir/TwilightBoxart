using Microsoft.AspNetCore.Http.Features;

namespace TwilightBoxart.Web.Extensions;

/// <summary>Per-endpoint request body ceiling, in bytes.</summary>
public sealed record MaxRequestBodyMetadata(long Bytes);

/// <summary>
/// Enforces a per-endpoint body size ceiling before model binding reads a single byte.
/// </summary>
/// <remarks>
/// Checked in middleware rather than in an endpoint filter on purpose: a filter runs after parameter
/// binding, so by the time it could reject an oversized body the server has already deserialised it -
/// which is precisely the allocation the limit exists to prevent.
///
/// Both halves matter. Content-Length catches the ordinary case cheaply and lets us return a clean
/// 413. Setting <see cref="IHttpMaxRequestBodySizeFeature"/> covers a chunked upload that declares no
/// length, where the server aborts the read once the ceiling is crossed.
/// </remarks>
public static class RequestLimits
{
    public static RouteHandlerBuilder WithMaxRequestBody(this RouteHandlerBuilder builder, long bytes) =>
        builder.WithMetadata(new MaxRequestBodyMetadata(bytes));

    public static WebApplication UseRequestBodyLimits(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<MaxRequestBodyMetadata>();
            if (limit is null)
            {
                await next(context);
                return;
            }

            if (context.Request.ContentLength > limit.Bytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = limit.Bytes;
            }

            await next(context);
        });

        return app;
    }
}
