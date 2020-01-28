using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwilightBoxart.Helpers;

namespace TwilightBoxart.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            ConsoleEx.WriteGreenLine(BoxartConfig.Credits);
            Console.WriteLine();

            var config = new BoxartConfig();
            try
            {
                config.Load();
            }
            catch { Console.WriteLine("Could not load TwilightBoxart.ini - using defaults."); }

            if (!config.DisableUpdates)
            {
                try
                {
                    var update = GithubClient.GetNewRelease(BoxartConfig.Repository, BoxartConfig.Version);

                    if (update != null)
                    {
                        ConsoleEx.WriteGreenLine(update.UpdateText);

                        if (ConsoleEx.YesNoMenu())
                        {
                            OSHelper.OpenBrowser(BoxartConfig.RepositoryReleasesUrl);
                        }

                        Console.WriteLine();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Could not check for updates: {e.Message}");
                }
            }

            if (string.IsNullOrEmpty(config.SdRoot))
            {
                var allDrives = new List<DriveInfo>();
                try
                {
                    allDrives = DriveInfo.GetDrives().Where(c => c.DriveType == DriveType.Removable).ToList();
                }
                catch { }
                
                var choice = FileHelper.GetCurrentDirectory();
                if (allDrives.Count > 0)
                {
                    var choices = allDrives.Select(c => c.Name).ToList();
                    choices.Add("Current Directory");
                    var i = ConsoleEx.MenuIndex("Select your SD location: ", true, choices.ToArray());
                    if (i != choices.Count - 1)
                    {
                        choice = allDrives[i].RootDirectory.FullName;
                    }
                }
                else
                {
                    Console.WriteLine("No settings or drives found. Using current directory.");
                }

                config.SdRoot = choice;
            }

            var boxArtPath = config.GetBoxartPath();
            ConsoleEx.WriteGreenLine("Loaded settings:");
            Console.WriteLine("SDRoot / Roms location: \t" + config.SdRoot);
            Console.WriteLine("BoxArt location: \t\t" + boxArtPath);
            Console.WriteLine();
            if (!ConsoleEx.YesNoMenu("Is this OK?"))
            {
                Console.WriteLine("Please edit TwilightBoxart.ini or insert your SD card and try again.");
                return;
            }
            Console.WriteLine();
            
            var progress = new Progress<string>(Console.WriteLine);
            var crawler = new BoxartCrawler(progress);
            crawler.InitializeDb();
            crawler.DownloadArt(config);
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
