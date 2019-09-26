using System;
using System.IO;
using KirovAir.Core.Extensions;
using KirovAir.Core.Utilities;

namespace TwilightBoxart.Models.Base
{
    public class LibRetroRom : Rom
    {
        /// <summary>
        /// Used for 'simple' name or sha1 mapping only.
        /// </summary>
        /// <param name="targetFile"></param>
        public override void DownloadBoxArt(string targetFile)
        {
            if (string.IsNullOrEmpty(NoIntroName))
            {
                DownloadByName(targetFile);
                return;
            }

            try
            {
                // Try NoIntroName first.
                DownloadWithRetry(NoIntroName, targetFile);
            }
            catch
            {
                // Else try filename.
                DownloadByName(targetFile);
            }
        }

        private void DownloadByName(string targetFile)
        {
           
            try
            {
                DownloadWithRetry(Name, targetFile);
            }
            catch
            {
                throw new NoMatchException("Could not match rom using sha1 or filename.. Skipping.");
            }
        }

        private void DownloadWithRetry(string name, string targetFile)
        {
            try
            {
                Download(ConsoleType, name, targetFile);
            }
            catch
            {
                if (NoIntroConsoleType == ConsoleType.Unknown || ConsoleType == NoIntroConsoleType) throw;

                // Try again on NoIntroDb ConsoleType if found.
                Download(NoIntroConsoleType, name, targetFile);
            }
        }

        private void Download(ConsoleType consoleType, string name, string targetFile)
        {
            // We can generate the LibRetro content url based on the NoIntroDb name.
            var consoleStr = consoleType.GetDescription().Replace(" ", "_");
            var url = $"https://github.com/libretro-thumbnails/{consoleStr}/raw/master/Named_Boxarts/";
            url = FileHelper.CombineUri(url, $"{name}.png");
            ImgDownloader.DownloadAndResize(url, targetFile);
        }
    }
}