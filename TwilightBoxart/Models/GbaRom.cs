using KirovAir.Core.Extensions;
using TwilightBoxart.Downloaders.LibRetro;

namespace TwilightBoxart.Models
{
    public class GbaRom : Rom
    {
        public static LibRetroArtDownloader LibRetroArtDownloader { get; set; }

        public GbaRom(byte[] header)
        {
            if (LibRetroArtDownloader == null)
            {
                // Todo: Add config
                LibRetroArtDownloader = new LibRetroArtDownloader(
                    @"https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/master/Nintendo%20-%20Game%20Boy%20Advance.dat",
                    @"https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/raw/master/Named_Boxarts/");
            }

            Title = header.GetString(160, 12);

        }

        public sealed override void DownloadBoxArt(string targetFile)
        {
            LibRetroArtDownloader.Download(this, targetFile);
        }
    }
}
