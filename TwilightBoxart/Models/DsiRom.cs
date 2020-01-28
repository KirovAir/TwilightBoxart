using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Models
{
    public class DsiRom : NdsRom
    {
        public override ConsoleType ConsoleType => ConsoleType.NintendoDSi;

        public DsiRom(byte[] header) : base(header)
        {
        }

        public DsiRom(string titleId) : base(titleId)
        {
        }

        public override void DownloadBoxArt(string targetFile)
        {
            try
            {
                base.DownloadBoxArt(targetFile);
            }
            catch (NoMatchException)
            {
                // Todo: Make this less ugly, embedded and optional.
                if (IsDsiWare(TitleId)) // This is DSiWare. There is no BoxArt available (probably) so use a default image.
                {
                    ImgDownloader.DownloadAndResize(BoxartConfig.DsiWareBoxartUrl, targetFile);
                }
            }
        }

        public static bool IsDsiWare(string titleId)
        {
            return titleId[0] == 'K' || titleId[0] == 'H';
        }
    }
}
