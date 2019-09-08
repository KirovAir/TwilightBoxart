using KirovAir.Core.Extensions;
using System.Net;

namespace TwilightBoxart.Models
{
    public class NdsRom : Rom
    {
        public NdsRom(byte[] header)
        {
            Title = header.GetString(0, 12);
            TitleId = header.GetString(12, 4);
            RegionId = (char)header[15];
        }

        public sealed override void DownloadBoxArt(string targetFile)
        {
            // Example: https://art.gametdb.com/ds/coverS/US/BSKE.png
            var region = GetUrlRegion();
            using (var client = new WebClient())
            {
                var url = $"https://art.gametdb.com/ds/coverS/{region}/{TitleId}.png";
                //client.DownloadFile(url, targetFile);
            }
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
                    region = "AU";
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
