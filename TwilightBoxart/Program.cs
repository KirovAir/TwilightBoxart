using System;
using System.IO;
using TwilightBoxart.Models;
using System.Linq;
using KirovAir.Core.ConsoleApp;
using KirovAir.Core.Utilities;
using TwilightBoxart.Downloaders.LibRetro;

namespace TwilightBoxart
{
    class Program
    {
        private static readonly string[] SupportedExtensions = { ".dsi", ".nds", ".gb", ".gbc", ".gba" };
        private static readonly Config Config = new Config();

        static void Main(string[] args)
        {
            var dl = new LibRetroArtDownloader(@"https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/master/Nintendo%20-%20Game%20Boy%20Advance.dat",
                                                "https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/tree/master/Named_Boxarts");
                      


            try
            {
                Config.Load("TwilightBoxart.ini");
            } catch { Console.WriteLine("Could not load TwilightBoxart.ini - using defaults."); }

            if (string.IsNullOrEmpty(Config.SdRoot))
            {
                Config.SdRoot = FileHelper.GetCurrentDirectory();
            }
            var romsPath = Path.Combine(Config.SdRoot, Config.RomsDir);
            var boxArtPath = Path.Combine(Config.SdRoot, Config.BoxArtDir);

            Console.WriteLine("SDRoot: " + Config.SdRoot);
            Console.WriteLine("Roms location: " + romsPath);
            Console.WriteLine("BoxArt location: " + boxArtPath);
            Console.WriteLine();

            if (!ConsoleEx.YesNoMenu("Is this OK?"))
            {
                return;
            }
            Console.WriteLine();

            if (!Directory.Exists(romsPath))
            {
                ConsoleEx.WriteRedLine($"Could not open {romsPath}. Please check TwilightBoxart.ini");
            }
            else
            {
                DownloadArt(romsPath, boxArtPath);
            }
            Console.ReadKey();
        }

        static void DownloadArt(string romsPath, string boxArtPath)
        {
            foreach (var romFile in Directory.EnumerateFiles(romsPath, "*.*"))
            {
                var ext = Path.GetExtension(romFile).ToLower();
                if (!SupportedExtensions.Contains(ext))
                    continue;

                var targetArtFile = Path.Combine(boxArtPath, Path.GetFileNameWithoutExtension(romFile) + ".png");
                if (File.Exists(targetArtFile))
                {  // We already have it.
                    ConsoleEx.WriteGreenLine($"Skipping {Path.GetFileNameWithoutExtension(romFile)}.. (We already have it)");
                    continue;
                }

                try
                {
                    Console.Write($"Searching art for {Path.GetFileName(romFile)}.. ");
                    var rom = Rom.FromFile(romFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetArtFile));
                    rom.DownloadBoxArt(targetArtFile);
                    Console.WriteLine("Done!");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Something bad happened: " + e);
                }
            }
        }
    }
}
