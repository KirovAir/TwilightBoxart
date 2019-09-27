using TwilightBoxart.Helpers;

namespace TwilightBoxart.Models.Base
{
    public interface IRom
    {
        string FileName { get; set; }
        string SearchName { get; }
        string Sha1 { get; set; }
        string Title { get; set; }
        string TitleId { get; set; }
        ConsoleType ConsoleType { get; set; }
        string NoIntroName { get; set; }
        ConsoleType NoIntroConsoleType { get; set; }
        void DownloadBoxArt(string targetFile);
        void SetDownloader(ImgDownloader downloader);
    }
}