using TwilightBoxart.Crawlers.NoIntro;

namespace TwilightBoxart.Models.Base
{
    public interface IRom
    {
        string FileName { get; set; }
        string Sha1 { get; set; }
        string Title { get; set; }
        string TitleId { get; set; }
        ConsoleType ConsoleType { get; set; }
        string NoIntroName { get; set; }
        NoIntroConsoleType NoIntroConsoleType { get; set; }
        void DownloadBoxArt(string targetFile);
    }
}