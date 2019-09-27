using KirovAir.Core.Extensions;
using System;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class NdsRom : LibRetroRom
    {
        public override ConsoleType ConsoleType => ConsoleType.NintendoDS;
        public char RegionId { get; set; }

        public NdsRom(byte[] header)
        {
            Title = header.GetString(0, 12);
            TitleId = header.GetString(12, 4);
            RegionId = (char)header[15];
        }

        public override void DownloadBoxArt(string targetFile)
        {
            try
            {
                var region = GetUrlRegion(); // Try correct region
                try
                {
                    DownloadAndResize(region, targetFile);
                }
                catch (Exception e)
                {
                    if (region != "EN" && e.Message.Contains("404")) // Finally, try EN region.
                    {
                        DownloadAndResize("EN", targetFile);
                        return;
                    }

                    throw;
                }
            }
            catch
            {
                base.DownloadBoxArt(targetFile);
            }
        }

        private static readonly string[] Qualities = { "HQ", "M", "S" };
        private static readonly string[] Extensions = { "jpg", "jpg", "png" };
        private void DownloadAndResize(string region, string targetFile)
        {
            var lastException = new Exception("Unable to download region " + region);
            for (var i = 0; i < Qualities.Length; i++)
            {
                var quality = Qualities[i];
                var extension = Extensions[i];
                var url = $"https://art.gametdb.com/ds/cover{quality}/{region}/{TitleId}.{extension}";

                try
                {
                    ImgDownloader.DownloadAndResize(url, targetFile);
                    return; // Success.
                }
                catch (Exception e)
                {
                    lastException = e;
                }
            }

            throw lastException;
        }

        private string GetUrlRegion()
        {
            var region = "EN";
            switch (RegionId)
            {
                case 'E':
                case 'T':
                    region = "US";   // USA
                    break;
                case 'J':
                    region = "JA";   // Japanese
                    break;
                case 'K':
                    region = "KO";   // Korean
                    break;

                case 'O':           // USA/Europe
                    region = "EN";
                    break;
                case 'P':           // Europe


                case 'U':
                    // Alternate country code for Australia.
                    region = "EN";
                    break;

                // European country-specific localizations.
                case 'D':
                    region = "DE";   // German
                    break;
                case 'F':
                    region = "FR";   // French
                    break;
                case 'H':
                    region = "NL";   // Dutch
                    break;
                case 'I':
                    region = "IT";   // Italian
                    break;
                case 'R':
                    region = "RU";   // Russian
                    break;
                case 'S':
                    region = "ES";   // Spanish
                    break;
                case '#':
                    region = "HB"; // Homebrew
                    break;
            }

            return region;
        }
    }
}
