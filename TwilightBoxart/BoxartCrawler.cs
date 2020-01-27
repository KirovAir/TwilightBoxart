using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
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
        private CancellationTokenSource _cancelToken;

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

        public void DownloadArt(IBoxartConfig downloadConfig)
        {
            _cancelToken = new CancellationTokenSource();
            _progress?.Report($"Started! Using width: {downloadConfig.BoxartWidth} height: {downloadConfig.BoxartHeight}. Scanning {downloadConfig.SdRoot}..");

            try
            {
                if (!Directory.Exists(downloadConfig.SdRoot))
                {
                    _progress?.Report($"Could not open {downloadConfig.SdRoot}.");
                    return;
                }

                foreach (var romFile in Directory.EnumerateFiles(downloadConfig.SdRoot, "*.*", SearchOption.AllDirectories))
                {
                    if (_cancelToken.IsCancellationRequested)
                    {
                        _progress?.Report("Stopped by user.");
                        break;
                    }

                    var ext = Path.GetExtension(romFile).ToLower();
                    if (!BoxartConfig.ExtensionMapping.Keys.Contains(ext))
                        continue;

                    var targetArtFile = Path.Combine(downloadConfig.BoxartPath, Path.GetFileName(romFile) + ".png");
                    if (!downloadConfig.OverwriteExisting && File.Exists(targetArtFile))
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

                        var downloader = new ImgDownloader(downloadConfig);
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

        public void Stop()
        {
            _cancelToken?.Cancel();
        }
    }
}