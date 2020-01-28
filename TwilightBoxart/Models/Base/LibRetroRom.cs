using TwilightBoxart.Helpers;

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
            catch (NoMatchException)
            {
                if (NoIntroName == SearchName)
                {
                    throw new NoMatchException("Nothing was found! (Using sha1/filename)");
                }
                // Else try filename.
                DownloadByName(targetFile);
            }
        }

        private void DownloadByName(string targetFile)
        {
            try
            {
                DownloadWithRetry(SearchName, targetFile);
            }
            catch
            {
                throw new NoMatchException("Nothing was found! (Using sha1/filename)");
            }
        }

        private void DownloadWithRetry(string name, string targetFile)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new NoMatchException("Invalid filename.");
            }

            try
            {
                Download(ConsoleType, name, targetFile);
            }
            catch (NoMatchException)
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

            // Found the characters: https://docs.libretro.com/guides/roms-playlists-thumbnails/
            // &*/:`<>?\|
            name = name.Replace("&", "_");
            name = name.Replace("*", "_");
            name = name.Replace("/", "_");
            name = name.Replace(":", "_");
            name = name.Replace("`", "_");
            name = name.Replace("<", "_");
            name = name.Replace(">", "_");
            name = name.Replace("?", "_");
            name = name.Replace("\\", "_");
            name = name.Replace("|", "_");

            url = FileHelper.CombineUri(url, $"{name}.png");
            ImgDownloader.DownloadAndResize(url, targetFile);
        }
    }
}