using System.ComponentModel;

namespace TwilightBoxart.Core.Models;

/// <summary>
/// Supported console types. <see cref="DescriptionAttribute"/> matches the No-Intro / libretro-thumbnails
/// repository naming ("Nintendo - Game Boy" -> "Nintendo_-_Game_Boy"); <see cref="ConsoleTypeExtensions.Slug"/>
/// gives the short lowercase form used in API routes.
/// </summary>
public enum ConsoleType
{
    Unknown = 0,

    [Description("Nintendo - Game Boy")]
    GameBoy,

    [Description("Nintendo - Game Boy Color")]
    GameBoyColor,

    [Description("Nintendo - Game Boy Advance")]
    GameBoyAdvance,

    [Description("Nintendo - Nintendo DS")]
    NintendoDs,

    // NOT "Nintendo - Nintendo DSi (Digital)". That is DAT-o-MATIC's name for the DSiWare set, and it
    // is what the 2020 crawler used, but neither libretro repository answers to it: the thumbnails org
    // has no such repo (verified 404) and the libretro-database No-Intro mirror publishes the same set
    // as plain "Nintendo - Nintendo DSi". Since both consumers of this attribute are libretro, the
    // "(Digital)" spelling silently 404s every DSi cover.
    [Description("Nintendo - Nintendo DSi")]
    NintendoDsi,

    [Description("Nintendo - Nintendo Entertainment System")]
    Nes,

    [Description("Nintendo - Super Nintendo Entertainment System")]
    Snes,

    [Description("Nintendo - Nintendo 64")]
    Nintendo64,

    [Description("Nintendo - Family Computer Disk System")]
    FamicomDiskSystem,

    [Description("Sega - Mega Drive - Genesis")]
    MegaDrive,

    [Description("Sega - Master System - Mark III")]
    MasterSystem,

    [Description("Sega - Game Gear")]
    GameGear,

    [Description("Nintendo - Pokemon Mini")]
    PokemonMini,

    [Description("Sega - SG-1000")]
    Sg1000,

    [Description("NEC - PC Engine - TurboGrafx 16")]
    PcEngine,

    [Description("Bandai - WonderSwan")]
    WonderSwan,

    [Description("Bandai - WonderSwan Color")]
    WonderSwanColor,

    [Description("SNK - Neo Geo Pocket")]
    NeoGeoPocket,

    [Description("SNK - Neo Geo Pocket Color")]
    NeoGeoPocketColor,

    [Description("Atari - 2600")]
    Atari2600,

    [Description("Atari - 5200")]
    Atari5200,

    [Description("Atari - 7800")]
    Atari7800,

    [Description("Coleco - ColecoVision")]
    ColecoVision,

    [Description("Mattel - Intellivision")]
    Intellivision,

    [Description("Microsoft - MSX")]
    Msx,

    // No extension of its own: TWiLightMenu++ launches both MSX generations from .msx, so the
    // extension hint can only name one of them. That costs nothing, because the extension is only
    // consulted by the fuzzy-name rung: an MSX2 dump identified by CRC32 comes back carrying its
    // own console from the DAT. It must NOT be folded into Msx the way Download Play folds into
    // NintendoDs: folding is only safe when both sets share a thumbnails repository, and MSX and
    // MSX2 are two separate ones, so a folded MSX2 title would 404 on every cover.
    [Description("Microsoft - MSX2")]
    Msx2,
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
        [ConsoleType.Msx2] = "msx2",
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

    public static string Slug(this ConsoleType type) => Slugs.GetValueOrDefault(type, "unknown");

    /// <summary>Parses a platform route segment or query hint: slug or enum name, case-insensitive.</summary>
    public static ConsoleType FromRouteValue(string? value) =>
        value is not null && ByRouteValue.TryGetValue(value, out var t) ? t : ConsoleType.Unknown;

    /// <summary>No-Intro / libretro display name, e.g. "Nintendo - Game Boy".</summary>
    public static string Description(this ConsoleType type) => Descriptions[type];

    /// <summary>libretro-thumbnails repository name, e.g. "Nintendo_-_Game_Boy".</summary>
    public static string LibRetroRepository(this ConsoleType type) => type.Description().Replace(' ', '_');
}
