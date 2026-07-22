using Microsoft.Extensions.DependencyInjection;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Render;

namespace TwilightBoxart.Core.Art;

/// <summary>Wires the art sources, the resolver and the renderer into a host.</summary>
public static class ArtServiceCollectionExtensions
{
    /// <summary>
    /// Registers the upstream art pipeline. Sources are singletons on purpose: their concurrency gate
    /// and <c>Retry-After</c> cooldown are only meaningful if they are shared process-wide.
    /// </summary>
    /// <remarks>
    /// Takes no arguments: every upstream limit is a constant in <see cref="ArtSourceLimits"/>, so the
    /// web host and the desktop client are polite to GameTDB and libretro in exactly the same way and
    /// neither can be configured into being otherwise.
    /// </remarks>
    public static IServiceCollection AddTwilightArt(this IServiceCollection services)
    {
        services.AddHttpClient(ArtSourceLimits.HttpClientName, client =>
        {
            client.Timeout = ArtSourceLimits.RequestTimeout;
            client.MaxResponseContentBufferSize = ArtSourceLimits.MaxDownloadBytes;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ArtSourceLimits.UserAgent);
        });

        services.AddSingleton<IArtSource, GameTdbArtSource>();
        services.AddSingleton<IArtSource, LibRetroArtSource>();
        services.AddSingleton<IArtSource, DsiWarePlaceholderArtSource>();
        services.AddSingleton<IBoxartRenderer, BoxartRenderer>();

        return services;
    }
}
