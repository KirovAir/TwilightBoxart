using System;
using System.IO;
using System.Linq;
using KirovAir.Core.Utilities;
using TwilightBoxart.Data;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart
{
    public class BoxartCrawler
    {
        private static RomDatabase _romDb;

        public BoxartCrawler()
        {
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

            foreach (var romFile in Directory.EnumerateFiles(romsPath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(romFile).ToLower();
                if (!Config.ExtensionMapping.Keys.Contains(ext))
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
