using System;
using KirovAir.Core.ConsoleApp;
using TwilightBoxart.Crawlers.LocalMeta;

namespace TwilightDbGen
{
    class Program
    {
        private static readonly Config Config = new Config();

        static void Main(string[] args)
        {
            Config.Load("TwilightDbGen.ini");
            Console.WriteLine("This program will generate a sha1/title/id DB based on the path:");
            Console.WriteLine(Config.ScanDir);
            if (!ConsoleEx.YesNoMenu("Start now?"))
                return;
            var crawler = new LocalMetaCrawler(Config.ScanDir, Config.OutputFile);
            crawler.Go();
            Console.WriteLine("Done.");
        }
    }
}
