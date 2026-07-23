using System.Diagnostics;
using TwilightBoxart.Core;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Web.Extensions;

/// <summary>Anonymous aggregate activity by client type.</summary>
public static class Activity
{
    public const string OtherLabel = "other";

    private const string LegacyLabel = "v0.7";

    private const string DsiUserAgentPrefix = "TwilightBoxart-DSi/";

    private const int MaxLabelLength = 32;

    private static readonly object LabelKey = new();

    public static WebApplication UseActivity(this WebApplication app)
    {
        var activity = app.Services.GetRequiredService<ActivityMonitor>();
        var logger = app.Services.GetRequiredService<ILogger<ActivityMonitor>>();

        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            if (endpoint is null or "Health"
                || endpoint.StartsWith("Admin", StringComparison.Ordinal)
                || HttpMethods.IsOptions(context.Request.Method))
            {
                await next(context);
                return;
            }

            var started = Stopwatch.GetTimestamp();
            var failed = false;
            try
            {
                await next(context);
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                var client = ClientLabel(context);
                var status = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                activity.RecordRequest(client, status);

                if (endpoint is "GetArtPng" or "GetArtByFingerprint" or "LegacyApi" && (status is 200 or 304 or 404))
                {
                    activity.RecordArt(client, hit: status != 404);
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("{Client} {Method} {Path} answered {Status} in {Elapsed:0.0}ms",
                        client, context.Request.Method, context.Request.Path, status,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
        });

        return app;
    }

    public static string ClientLabel(HttpContext context)
    {
        if (context.Items.TryGetValue(LabelKey, out var cached))
        {
            return (string)cached!;
        }

        var label = Classify(context);
        context.Items[LabelKey] = label;
        return label;
    }

    private static string Classify(HttpContext context)
    {
        var declared = context.Request.Headers[ClientHeader.Name].ToString();
        if (declared.Length > 0)
        {
            return Sanitize(declared);
        }

        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (userAgent.StartsWith(DsiUserAgentPrefix, StringComparison.Ordinal))
        {
            return Sanitize("dsi/" + userAgent[DsiUserAgentPrefix.Length..]);
        }

        return context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "LegacyApi"
            ? LegacyLabel
            : OtherLabel;
    }

    private static string Sanitize(string value)
    {
        var label = value.Trim();
        if (label.Length is 0 or > MaxLabelLength)
        {
            return OtherLabel;
        }

        foreach (var c in label)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '/' or '_' or '-'))
            {
                return OtherLabel;
            }
        }

        return label;
    }
}
