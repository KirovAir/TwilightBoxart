using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class DsiRom : NdsRom
    {
        public override ConsoleType ConsoleType => ConsoleType.NintendoDSi;

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
                    ImgDownloader.DownloadAndResize(BoxartConfig.DsiWareBoxartUrl, targetFile);
                }
            }
        }
    }
}
