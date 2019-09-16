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
        private readonly IProgress<string> _progress;
        private static RomDatabase _romDb;

        public BoxartCrawler(IProgress<string> progress = null)
        {
            _progress = progress;
            _romDb = new RomDatabase(Path.Combine(FileHelper.GetCurrentDirectory(), "NoIntro.db"));
        }

        public void InitializeDb()
        {
            _romDb.Initialize(_progress);
        }

        public void DownloadArt(string romsPath, string boxArtPath, int defaultWidth, int defaultHeight)
        {
            _progress?.Report($"Scanning {romsPath}..");

            try
            {
                if (!Directory.Exists(romsPath))
                {
                    _progress?.Report($"Could not open {romsPath}.");
                    return;
                }

                var downloader = new ImgDownloader(defaultWidth, defaultHeight); // Use 1 width/height for all. Maybe we want different sizes per type in the future.

                foreach (var romFile in Directory.EnumerateFiles(romsPath, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(romFile).ToLower();
                    if (!BoxartConfig.ExtensionMapping.Keys.Contains(ext))
                        continue;

                    var targetArtFile = Path.Combine(boxArtPath, Path.GetFileName(romFile) + ".png");
                    if (File.Exists(targetArtFile))
                    {
                        // We already have it.
                        _progress?.Report($"Skipping {Path.GetFileName(romFile)}.. (We already have it)");
                        continue;
                    }

                    try
                    {
                        _progress?.Report($"Searching art for {Path.GetFileName(romFile)}.. ");
                        var rom = Rom.FromFile(romFile);
                        rom.SetDownloader(downloader);
                        _romDb.AddMetadata(rom);

                        Directory.CreateDirectory(Path.GetDirectoryName(targetArtFile));
                        rom.DownloadBoxArt(targetArtFile);
                        _progress?.Report("Got it!");
                    }
                    catch (Exception e)
                    {
                        _progress?.Report("Something bad happened: " + e.Message);
                    }
                }
                
                _progress?.Report("Finished scan.");
            }
            catch (Exception e)
            {
                _progress?.Report("Unhandled exception occured! " + e);
            }
        }
    }
}
