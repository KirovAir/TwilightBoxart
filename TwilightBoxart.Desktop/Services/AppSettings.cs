using System.Text.Json;
using System.Text.Json.Serialization;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Desktop.Services;

/// <summary>
/// User settings, persisted as JSON under the per-user data directory. Deliberately the same knobs the
/// browser client and the backend expose, so a card scanned by any of them lands on identical art.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The hosted service; what a fresh install points at.</summary>
    public const string DefaultBackendUrl = "https://boxart.kirovair.com";

    /// <summary>
    /// The backend to use when it is reachable. When it is not, the app falls back to the local index
    /// automatically, so the user never has to choose. Also where the local index is downloaded from.
    /// Editable behind the advanced (gear) flyout for people who host their own.
    /// </summary>
    public string BackendUrl { get; set; } = DefaultBackendUrl;

    public string? RootFolder { get; set; }

    /// <summary>Custom boxart destination. Null means the usual spot under the SD root.</summary>
    public string? BoxartFolder { get; set; }

    public int Width { get; set; } = 128;
    public int Height { get; set; } = 115;
    public bool KeepAspectRatio { get; set; } = true;
    public BoxartBorderStyle BorderStyle { get; set; } = BoxartBorderStyle.None;
    public int BorderThickness { get; set; } = 1;

    /// <summary>Border colour as <c>#RRGGBB</c>. Alpha is always opaque; a translucent border on art is noise.</summary>
    public string BorderColor { get; set; } = "#000000";

    /// <summary>Overwrite art that is already on the card, rather than skipping it.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Concurrent art fetches. Six is what a browser opens per origin; a polite default upstream.</summary>
    public int Concurrency { get; set; } = 6;

    /// <summary>The one mapping from stored settings to render parameters.</summary>
    public RenderOptions ToRenderOptions() => new RenderOptions
    {
        Width = Width,
        Height = Height,
        KeepAspectRatio = KeepAspectRatio,
        BorderStyle = BorderStyle,
        BorderThickness = BorderThickness,
        BorderColor = RenderOptions.ParseColor(BorderColor) ?? 0xFF000000,
    }.Normalized();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string SettingsPath =>
        Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt or unreadable settings file must never stop the app from starting.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Best effort: failing to persist settings is not worth surfacing as an error.
        }
    }
}

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwilightBoxart");
}
