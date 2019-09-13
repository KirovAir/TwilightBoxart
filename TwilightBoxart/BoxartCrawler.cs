using System;
using System.IO;
using System.Linq;
using KirovAir.Core.Utilities;
using TwilightBoxart.Data;
using TwilightBoxart.Helpers;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public class BoxartCrawler
    {
        private readonly BoxartConfig _config;
        private static RomDatabase _romDb;

        public BoxartCrawler(BoxartConfig config)
        {
            _config = config;
            _romDb = new RomDatabase(Path.Combine(FileHelper.GetCurrentDirectory(), "NoIntro.db"));
        }

        public void InitializeDb(IProgress<string> progress = null)
        {
            _romDb.Initialize(progress);
        }
        public void DownloadArt(string romsPath, string boxArtPath, IProgress<string> progress = null)
        {
            if (!Directory.Exists(romsPath))
            {
                progress?.Report($"Could not open {romsPath}. Please check TwilightBoxart.ini");
                return;
            }

            var downloader = new ImgDownloader(_config.BoxartWidth, _config.BoxartHeight); // Use 1 width/height for all. Maybe we want different sizes per type in the future.

            foreach (var romFile in Directory.EnumerateFiles(romsPath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(romFile).ToLower();
                if (!BoxartConfig.ExtensionMapping.Keys.Contains(ext))
                    continue;

                var targetArtFile = Path.Combine(boxArtPath, Path.GetFileName(romFile) + ".png");
                if (File.Exists(targetArtFile))
                {  // We already have it.
                    progress?.Report($"Skipping {Path.GetFileName(romFile)}.. (We already have it)");
                    continue;
                }

                try
                {
                    progress?.Report($"Searching art for {Path.GetFileName(romFile)}.. ");
                    var rom = Rom.FromFile(romFile);
                    rom.SetDownloader(downloader);
                    _romDb.AddMetadata(rom);

                    Directory.CreateDirectory(Path.GetDirectoryName(targetArtFile));
                    rom.DownloadBoxArt(targetArtFile);
                    progress?.Report("Done!");
                }
                catch (Exception e)
                {
                    progress?.Report("Something bad happened: " + e.Message);
                }
            }
        }
    }
}
