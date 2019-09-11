using System;
using System.IO;
using System.Linq;
using KirovAir.Core.ConsoleApp;
using KirovAir.Core.Utilities;
using TwilightBoxart.Data;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    class Program
    {
        private static readonly Config Config = new Config();
        private static RomDatabase _romDb;

        static void Main(string[] args)
        {
            ConsoleEx.WriteGreenLine("TwilightBoxart - Created by KirovAir.");
            Console.WriteLine("Loads of love to the devs of TwilightMenu++, LibRetro and the maintainers of the No-Intro DB.");
            Console.WriteLine();
            try
            {
                Config.Load("TwilightBoxart.ini");
            }
            catch { Console.WriteLine("Could not load TwilightBoxart.ini - using defaults."); }

            if (string.IsNullOrEmpty(Config.SdRoot))
            {
                Config.SdRoot = FileHelper.GetCurrentDirectory();
            }
            var romsPath = Path.Combine(Config.SdRoot, Config.RomsDir);
            var boxArtPath = Path.Combine(Config.SdRoot, Config.BoxArtDir);

            _romDb = new RomDatabase(Path.Combine(FileHelper.GetCurrentDirectory(), "NoIntro.db"));
            Console.WriteLine();
            ConsoleEx.WriteGreenLine("Loaded settings:");
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

        private static void DownloadArt(string romsPath, string boxArtPath)
        {
            foreach (var romFile in Directory.EnumerateFiles(romsPath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(romFile).ToLower();
                if (!Config.ExtensionMapping.Keys.Contains(ext))
                    continue;

                var targetArtFile = Path.Combine(boxArtPath, Path.GetFileName(romFile) + ".png");
                if (File.Exists(targetArtFile))
                {  // We already have it.
                    ConsoleEx.WriteGreenLine($"Skipping {Path.GetFileName(romFile)}.. (We already have it)");
                    continue;
                }

                try
                {
                    Console.Write($"Searching art for {Path.GetFileName(romFile)}.. ");
                    var rom = Rom.FromFile(romFile);
                    _romDb.AddMetadata(rom);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetArtFile));
                    rom.DownloadBoxArt(targetArtFile);
                    Console.WriteLine("Done!");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Something bad happened: " + e.Message);
                }
            }
        }
    }
}
