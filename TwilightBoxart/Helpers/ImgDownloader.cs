using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace TwilightBoxart.Helpers
{
    public class ImgDownloader
    {
        private readonly int _width;
        private readonly int _height;

        public ImgDownloader(int width, int height)
        {
            _width = width;
            _height = height;
        } 

        public void DownloadAndResize(string url, string targetFile)
        {
            using var webClient = new WebClient();
            var data = webClient.DownloadData(url);
            using var image = Image.Load(data);
            image.Mutate(x => x.Resize(_width, _height));
            image.Save(targetFile);
        }
    }
}
