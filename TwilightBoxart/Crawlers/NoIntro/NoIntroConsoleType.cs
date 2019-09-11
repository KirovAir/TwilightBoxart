using System.ComponentModel;
using SQLite;

namespace TwilightBoxart.Crawlers.NoIntro
{
    [StoreAsText]
    public enum NoIntroConsoleType
    {
        [Description("Nintendo - Game Boy")]
        GameBoy,

        [Description("Nintendo - Game Boy Color")]
        GameBoyColor,

        [Description("Nintendo - Game Boy Advance")]
        GameBoyAdvance,

        [Description("Nintendo - Nintendo DS")]
        NintendoDs,

        [Description("Nintendo - Nintendo DS (Download Play)")]
        NintendoDsDownloadPlay,

        [Description("Nintendo - Nintendo DSi")]
        NintendoDSi,

        [Description("Nintendo - Nintendo DSi (Digital)")]
        NintendoDSiDigital,

        [Description("Nintendo - Nintendo Entertainment System")]
        NintendoEntertainmentSystem,

        [Description("Nintendo - Super Nintendo Entertainment System")]
        SuperNintendoEntertainmentSystem
    }
}
