using System;
using System.Collections.Generic;
using System.IO;
using KirovAir.Core.Config;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public class BoxartConfig : IniSettings
    {
        public string SdRoot { get; set; } = "";
        public string BoxartPath { get; set; } = @"{sdroot}\_nds\TWiLightMenu\boxart";
        public int BoxartWidth { get; set; } = 128;
        public int BoxartHeight { get; set; } = 115;

        public const string FileName = "TwilightBoxart.ini";
        public static string Credits = "TwilightBoxart - Created by KirovAir." + Environment.NewLine + "Loads of love to the devs of TwilightMenu++, LibRetro, GameTDB and the maintainers of the No-Intro DB.";

        public void Load()
        {
            Load(FileName);
        }


        public string GetBoxartPath(string root = "")
        {
            if (root == "")
            {
                root = SdRoot;
            }

            if (!BoxartPath.StartsWith("{sdroot}"))
            {
                return BoxartPath;
            }

            return Path.Combine(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, BoxartPath.Replace("{sdroot}", "").TrimStart(Path.DirectorySeparatorChar));
        }

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