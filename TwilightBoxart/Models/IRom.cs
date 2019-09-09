namespace TwilightBoxart.Models
{
    public interface IRom
    {
        void DownloadBoxArt(string targetFile);
        string Md5Hash { get; set; }
    }
}