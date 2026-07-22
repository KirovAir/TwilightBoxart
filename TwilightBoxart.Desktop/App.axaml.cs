using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TwilightBoxart.Desktop.Services;
using TwilightBoxart.Desktop.ViewModels;
using TwilightBoxart.Desktop.Views;

namespace TwilightBoxart.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Build();
            var viewModel = services.GetRequiredService<MainViewModel>();

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // When the app is asked to close: stop background work and persist settings, so the next
            // launch remembers the card, the mode and the render options.
            desktop.ShutdownRequested += (_, _) =>
            {
                viewModel.Shutdown();
                viewModel.Save();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
