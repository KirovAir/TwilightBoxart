using System;
using System.IO;
using KirovAir.Core.ConsoleApp;
using KirovAir.Core.Utilities;

namespace TwilightBoxart.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            ConsoleEx.WriteGreenLine("TwilightBoxart - Created by KirovAir.");
            Console.WriteLine("Loads of love to the devs of TwilightMenu++, LibRetro and the maintainers of the No-Intro DB.");
            Console.WriteLine();

            var config = new Config();
            try
            {
                config.Load("TwilightBoxart.ini");
            }
            catch { Console.WriteLine("Could not load TwilightBoxart.ini - using defaults."); }

            if (string.IsNullOrEmpty(config.SdRoot))
            {
                config.SdRoot = FileHelper.GetCurrentDirectory();
            }

            var boxArtPath = Path.Combine(config.SdRoot, config.BoxArtPath);
            ConsoleEx.WriteGreenLine("Loaded settings:");
            Console.WriteLine("SDRoot / Roms location: \t" + config.SdRoot);
            Console.WriteLine("BoxArt location: \t\t" + boxArtPath);
            Console.WriteLine();
            if (!ConsoleEx.YesNoMenu("Is this OK?"))
            {
                return;
            }
            Console.WriteLine();

            var crawler = new BoxartCrawler();
            var progress = new Progress<string>(Console.WriteLine);
            crawler.InitializeDb(progress);
            crawler.DownloadArt(config.SdRoot, boxArtPath, progress);
        }

        // Todo: Implement as CLI and add Progress<> to MetaCrawler.
        //public void AddMeta()
        //{
        //    Console.WriteLine("This program will generate a sha1/title/id DB based on the path:");
        //    Console.WriteLine("");
        //    if (!ConsoleEx.YesNoMenu("Start now?"))
        //        return;
        //    var crawler = new LocalMetaCrawler("", "");
        //    crawler.Go();
        //    Console.WriteLine("Done.");
        //}
    }
}
