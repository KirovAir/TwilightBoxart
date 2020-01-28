using System;
using System.Collections.Generic;
using System.IO;
using TwilightBoxart.Helpers;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public interface IAppConfig : IBoxartConfig
    {
        string SdRoot { get; set; }
        string BoxartPath { get; set; }
        bool OverwriteExisting { get; set; }
    }

    public interface IRequestModel : IBoxartConfig
    {
        string Sha1 { get; set; }
        string Filename { get; set; }
        byte[] Header { get; set; }
    }

    public interface IBoxartConfig
    {
        string CachePath { get; set; }
        int BoxartWidth { get; set; }
        int BoxartHeight { get; set; }
        bool KeepAspectRatio { get; set; }
        BoxartBorderStyle BoxartBorderStyle { get; set; }
        int BoxartBorderThickness { get; set; }
        uint BoxartBorderColor { get; set; }
    }

    public class BoxartConfig : IniSettings, IAppConfig
    {
        public string SdRoot { get; set; } = "";
        public string BoxartPath { get; set; } = @"{sdroot}\_nds\TWiLightMenu\boxart";
        public string CachePath { get; set; }
        public int BoxartWidth { get; set; } = 128;
        public int BoxartHeight { get; set; } = 115;
        public bool KeepAspectRatio { get; set; } = true;
        public bool OverwriteExisting { get; set; } = false;
        public BoxartBorderStyle BoxartBorderStyle { get; set; }
        public int BoxartBorderThickness { get; set; }
        public uint BoxartBorderColor { get; set; }
        public bool DisableUpdates { get; set; } = false;
        
        public const string MagicDir = "_nds";
        public const string FileName = "TwilightBoxart.ini";
        public const string Repository = "KirovAir/TwilightBoxart";

        public static Version Version = new Version(0, 7, 0);
        public static string Credits = "TwilightBoxart - Created by KirovAir." + Environment.NewLine + "Loads of love to the devs of TwilightMenu++, LibRetro, GameTDB and the maintainers of the No-Intro DB.";

        public static string RepositoryUrl = $"https://github.com/{Repository}";
        public static string RepositoryReleasesUrl = $"https://github.com/{Repository}/releases";
        public static string NoIntroDbUrl = $"{RepositoryUrl}/raw/master/TwilightBoxart/NoIntro.db";
        public static string DsiWareBoxartUrl = $"{RepositoryUrl}/raw/master/img/dsiware.jpg";

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

            if (root.Contains(Path.DirectorySeparatorChar.ToString()))
            {
                try
                {
                    var split = root.Split(Path.DirectorySeparatorChar);
                    var tmpReplace = "";
                    for (var i = split.Length; i-- > 0;)
                    {
                        tmpReplace = split[i] + Path.DirectorySeparatorChar + tmpReplace;
                        tmpReplace = tmpReplace.TrimEnd(Path.DirectorySeparatorChar);

                        // Remove where we are.
                        var place = root.LastIndexOf(tmpReplace);
                        if (place == -1)
                            break;
                        var correctRoot = root.Remove(place, tmpReplace.Length);

                        if (Directory.Exists(Path.Combine(correctRoot, MagicDir)))
                        {
                            root = correctRoot;
                            break;
                        }
                    }
                }
                catch { }
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
            {".sgb", ConsoleType.GameBoy},
            {".gbc", ConsoleType.GameBoyColor},
            {".gba", ConsoleType.GameBoyAdvance},
            {".nds", ConsoleType.NintendoDS},
            {".ds", ConsoleType.NintendoDS},
            {".dsi", ConsoleType.NintendoDSi},
            {".gg", ConsoleType.SegaGameGear},
            {".gen", ConsoleType.SegaGenesis},
            {".sms", ConsoleType.SegaMasterSystem},
            {".fds", ConsoleType.FamicomDiskSystem},
            {".zip", ConsoleType.Unknown }
        };
    }
}