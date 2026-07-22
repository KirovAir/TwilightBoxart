using Microsoft.Extensions.DependencyInjection;
using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Probe;
using TwilightBoxart.Desktop.ViewModels;

namespace TwilightBoxart.Desktop.Services;

/// <summary>The composition root. Core's <see cref="ArtServiceCollectionExtensions.AddTwilightArt"/> gives the desktop the same art sources, HttpClient and renderer as the server.</summary>
public static class AppServices
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // No provider wired: ILogger<T> must resolve for Core, but the scan log is the user-facing output.
        services.AddLogging();

        services.AddTwilightArt();

        services.AddSingleton<RomProbeService>();
        services.AddSingleton<IndexManager>();
        services.AddSingleton<BackendFactory>();
        services.AddSingleton<ScanService>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
