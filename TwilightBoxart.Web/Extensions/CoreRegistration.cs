using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Identify;
using TwilightBoxart.Web.Models;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Web.Extensions;

/// <summary>
/// Wires the identification and art services that TwilightBoxart.Core owns into the web host.
/// </summary>
/// <remarks>
/// Core registers its own art pipeline through <see cref="ArtServiceCollectionExtensions.AddTwilightArt"/>,
/// which owns the named HttpClient, the contactable User-Agent and the per-source politeness cap. The
/// web tier deliberately does not re-register those pieces by hand: the sources are singletons because
/// their concurrency gate and Retry-After cooldown are only meaningful process-wide, and rebuilding
/// that here would quietly undo it.
/// </remarks>
public static class CoreRegistration
{
    public static IServiceCollection AddTwilightCore(
        this IServiceCollection services, TwilightSettings settings)
    {
        // The reloadable wrapper, not a direct Open: the server can now rebuild the index while
        // running, so every consumer must hold the one indirection that survives the swap. It opens
        // to a null-object index when the file does not exist yet - a server with no index still
        // serves art for every serial-bearing platform, because the title id comes out of the ROM
        // header rather than out of the database.
        services.AddSingleton(sp => new ReloadableMetadataIndex(
            settings.IndexPath,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ReloadableMetadataIndex>()));
        services.AddSingleton<IMetadataIndex>(sp => sp.GetRequiredService<ReloadableMetadataIndex>());

        services.AddSingleton<IRomIdentifier, IdentificationLadder>();

        // No arguments: the upstream limits are constants in ArtSourceLimits, so the web host and the
        // desktop client are polite to GameTDB and libretro in exactly the same way.
        services.AddTwilightArt();

        return services;
    }
}
