using System;
using System.IO;
using System.Linq;
using System.Net;
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
            // Disable all SSL cert pinning for now as users have reported problems with github.
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            _progress = progress;
            _romDb = new RomDatabase(Path.Combine(FileHelper.GetCurrentDirectory(), "NoIntro.db"));
        }

        public void InitializeDb()
        {
            _romDb.Initialize(_progress);
        }

        public void DownloadArt(string romsPath, string boxArtPath, int defaultWidth, int defaultHeight, bool keepAspectRatio = true)
        {
            _progress?.Report($"Started! Using width: {defaultWidth} height: {defaultHeight}. Scanning {romsPath}..");

            try
            {
                if (!Directory.Exists(romsPath))
                {
                    _progress?.Report($"Could not open {romsPath}.");
                    return;
                }

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
                        _romDb.AddMetadata(rom);

                        var downloader = new ImgDownloader(defaultWidth, defaultHeight, keepAspectRatio);
                        rom.SetDownloader(downloader);

                        Directory.CreateDirectory(Path.GetDirectoryName(targetArtFile));
                        rom.DownloadBoxArt(targetArtFile);
                        _progress?.Report("Got it!");
                    }
                    catch (NoMatchException ex)
                    {
                        _progress?.Report(ex.Message);
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
