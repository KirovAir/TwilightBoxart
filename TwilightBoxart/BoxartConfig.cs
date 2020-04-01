using System;
using System.Collections.Generic;
using System.IO;
using TwilightBoxart.Helpers;

namespace TwilightBoxart
{
    public interface ISharedConfig
    {
        int BoxartWidth { get; set; }
        int BoxartHeight { get; set; }
        bool KeepAspectRatio { get; set; }
        BoxartBorderStyle BoxartBorderStyle { get; set; }
        int BoxartBorderThickness { get; set; }
        uint BoxartBorderColor { get; set; }
    }

    public interface IAppConfig : ISharedConfig
    {
        string SdRoot { get; set; }
        string BoxartPath { get; set; }
        string SettingsPath { get; set; }
        bool OverwriteExisting { get; set; }
    }

    public class BoxartConfig : IniSettings, IAppConfig
    {
        public string SdRoot { get; set; } = "";
        public string BoxartPath { get; set; } = @"{sdroot}\_nds\TWiLightMenu\boxart";
        public string SettingsPath { get; set; } = @"{sdroot}\_nds\TWiLightMenu\settings.ini";
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

        public static string ApiUrl = "https://boxart.kirovair.com/api";
        public static string RepositoryUrl = $"https://github.com/{Repository}";
        public static string RepositoryReleasesUrl = $"https://github.com/{Repository}/releases";

        public void Load()
        {
            Load(FileName);
        }

        public string GetCorrectBoxartPath(string root = "")
        {
            return GetCorrectPath(BoxartPath, root);
        }

        public string GetCorrectSettingsIniPath(string root = "")
        {
            return GetCorrectPath(SettingsPath, root);
        }

        public string GetCorrectPath(string pathMask, string root = "")
        {
            if (root == "")
            {
                root = SdRoot;
            }

            if (!pathMask.StartsWith("{sdroot}"))
            {
                return pathMask;
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

            return Path.Combine(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, pathMask.Replace("{sdroot}", "").TrimStart(Path.DirectorySeparatorChar));
        }

        public static readonly List<string> SupportedFiles = new List<string>
        {
            ".nes",
            ".sfc",
            ".smc",
            ".snes",
            ".gb",
            ".sgb",
            ".gbc",
            ".gba",
            ".nds",
            ".ds",
            ".dsi",
            ".gg",
            ".gen",
            ".sms",
            ".fds",
            ".zip"
        };
    }
}