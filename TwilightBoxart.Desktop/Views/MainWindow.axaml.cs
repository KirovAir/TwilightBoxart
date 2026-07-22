using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TwilightBoxart.Desktop.ViewModels;

namespace TwilightBoxart.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnLogoPressed(object? sender, PointerPressedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri(TwilightBoxart.Core.About.RepositoryUrl));

    // The native folder dialogs need the window's StorageProvider, so they live here and hand the
    // chosen path back to the view model. async void: an unhandled exception here would take the
    // process down, so failures are caught and surfaced instead.
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickFolderAsync("Select your SD card (the folder that contains _nds)");
            if (path is not null && DataContext is MainViewModel viewModel)
            {
                viewModel.SetRootFolder(path);
            }
        }
        catch (Exception ex)
        {
            ReportPickerFailure(ex);
        }
    }

    private async void OnBrowseBoxart(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickFolderAsync("Select where the boxart should go");
            if (path is not null && DataContext is MainViewModel viewModel)
            {
                viewModel.BoxartFolder = path;
            }
        }
        catch (Exception ex)
        {
            ReportPickerFailure(ex);
        }
    }

    private void ReportPickerFailure(Exception ex)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StatusText = $"Could not open the folder picker: {ex.Message}";
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
