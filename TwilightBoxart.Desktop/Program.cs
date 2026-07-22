using Avalonia;
#if DEBUG
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TwilightBoxart.Desktop.Services;
using TwilightBoxart.Desktop.ViewModels;
using TwilightBoxart.Desktop.Views;
#endif

namespace TwilightBoxart.Desktop;

internal static class Program
{
    // Avalonia needs an STA thread and must be configured before any control is touched.
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        // Dev aid: render the main window to a PNG with no display, so the layout can be reviewed.
        //   TwilightBoxart.Desktop --screenshot out.png
        if (args is ["--screenshot", var path])
        {
            Screenshot(path);
            return;
        }
#endif

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia XAML tooling, so it stays public and static.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

#if DEBUG
    private static void Screenshot(string path)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var services = AppServices.Build();
        var window = new MainWindow
        {
            DataContext = services.GetRequiredService<MainViewModel>(),
            Width = 420,
            Height = 480,
        };
        window.Show();

        // Let layout, styling and bindings settle before the capture.
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        window.CaptureRenderedFrame()?.Save(path, PngBitmapEncoderOptions.Default);
    }
#endif
}
