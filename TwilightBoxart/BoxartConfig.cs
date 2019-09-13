using System;
using System.Collections.Generic;
using KirovAir.Core.Config;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public class BoxartConfig : IniSettings
    {
        public string SdRoot { get; set; } = "";
        public string BoxArtPath { get; set; } = @"_nds\TWiLightMenu\boxart";
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

        public static Dictionary<ConsoleType, ConsoleConfig> Consoles = new Dictionary<ConsoleType, ConsoleConfig>
        {
            {
                ConsoleType.NintendoEntertainmentSystem,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_Entertainment_System/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.SuperNintendoEntertainmentSystem,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Super_Nintendo_Entertainment_System/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.GameBoy,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.GameBoyColor,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Color/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.GameBoyAdvance,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.NintendoDS,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_DS/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.NintendoDSi,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_DSi/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.SegaGameGear,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Sega_-_Game_Gear/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.SegaGenesis,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Sega_-_Mega_Drive_-_Genesis/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.SegaMasterSystem,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Sega_-_Master_System_-_Mark_III/raw/master/Named_Boxarts/"
                }
            }
        };
    }

    public class ConsoleConfig
    {
        public static ConsoleConfig Get(ConsoleType type)
        {
            if (!BoxartConfig.Consoles.ContainsKey(type))
            {
                throw new Exception("Could not find config for " + type);
            }
            return BoxartConfig.Consoles[type];
        }

        public string ContentUrl { get; set; }
    }
}