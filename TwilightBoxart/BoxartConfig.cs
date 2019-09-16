using System.Collections.Generic;
using KirovAir.Core.Config;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public class BoxartConfig : IniSettings
    {
        public string SdRoot { get; set; } = "";
        public string BoxartPath { get; set; } = @"_nds\TWiLightMenu\boxart";
        public int BoxartWidth { get; set; } = 128;
        public int BoxartHeight { get; set; } = 115;

        // Used as backup mapping.
        public static readonly Dictionary<string, ConsoleType> ExtensionMapping = new Dictionary<string, ConsoleType>
        {
            {".nes", ConsoleType.NintendoEntertainmentSystem},
            {".sfc", ConsoleType.SuperNintendoEntertainmentSystem},
            {".smc", ConsoleType.SuperNintendoEntertainmentSystem},
            {".snes", ConsoleType.SuperNintendoEntertainmentSystem},
            {".gb", ConsoleType.GameBoy},
            {".gbc", ConsoleType.GameBoyColor},
            {".gba", ConsoleType.GameBoyAdvance},
            {".nds", ConsoleType.NintendoDS},
            {".ds", ConsoleType.NintendoDS},
            {".dsi", ConsoleType.NintendoDSi},
            {".gg", ConsoleType.SegaGameGear},
            {".gen", ConsoleType.SegaGenesis},
            {".sms", ConsoleType.SegaMasterSystem}
        };

        /// <summary>
        /// Mapping to merge some ConsoleTypes in the DB.
        /// </summary>
        public static Dictionary<ConsoleType, ConsoleType> NoIntroDbMapping = new Dictionary<ConsoleType, ConsoleType>
        {
            {ConsoleType.NintendoEntertainmentSystem, ConsoleType.NintendoEntertainmentSystem},
            {ConsoleType.SuperNintendoEntertainmentSystem, ConsoleType.SuperNintendoEntertainmentSystem},

            {ConsoleType.GameBoy, ConsoleType.GameBoy},
            {ConsoleType.GameBoyColor, ConsoleType.GameBoyColor},
            {ConsoleType.GameBoyAdvance, ConsoleType.GameBoyAdvance},

            {ConsoleType.NintendoDS, ConsoleType.NintendoDS},
            {ConsoleType.NintendoDSDownloadPlay, ConsoleType.NintendoDS},
            {ConsoleType.NintendoDSi, ConsoleType.NintendoDSi},
            {ConsoleType.NintendoDSiDigital, ConsoleType.NintendoDSi},

            {ConsoleType.SegaGameGear, ConsoleType.SegaGameGear},
            {ConsoleType.SegaGenesis, ConsoleType.SegaGenesis},
            {ConsoleType.SegaMasterSystem, ConsoleType.SegaMasterSystem}
        };
    }
}