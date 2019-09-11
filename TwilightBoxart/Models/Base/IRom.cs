using TwilightBoxart.Crawlers.NoIntro;

namespace TwilightBoxart.Models.Base
{
    public interface IRom
    {
        void DownloadBoxArt(string targetFile);
        string Sha1 { get; set; }
        string TitleId { get; set; }
        ConsoleType ConsoleType { get; set; }
        string NoIntroName { get; set; }
        NoIntroConsoleType NoIntroConsoleType { get; set; }
    }
}