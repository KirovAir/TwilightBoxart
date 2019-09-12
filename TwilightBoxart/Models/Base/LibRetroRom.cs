using System;
using KirovAir.Core.Utilities;
using TwilightBoxart.Helpers;

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

            var url = FileHelper.CombineUri(ConsoleConfig.Get(ConsoleType).ContentUrl, $"{NoIntroName}.png");
            ImgHelper.DownloadAndResize(url, targetFile);
        }
    }
}
