using System.IO;
using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
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
                var decoder = GetDecoder(url);

                using (var image = decoder == null ? Image.Load(data) : Image.Load(data, decoder))
                {
                    image.Metadata.ExifProfile = null;

                    image.Mutate(x => x.Resize(_width, _height));

                    var encoder = GetEncoder(image, targetFile);
                    image.Save(targetFile, encoder);
                }
            }
        }

        private IImageDecoder GetDecoder(string sourceFile)
        {
            var ext = Path.GetExtension(sourceFile)?.ToLower();

            switch (ext)
            {
                case ".png":
                    return new PngDecoder { IgnoreMetadata = true };
                case ".jpg":
                case ".jpeg":
                    return new JpegDecoder { IgnoreMetadata = true };
                case ".gif":
                    return new GifDecoder { IgnoreMetadata = true };
                default:
                    return null;
            }
        }

        private IImageEncoder GetEncoder(Image image, string targetFile)
        {
            var ext = Path.GetExtension(targetFile);
            var manager = image.GetConfiguration().ImageFormatsManager;
            var format = manager.FindFormatByFileExtension(ext);
            var encoder = manager.FindEncoder(format);

            if (encoder is PngEncoder pngEncoder)
            {
                SetPngSettings(pngEncoder);
            }

            return encoder;
        }

        private void SetPngSettings(PngEncoder encoder)
        {
            encoder.CompressionLevel = 9;
            encoder.InterlaceMethod = PngInterlaceMode.None;
            encoder.BitDepth = PngBitDepth.Bit8;
            encoder.ColorType = PngColorType.Rgb;
            encoder.FilterMethod = PngFilterMethod.Adaptive;
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
