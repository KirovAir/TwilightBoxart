using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TwilightBoxart
{
    public class BoxartCrawler
    {
        private readonly IProgress<string> _progress;
        private CancellationTokenSource _cancelToken;
        private readonly HttpClientHandler _handler = new HttpClientHandler
        {
            // This is BAD practice but needed for some users. Don't use this for production apps. ;)
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        private readonly HttpClient _httpClient;

        public BoxartCrawler(IProgress<string> progress = null)
        {
            _httpClient = new HttpClient(_handler);
            _progress = progress;
        }

        public async Task DownloadArt(IAppConfig downloadConfig)
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
                    if (!BoxartConfig.SupportedFiles.Contains(ext))
                        continue;


                    var fileName = Path.GetFileName(romFile);
                    var targetArtFile = Path.Combine(downloadConfig.BoxartPath, fileName + ".png");
                    if (!downloadConfig.OverwriteExisting && File.Exists(targetArtFile))
                    {
                        // We already have it.
                        _progress?.Report($"Skipping {fileName}.. (We already have it)");
                        continue;
                    }

                    try
                    {
                        _progress?.Report($"Searching art for {fileName}.. ");

                        var meta = FileMetaData.FromFile(romFile, BoxartConfig.SupportedFiles);

                        var formContent = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("Filename", fileName),
                            new KeyValuePair<string, string>("Sha1", meta.Sha1),
                            new KeyValuePair<string, string>("Header", Convert.ToBase64String(meta.Header)),
                            new KeyValuePair<string, string>("BoxartWidth", downloadConfig.BoxartWidth.ToString()),
                            new KeyValuePair<string, string>("BoxartHeight", downloadConfig.BoxartHeight.ToString()),
                            new KeyValuePair<string, string>("KeepAspectRatio", downloadConfig.KeepAspectRatio.ToString()),
                            new KeyValuePair<string, string>("BoxartBorderStyle", downloadConfig.BoxartBorderStyle.ToString()),
                            new KeyValuePair<string, string>("BoxartBorderColor", "0x" + downloadConfig.BoxartBorderColor.ToString("X")),
                            new KeyValuePair<string, string>("BoxartBorderThickness", downloadConfig.BoxartBorderThickness.ToString())
                        });

                        var result = await _httpClient.PostAsync(BoxartConfig.ApiUrl, formContent);
                        if (result.StatusCode == HttpStatusCode.NotFound)
                        {
                            _progress?.Report("Could not find boxart. (404)");
                            continue;
                        }
                        result.EnsureSuccessStatusCode();

                        // We got it!
                        Directory.CreateDirectory(Path.GetDirectoryName(targetArtFile));
                        using (var fs = new FileStream(targetArtFile, FileMode.Create))
                        {
                            await result.Content.CopyToAsync(fs);
                        }

                        _progress?.Report("Got it!");
                    }
                    catch (Exception e)
                    {
                        var fullError = e.Message;
                        while (e.InnerException != null)
                        {
                            e = e.InnerException;
                            fullError += " " + e.Message;
                        }
                        _progress?.Report(fullError);
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