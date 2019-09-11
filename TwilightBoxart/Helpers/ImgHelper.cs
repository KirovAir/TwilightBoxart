using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace TwilightBoxart.Helpers
{
    public static class ImgHelper
    {
        public static int Width { get; set; } = 128;
        public static int Height { get; set; } = 115;

        public static void DownloadAndResize(string url, string targetFile)
        {
            using var webClient = new WebClient();
            var data = webClient.DownloadData(url);
            using var image = Image.Load(data);
            image.Mutate(x => x.Resize(Width, Height));
            image.Save(targetFile);
        }
    }
}
