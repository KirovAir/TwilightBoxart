using System;
using KirovAir.Core.Extensions;
using KirovAir.Core.Utilities;

namespace TwilightBoxart.Models.Base
{
    public class LibRetroRom : Rom
    {
        /// <summary>
        /// Used for 'simple' sha1 mapping only.
        /// </summary>
        /// <param name="targetFile"></param>
        public override void DownloadBoxArt(string targetFile)
        {
            if (string.IsNullOrEmpty(NoIntroName))
            {
                throw new Exception("No NoIntro name found for rom! Could not download from libretro github.");
            }
            
            try
            {
                Download(ConsoleType, targetFile);
            }
            catch
            {
                if (NoIntroConsoleType == ConsoleType.Unknown || ConsoleType == NoIntroConsoleType) throw;

                // Try again on NoIntroDb ConsoleType if found.
                Download(NoIntroConsoleType, targetFile);
            }
        }

        private void Download(ConsoleType consoleType, string targetFile)
        {
            // We can generate the LibRetro content url based on the NoIntroDb name.
            var consoleStr = consoleType.GetDescription().Replace(" ", "_");
            var url = $"https://github.com/libretro-thumbnails/{consoleStr}/raw/master/Named_Boxarts/";
            url = FileHelper.CombineUri(url, $"{NoIntroName}.png");
            ImgDownloader.DownloadAndResize(url, targetFile);
        }
    }
}