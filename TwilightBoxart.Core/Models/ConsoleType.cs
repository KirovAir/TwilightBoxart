using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace TwilightBoxart.Core.Models;

/// <summary>
/// Supported console types. <see cref="DescriptionAttribute"/> matches the No-Intro / libretro-thumbnails
/// repository naming ("Nintendo - Game Boy" -> "Nintendo_-_Game_Boy"); <see cref="DisplayAttribute"/>
/// is the label a user reads; <see cref="ConsoleTypeExtensions.Slug"/> gives the short lowercase form
/// used in API routes.
/// </summary>
public enum ConsoleType
{
    Unknown = 0,

    [Display(Name = "Game Boy")]
    [Description("Nintendo - Game Boy")]
    GameBoy,

    [Display(Name = "Game Boy Color")]
    [Description("Nintendo - Game Boy Color")]
    GameBoyColor,

    [Display(Name = "Game Boy Advance")]
    [Description("Nintendo - Game Boy Advance")]
    GameBoyAdvance,

    [Display(Name = "Nintendo DS")]
    [Description("Nintendo - Nintendo DS")]
    NintendoDs,

    // NOT "Nintendo - Nintendo DSi (Digital)". That is DAT-o-MATIC's name for the DSiWare set, and it
    // is what the 2020 crawler used, but neither libretro repository answers to it: the thumbnails org
    // has no such repo (verified 404) and the libretro-database No-Intro mirror publishes the same set
    // as plain "Nintendo - Nintendo DSi". Since both consumers of this attribute are libretro, the
    // "(Digital)" spelling silently 404s every DSi cover.
    [Display(Name = "Nintendo DSi")]
    [Description("Nintendo - Nintendo DSi")]
    NintendoDsi,

    [Display(Name = "NES")]
    [Description("Nintendo - Nintendo Entertainment System")]
    Nes,

    [Display(Name = "SNES")]
    [Description("Nintendo - Super Nintendo Entertainment System")]
    Snes,

    [Display(Name = "Nintendo 64")]
    [Description("Nintendo - Nintendo 64")]
    Nintendo64,

    [Display(Name = "Famicom Disk System")]
    [Description("Nintendo - Family Computer Disk System")]
    FamicomDiskSystem,

    [Display(Name = "Mega Drive")]
    [Description("Sega - Mega Drive - Genesis")]
    MegaDrive,

    [Display(Name = "Master System")]
    [Description("Sega - Master System - Mark III")]
    MasterSystem,

    [Display(Name = "Game Gear")]
    [Description("Sega - Game Gear")]
    GameGear,

    [Display(Name = "Pokemon Mini")]
    [Description("Nintendo - Pokemon Mini")]
    PokemonMini,

    [Display(Name = "SG-1000")]
    [Description("Sega - SG-1000")]
    Sg1000,

    [Display(Name = "PC Engine")]
    [Description("NEC - PC Engine - TurboGrafx 16")]
    PcEngine,

    [Display(Name = "WonderSwan")]
    [Description("Bandai - WonderSwan")]
    WonderSwan,

    [Display(Name = "WonderSwan Color")]
    [Description("Bandai - WonderSwan Color")]
    WonderSwanColor,

    [Display(Name = "Neo Geo Pocket")]
    [Description("SNK - Neo Geo Pocket")]
    NeoGeoPocket,

    [Display(Name = "Neo Geo Pocket Color")]
    [Description("SNK - Neo Geo Pocket Color")]
    NeoGeoPocketColor,

    [Display(Name = "Atari 2600")]
    [Description("Atari - 2600")]
    Atari2600,

    [Display(Name = "Atari 5200")]
    [Description("Atari - 5200")]
    Atari5200,

    [Display(Name = "Atari 7800")]
    [Description("Atari - 7800")]
    Atari7800,

    [Display(Name = "ColecoVision")]
    [Description("Coleco - ColecoVision")]
    ColecoVision,

    [Display(Name = "Intellivision")]
    [Description("Mattel - Intellivision")]
    Intellivision,

    [Display(Name = "MSX")]
    [Description("Microsoft - MSX")]
    Msx,

    // No extension of its own: TWiLightMenu++ launches both MSX generations from .msx, so the
    // extension hint can only name one of them. That costs nothing, because the extension is only
    // consulted by the fuzzy-name rung: an MSX2 dump identified by CRC32 comes back carrying its
    // own console from the DAT. It must NOT be folded into Msx the way Download Play folds into
    // NintendoDs: folding is only safe when both sets share a thumbnails repository, and MSX and
    // MSX2 are two separate ones, so a folded MSX2 title would 404 on every cover.
    [Display(Name = "MSX2")]
    [Description("Microsoft - MSX2")]
    Msx2
}

public static class ConsoleTypeExtensions
{
    // Route slug <-> ConsoleType. The slug is the stable public identifier in /v2/art/{platform}/...,
    // so these strings are API surface: do not rename them without versioning the route.
    private static readonly Dictionary<ConsoleType, string> Slugs = new()
    {
        [ConsoleType.GameBoy] = "gb",
        [ConsoleType.GameBoyColor] = "gbc",
        [ConsoleType.GameBoyAdvance] = "gba",
        [ConsoleType.NintendoDs] = "nds",
        [ConsoleType.NintendoDsi] = "dsi",
        [ConsoleType.Nes] = "nes",
        [ConsoleType.Snes] = "snes",
        [ConsoleType.Nintendo64] = "n64",
        [ConsoleType.FamicomDiskSystem] = "fds",
        [ConsoleType.MegaDrive] = "md",
        [ConsoleType.MasterSystem] = "sms",
        [ConsoleType.GameGear] = "gg",
        [ConsoleType.PokemonMini] = "min",
        [ConsoleType.Sg1000] = "sg",
        [ConsoleType.PcEngine] = "pce",
        [ConsoleType.WonderSwan] = "ws",
        [ConsoleType.WonderSwanColor] = "wsc",
        [ConsoleType.NeoGeoPocket] = "ngp",
        [ConsoleType.NeoGeoPocketColor] = "ngpc",
        [ConsoleType.Atari2600] = "a26",
        [ConsoleType.Atari5200] = "a52",
        [ConsoleType.Atari7800] = "a78",
        [ConsoleType.ColecoVision] = "col",
        [ConsoleType.Intellivision] = "int",
        [ConsoleType.Msx] = "msx",
        [ConsoleType.Msx2] = "msx2"
    };

    // Every spelling a client could reasonably have learned from this API: the slug ("nds") and the
    // enum name as identify serialises it ("NintendoDs"), case-insensitive. Tolerant on purpose -
    // shipped clients cannot be fixed, so anything that ever parsed must keep parsing forever.
    private static readonly Dictionary<string, ConsoleType> ByRouteValue = BuildByRouteValue();

    private static Dictionary<string, ConsoleType> BuildByRouteValue()
    {
        var map = new Dictionary<string, ConsoleType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, slug) in Slugs)
        {
            map[slug] = type;
            map[type.ToString()] = type;
        }

        return map;
    }

    private static readonly Dictionary<ConsoleType, string> Descriptions =
        Enum.GetValues<ConsoleType>().ToDictionary(v => v, v =>
            typeof(ConsoleType).GetField(v.ToString())?
                .GetCustomAttributes(typeof(DescriptionAttribute), false)
                .Cast<DescriptionAttribute>().FirstOrDefault()?.Description ?? v.ToString());

    // The short label, kept next to the console it names for the same reason Description is. The two
    // are not interchangeable: Description is the No-Intro / libretro repository name and is wire
    // data, so "Nintendo - Nintendo Entertainment System" is what a lookup needs and "NES" is what a
    // reader needs. This lived in the browser client as PLATFORM_LABELS until it fell fourteen
    // consoles behind this enum.
    private static readonly Dictionary<ConsoleType, string> Names =
        Enum.GetValues<ConsoleType>().ToDictionary(v => v, v =>
            typeof(ConsoleType).GetField(v.ToString())?
                .GetCustomAttributes(typeof(DisplayAttribute), false)
                .Cast<DisplayAttribute>().FirstOrDefault()?.Name ?? v.ToString());

    public static string Slug(this ConsoleType type)
    {
        return Slugs.GetValueOrDefault(type, "unknown");
    }

    /// <summary>Parses a platform route segment or query hint: slug or enum name, case-insensitive.</summary>
    public static ConsoleType FromRouteValue(string? value)
    {
        return value is not null && ByRouteValue.TryGetValue(value, out var t) ? t : ConsoleType.Unknown;
    }

    /// <summary>Short human label, e.g. "Nintendo DS".</summary>
    public static string Name(this ConsoleType type)
    {
        return Names[type];
    }

    /// <summary>No-Intro / libretro display name, e.g. "Nintendo - Game Boy".</summary>
    public static string Description(this ConsoleType type)
    {
        return Descriptions[type];
    }

    /// <summary>libretro-thumbnails repository name, e.g. "Nintendo_-_Game_Boy".</summary>
    public static string LibRetroRepository(this ConsoleType type)
    {
        return type.Description().Replace(' ', '_');
    }
}
