using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;

namespace TwilightBoxart.Helpers
{
    /// <summary>
    /// Downloads and resizes an image.
    /// </summary>
    public class ImgDownloader
    {
        private int _width;
        private int _height;
        private readonly bool _keepAspectRatio;
        private readonly BorderSettings _borderSettings;

        public ImgDownloader(int width, int height, bool keepAspectRatio = true, BorderSettings borderSettings = null)
        {
            _width = width;
            _height = height;
            _keepAspectRatio = keepAspectRatio;
            _borderSettings = borderSettings;
        }

        public void DownloadAndResize(string url, string targetFile)
        {
            using (var webClient = new WebClient())
            {
                var data = webClient.DownloadData(url);
                var decoder = GetDecoder(url);

                using (var image = decoder == null ? Image.Load(data) : Image.Load(data, decoder))
                {
                    var encoder = GetEncoder(image, targetFile);
                    image.Metadata.ExifProfile = null;

                    var targetSize = new Size(_width, _height);
                    if (_keepAspectRatio)
                    {
                        targetSize = GetSizeWithCorrectAspectRatio(image.Width, image.Height, _width, _height);
                    }

                    ResizeWithDSiBorder(image, encoder, targetSize, targetFile);

                    if (_borderSettings == null)
                    {
                        ResizeOnly(image, encoder, targetSize, targetFile);
                    }
                    else
                    {
                        ResizeWithBorder(image, encoder, targetSize, targetFile);
                    }
                }
            }
        }

        private void ResizeOnly(Image image, IImageEncoder encoder, Size size, string targetFile)
        {
            image.Mutate(x => x.Resize(size));
            image.Save(targetFile, encoder);
        }

        private void ResizeWithBorder(Image image, IImageEncoder encoder, Size size, string targetFile)
        {
            image.Mutate(x => x.Resize(size));

            var (width, height) = size;
            using (var canvas = new Image<Rgba32>(width, height))
            {
                canvas.Mutate(x => x.DrawImage(image, new Point(0, 0), new GraphicsOptions()));

                for (var i = 0; i < _borderSettings.Thickness; i++)
                {
                    var adj = i;
                    canvas.Mutate(x => x.DrawLines(
                            new GraphicsOptions(false, 1),
                            Pens.Solid(_borderSettings.Color, 2),
                            new Vector2(adj, adj), 
                            new Vector2(width - adj, adj),
                            new Vector2(width - adj, height - adj),
                            new Vector2(adj, height - adj), 
                            new Vector2(adj, adj)
                        )
                    );
                }

                canvas.Save(targetFile, encoder);
            }
        }

        private void ResizeWithDSiBorder(Image image, IImageEncoder encoder, Size size, string targetFile)
        {
            var (width, height) = size;

            image.Mutate(x => x.Resize(width - 8, height - 8));

            using (var canvas = new Image<Rgba32>(width, height))
            {
                canvas.Mutate(x => x.DrawImage(image, new Point(4, 4), new GraphicsOptions()));

                // Draw corners

                WriteCorner(canvas, ImgLib.DSi);


                canvas.Save(targetFile, encoder);
            }
        }

        private static readonly Dictionary<string, Image<Rgba32>> ImgCache = new Dictionary<string, Image<Rgba32>>();
        private static void WriteCorner(Image image, ImgData data)
        {
            if (!ImgCache.TryGetValue(data.Name, out var img))
            {
                img = Image.Load(Convert.FromBase64String(data.Data));
                ImgCache[data.Name] = img;
            }

            for (var i = 0; i < 4; i++)
            {
                using (var canvas = new Image<Rgba32>(data.CornerWidth, data.CornerHeight))
                {
                    var coords = data.Coords[i];
                    switch (i)
                    {
                        case 0:
                            canvas.Mutate(x => x.DrawImage(img, new Point(coords.CornerX, coords.CornerY), 1));
                            image.Mutate(c => c.DrawImage(canvas, new Point(0, 0), 1));
                            WriteRow(image, data, i);
                            break;
                        case 1:
                            canvas.Mutate(x => x.DrawImage(img, new Point(coords.CornerX, coords.CornerY), 1));
                            image.Mutate(c => c.DrawImage(canvas, new Point(image.Width - canvas.Width, 0), 1));
                            WriteRow(image, data, i);
                            break;
                        case 2:
                            canvas.Mutate(x => x.DrawImage(img, new Point(coords.CornerX, coords.CornerY), 1));
                            image.Mutate(c => c.DrawImage(canvas, new Point(image.Width - canvas.Width, image.Height - canvas.Height), 1));
                            WriteRow(image, data, i);
                            break;
                        case 3:
                            canvas.Mutate(x => x.DrawImage(img, new Point(coords.CornerX, coords.CornerY), 1));
                            image.Mutate(c => c.DrawImage(canvas, new Point(0, image.Height - canvas.Height), 1));
                            WriteRow(image, data, i);
                            break;
                    }
                }
            }
        }

        private static void WriteRow(Image image, ImgData data, int cycle)
        {
            var horizontal = cycle % 2 == 0;
            var end = cycle > 1;

            var width = data.BorderWidth;
            var height = data.BorderHeight;
            var imgSize = image.Width;

            var cornerSize = data.CornerWidth;

            if (!horizontal)
            {
                width = data.BorderHeight;
                height = data.BorderWidth;
                imgSize = image.Height;
                cornerSize = data.CornerHeight;
            }

            var x = data.Coords[cycle].BorderX;
            var y = data.Coords[cycle].BorderY;

            using (var canvas = new Image<Rgba32>(width, height))
            {
                canvas.Mutate(c => c.DrawImage(ImgCache[data.Name], new Point(x, y), 1));
                for (var i = cornerSize; i < imgSize - cornerSize; i++)
                {
                    var writeX = i;
                    var writeY = end ? (horizontal ? image.Height - height : image.Width - width) : 0;
                    if (!horizontal)
                    {
                        writeX = writeY;
                        writeY = i;
                    }

                    image.Mutate(c => c.DrawImage(canvas, new Point(writeX, writeY), 1));
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

        private Size GetSizeWithCorrectAspectRatio(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            var widthRatio = (float)targetWidth / (float)sourceWidth;
            var heightRatio = (float)targetHeight / (float)sourceHeight;

            var ratio = Math.Min(widthRatio, heightRatio);

            var width = (int)Math.Ceiling(sourceWidth * ratio);
            var height = (int)Math.Ceiling(sourceHeight * ratio);

            if (width > targetWidth)
            {
                width = targetWidth;
            }

            if (height > targetHeight)
            {
                height = targetHeight;
            }

            return new Size(width, height);
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

    public class BorderSettings
    {
        public int Thickness { get; set; }
        public Rgba32 Color { get; set; }
    }



}
