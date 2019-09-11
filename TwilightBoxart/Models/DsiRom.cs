using TwilightBoxart.Helpers;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class DsiRom : NdsRom
    {
        public override ConsoleType ConsoleType => ConsoleType.Dsi;

        public DsiRom(byte[] header) : base(header)
        {
        }

        public override void DownloadBoxArt(string targetFile)
        {
            try
            {
                base.DownloadBoxArt(targetFile);
            }
            catch
            {
                // Todo: Make this less ugly, embedded and optional.
                if (TitleId[0] == 'K' || TitleId[0] == 'H') // This is DSiWare. There is no BoxArt available (probably) so use a default image.
                {
                    ImgHelper.DownloadAndResize("https://www.imgdumper.nl/uploads9/5d790c464226f/5d790c463e9f2-BAE4069C-8E5A-47EF-978A-1601C73F0C84.jpeg", targetFile);
                }
            }
        }
    }
}
