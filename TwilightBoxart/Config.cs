using System;
using KirovAir.Core.Config;
using System.Collections.Generic;
using TwilightBoxart.Crawlers.NoIntro;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public class Config : IniSettings
    {
        public string SdRoot { get; set; } = "";
        public string RomsDir { get; set; } = "";
        public string BoxArtDir { get; set; } = @"_nds\TWiLightMenu\boxart";

        public static Dictionary<NoIntroConsoleType, ConsoleType> NoIntroDbMapping = new Dictionary<NoIntroConsoleType, ConsoleType>
        {
            {NoIntroConsoleType.GameBoy, ConsoleType.Gb},
            {NoIntroConsoleType.GameBoyColor, ConsoleType.Gbc},
            {NoIntroConsoleType.GameBoyAdvance, ConsoleType.Gba},
            {NoIntroConsoleType.NintendoDs, ConsoleType.Nds},
            {NoIntroConsoleType.NintendoDsDownloadPlay, ConsoleType.Nds},
            {NoIntroConsoleType.NintendoDSi, ConsoleType.Dsi},
            {NoIntroConsoleType.NintendoDSiDigital, ConsoleType.Dsi},
        };

        public static Dictionary<ConsoleType, ConsoleConfig> Consoles = new Dictionary<ConsoleType, ConsoleConfig>
        {
            {
                ConsoleType.Gb,
                new ConsoleConfig
                {
                    ContentUrl = "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy/b208f5466d7ea97b605bf4179de6534d988bc42c/Named_Boxarts/"
                }
            },
            {
                ConsoleType.Gbc,
                new ConsoleConfig
                {
                    ContentUrl = "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy_Color/a57dc041615f82e5f7a8d292dbc8c81a42b1f281/Named_Boxarts/"
                }
            },
            {
                ConsoleType.Gba,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.Nds,
                new ConsoleConfig
                {
                    ContentUrl = "https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_DS/raw/master/Named_Boxarts/"
                }
            },
            {
                ConsoleType.Dsi,
                new ConsoleConfig
                {
                    ContentUrl = "https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Nintendo_DSi/ca5aed91f8327158549077a438cdb0834063c61e/Named_Boxarts/"
                }
            }
        };
    }

    public class ConsoleConfig
    {
        public static ConsoleConfig Get(ConsoleType type)
        {
            if (!Config.Consoles.ContainsKey(type))
            {
                throw new Exception("Could not find config for " + type);
            }
            return Config.Consoles[type];
        }

        public string ContentUrl { get; set; }
    }
}
