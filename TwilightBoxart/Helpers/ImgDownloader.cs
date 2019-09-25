using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;

namespace TwilightBoxart.Helpers
{
    public class ImgDownloader
    {
        private int _width;
        private int _height;

        public ImgDownloader(int width, int height)
        {
            _width = width;
            _height = height;
        } 

        public void DownloadAndResize(string url, string targetFile)
        {
            using (var webClient = new WebClient())
            {
                var data = webClient.DownloadData(url);
                using (var image = Image.Load(data))
                {
                    image.Mutate(x => x.Resize(_width, _height));
                    image.Save(targetFile);
                }
            }
        }

        public void SetSizeAdjustedToAspectRatio(Size aspectRatio)
        {
            if (_width == aspectRatio.Width || _height == aspectRatio.Height)
            {
                _height = aspectRatio.Height;
                _width = aspectRatio.Width;
                return;
            }

            var sourceWidth = aspectRatio.Width;
            var sourceHeight = aspectRatio.Height;
            var dWidth = _width;
            var dHeight = _height;

            var isLandscape = sourceWidth > sourceHeight;

            int newHeight;
            int newWidth;
            if (isLandscape)
            {
                newHeight = dWidth * sourceHeight / sourceWidth;
                newWidth = dWidth;
            }
            else
            {
                newWidth = dHeight * sourceWidth / sourceHeight;
                newHeight = dHeight;
            }

            _width = newWidth;
            _height = newHeight;
        }
    }
}
