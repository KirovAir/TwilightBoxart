using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwilightBoxart.Desktop.Services;
using TwilightBoxart.Core;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Surfaced for the advanced flyout's placeholder, so the default stays discoverable.</summary>
    public const string DefaultBackendUrl = AppSettings.DefaultBackendUrl;

    /// <summary>Shown in the title bar and the header, from the assembly rather than spelled out twice.</summary>
    public static string Version => About.Version;

    private readonly ScanService _scanService;
    private readonly BackendFactory _backendFactory;
    private readonly IndexManager _indexManager;
    private readonly IHttpClientFactory _httpFactory;
    private readonly List<string> _log = [];
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _refreshCts;

    public MainViewModel(
        ScanService scanService,
        BackendFactory backendFactory,
        IndexManager indexManager,
        IHttpClientFactory httpFactory)
    {
        _scanService = scanService;
        _backendFactory = backendFactory;
        _indexManager = indexManager;
        _httpFactory = httpFactory;
        Apply(AppSettings.Load());
    }

    /// <summary>Maps stored settings onto the bound properties; shared by the constructor and the reset.</summary>
    private void Apply(AppSettings settings)
    {
        BackendUrl = settings.BackendUrl;
        RootFolder = settings.RootFolder;
        BoxartManual = !string.IsNullOrWhiteSpace(settings.BoxartFolder);
        BoxartFolder = BoxartManual ? settings.BoxartFolder! : DerivedBoxartFolder;
        KeepAspectRatio = settings.KeepAspectRatio;
        Overwrite = settings.Overwrite;
        Concurrency = settings.Concurrency;

        (IsSizeClassic, IsSizeLarge, IsSizeXl) = (settings.Width, settings.Height) switch
        {
            (128, 115) => (true, false, false),
            (168, 130) => (false, true, false),
            (208, 143) => (false, false, true),
            _ => (false, false, false),
        };
        IsSizeCustom = !(IsSizeClassic || IsSizeLarge || IsSizeXl);

        // After the preset flags: their change handlers stamp preset dimensions, and the stored
        // numbers must win for the Custom case.
        Width = settings.Width;
        Height = settings.Height;

        var white = string.Equals(settings.BorderColor, "#FFFFFF", StringComparison.OrdinalIgnoreCase);
        AddBorder = settings.BorderStyle != BoxartBorderStyle.None;
        IsBorder3ds = settings.BorderStyle == BoxartBorderStyle.Nintendo3Ds;
        IsBorderWhite = settings.BorderStyle == BoxartBorderStyle.Line && white;
        IsBorderBlack = settings.BorderStyle == BoxartBorderStyle.Line && !white;
        IsBorderDsi = !(IsBorder3ds || IsBorderBlack || IsBorderWhite);
        ThickBorder = settings.BorderThickness >= 2;
    }

    /// <summary>Back to a fresh install's configuration, persisted immediately. The SD path survives.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        Apply(new AppSettings { RootFolder = RootFolder });
        Save();
        StatusText = "Settings reset to defaults.";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    private string? _rootFolder;

    partial void OnRootFolderChanged(string? value)
    {
        if (!BoxartManual)
        {
            BoxartFolder = DerivedBoxartFolder;
        }
    }

    [ObservableProperty] private string _backendUrl = "";
    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private bool _keepAspectRatio;
    [ObservableProperty] private bool _overwrite;
    [ObservableProperty] private int _concurrency;

    // The size presets of the classic app. Selecting one stamps the numbers; Custom frees them.
    [ObservableProperty] private bool _isSizeClassic;
    [ObservableProperty] private bool _isSizeLarge;
    [ObservableProperty] private bool _isSizeXl;
    [ObservableProperty] private bool _isSizeCustom;

    partial void OnIsSizeClassicChanged(bool value) { if (value) { Width = 128; Height = 115; } }
    partial void OnIsSizeLargeChanged(bool value) { if (value) { Width = 168; Height = 130; } }
    partial void OnIsSizeXlChanged(bool value) { if (value) { Width = 208; Height = 143; } }

    [ObservableProperty] private bool _addBorder;
    [ObservableProperty] private bool _isBorderDsi;
    [ObservableProperty] private bool _isBorder3ds;
    [ObservableProperty] private bool _isBorderBlack;
    [ObservableProperty] private bool _isBorderWhite;
    [ObservableProperty] private bool _thickBorder;

    /// <summary>Where the art will go. Derived from the root unless the user sets it manually.</summary>
    [ObservableProperty] private string _boxartFolder = "";

    [ObservableProperty] private bool _boxartManual;

    partial void OnBoxartManualChanged(bool value)
    {
        if (!value)
        {
            BoxartFolder = DerivedBoxartFolder;
        }
    }

    private string DerivedBoxartFolder => RootFolder is null ? "" : ScanService.BoxartDirectory(RootFolder);

    private string EffectiveBoxartFolder =>
        BoxartManual && !string.IsNullOrWhiteSpace(BoxartFolder) ? BoxartFolder : DerivedBoxartFolder;

    [ObservableProperty] private int _found;
    [ObservableProperty] private int _written;
    [ObservableProperty] private int _skipped;
    [ObservableProperty] private int _missed;
    [ObservableProperty] private double _progressValue;

    [ObservableProperty] private string _statusText = "Choose your SD card folder to begin.";
    [ObservableProperty] private string _logText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshIndexCommand))]
    private bool _isRunning;

    // Refresh replaces the index file a Local-mode scan holds open, so the two must exclude each
    // other - on Windows the atomic move would fail against the open handle anyway.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshIndexCommand))]
    private bool _isRefreshingIndex;

    private bool CanStart => !IsRunning && !IsRefreshingIndex && !string.IsNullOrWhiteSpace(RootFolder);

    private bool CanCancel => IsRunning;

    private bool CanRefreshIndex => !IsRunning && !IsRefreshingIndex;

    /// <summary>
    /// The advanced flyout's index refresh: a fresh copy from the backend when one answers, a local
    /// rebuild from the public No-Intro data otherwise. For the user whose Local-mode index is
    /// months old, or damaged.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshIndex))]
    private async Task RefreshIndexAsync()
    {
        IsRefreshingIndex = true;
        StatusText = "Refreshing the game index..";
        _refreshCts = new CancellationTokenSource();
        try
        {
            var fresh = await _indexManager.RefreshAsync(BackendUrl, _httpFactory, AddLog, _refreshCts.Token);
            StatusText = fresh
                ? "Game index refreshed."
                : "Could not refresh the game index; the current one is untouched.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Index refresh stopped.";
        }
        finally
        {
            IsRefreshingIndex = false;
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartScanAsync()
    {
        Save();
        ResetCounters();
        _cts = new CancellationTokenSource();
        IsRunning = true;

        // Created on the UI thread, so its callback marshals every update back here off the scan threads.
        var progress = new Progress<ScanUpdate>(Apply);
        IArtBackend? backend = null;

        try
        {
            var settings = ToSettings();
            backend = await _backendFactory.CreateAsync(settings, AddLog, _cts.Token);
            var request = new ScanRequest(RootFolder!, EffectiveBoxartFolder, BuildRenderOptions(), Overwrite, Concurrency);
            await Task.Run(() => _scanService.RunAsync(backend, request, progress, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
            AddLog(ex.Message);
        }
        finally
        {
            backend?.Dispose();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        StatusText = "Stopping...";
        _cts?.Cancel();
    }

    /// <summary>Finds a mounted drive that has an _nds folder at its root, like the classic Detect SD.</summary>
    [RelayCommand]
    private void DetectSd()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "_nds")))
                {
                    SetRootFolder(drive.RootDirectory.FullName);
                    return;
                }
            }
            catch (Exception)
            {
                // An unreadable mount is simply not the card.
            }
        }

        StatusText = "No mounted drive with an _nds folder found.";
    }

    /// <summary>Sets the picked card folder from the view's native folder dialog.</summary>
    public void SetRootFolder(string path)
    {
        RootFolder = path;
        StatusText = $"Ready: {path}";
    }

    public void Save() => ToSettings().Save();

    /// <summary>Cancels any scan or index refresh still running; called when the app shuts down.</summary>
    public void Shutdown()
    {
        _cts?.Cancel();
        _refreshCts?.Cancel();
    }

    private void Apply(ScanUpdate update)
    {
        var c = update.Counters;
        Found = c.Found;
        Written = c.Written;
        Skipped = c.Skipped;
        Missed = c.Missed;
        ProgressValue = c.Found > 0 ? 100.0 * (c.Written + c.Skipped + c.Missed) / c.Found : 0;

        if (update.Status is { } status)
        {
            StatusText = status;
        }

        if (update.Log is { } line)
        {
            AddLog(line);
        }
    }

    private void AddLog(string line)
    {
        // Newest first, so the latest event is always visible without scrolling; capped so a long scan
        // cannot grow the string without bound.
        _log.Insert(0, line);
        if (_log.Count > 200)
        {
            _log.RemoveRange(200, _log.Count - 200);
        }

        LogText = string.Join('\n', _log);
    }

    private void ResetCounters()
    {
        Found = Written = Skipped = Missed = 0;
        ProgressValue = 0;
        _log.Clear();
        LogText = "";
    }

    private BoxartBorderStyle SelectedBorderStyle =>
        !AddBorder ? BoxartBorderStyle.None
        : IsBorder3ds ? BoxartBorderStyle.Nintendo3Ds
        : IsBorderBlack || IsBorderWhite ? BoxartBorderStyle.Line
        : BoxartBorderStyle.NintendoDsi;

    private AppSettings ToSettings() => new()
    {
        BackendUrl = BackendUrl,
        RootFolder = RootFolder,
        BoxartFolder = BoxartManual && !string.IsNullOrWhiteSpace(BoxartFolder) ? BoxartFolder : null,
        Width = Width,
        Height = Height,
        KeepAspectRatio = KeepAspectRatio,
        BorderStyle = SelectedBorderStyle,
        BorderThickness = ThickBorder ? 2 : 1,
        BorderColor = IsBorderWhite ? "#FFFFFF" : "#000000",
        Overwrite = Overwrite,
        Concurrency = Concurrency,
    };

    private RenderOptions BuildRenderOptions() => ToSettings().ToRenderOptions();
}
