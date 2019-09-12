using System.ComponentModel;
using SQLite;

namespace TwilightBoxart.Models.Base
{
    /// <summary>
    /// The Console Types. The description corresponds with the NoIntro DBs.
    /// </summary>
    [StoreAsText]
    public enum ConsoleType
    {
        Unknown,
        [Description("Nintendo - Game Boy")]
        GameBoy,
        [Description("Nintendo - Game Boy Color")]
        GameBoyColor,
        [Description("Nintendo - Game Boy Advance")]
        GameBoyAdvance,
        [Description("Nintendo - Nintendo DS")]
        NintendoDS,
        [Description("Nintendo - Nintendo DS (Download Play)")]
        NintendoDSDownloadPlay,
        [Description("Nintendo - Nintendo DSi")]
        NintendoDSi,
        [Description("Nintendo - Nintendo DSi (Digital)")]
        NintendoDSiDigital,
        [Description("Nintendo - Nintendo Entertainment System")]
        NintendoEntertainmentSystem,
        [Description("Nintendo - Super Nintendo Entertainment System")]
        SuperNintendoEntertainmentSystem,
        [Description("Sega - Mega Drive - Genesis")]
        SegaGenesis,
        [Description("Sega - Master System - Mark III")]
        SegaMasterSystem,
        [Description("Sega - Game Gear")]
        SegaGameGear
    }
}