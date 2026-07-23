using System.Buffers.Binary;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Render;

namespace TwilightBoxart.Tests;

[TestClass]
public class BoxartRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int ColorTypePalette = 3;

    [TestMethod]
    public void BoxartRenderer_ProducesACappedPngForEveryBorderStyle()
    {
        var source = Cover(1400, 1260);

        foreach (var style in Enum.GetValues<BoxartBorderStyle>())
        {
            var options = new RenderOptions
            {
                Width = 128,
                Height = 115,
                KeepAspectRatio = false,
                BorderStyle = style,
                BorderThickness = 2,
            };

            var png = new BoxartRenderer().Render(source, options);
            var header = ReadPngHeader(png, style.ToString());

            Assert.AreEqual(128, header.Width, $"{style}: unexpected width");
            Assert.AreEqual(115, header.Height, $"{style}: unexpected height");

            // The hard requirement: TWiLightMenu++ silently drops anything larger, so a violation here
            // surfaces to the user as a cover that is missing for no visible reason.
            Assert.IsTrue(
                png.Length <= RenderOptions.TwilightMaxPngBytes,
                $"{style}: {png.Length} bytes exceeds the {RenderOptions.TwilightMaxPngBytes} byte cap");
        }
    }

    [TestMethod]
    public void BoxartRenderer_QuantizesPathologicalArtUnderTheCap()
    {
        // Noise that survives downscaling: 8px blocks shrink to 4px blocks at 256x192, so the output is
        // still full-colour and near-incompressible. A straight 24-bit encode of this is ~100 KB.
        var source = NoiseBlocks(1024, 768, block: 8);

        // The largest DS-displayable size: the biggest render still held to the TWiLightMenu byte cap.
        var png = new BoxartRenderer().Render(source, new RenderOptions
        {
            Width = RenderOptions.TwilightMaxWidth,
            Height = RenderOptions.TwilightMaxHeight,
            KeepAspectRatio = false,
        });

        var header = ReadPngHeader(png, "noise");

        Assert.IsTrue(
            png.Length <= RenderOptions.TwilightMaxPngBytes,
            $"quantization fallback left {png.Length} bytes, over the {RenderOptions.TwilightMaxPngBytes} byte cap");

        // The ladder must buy the reduction with colour, not with pixels - a smaller cover would be a
        // silent downgrade of the thing the caller asked for.
        Assert.AreEqual(RenderOptions.TwilightMaxWidth, header.Width);
        Assert.AreEqual(RenderOptions.TwilightMaxHeight, header.Height);
        Assert.AreEqual(ColorTypePalette, header.ColorType, "expected the quantization fallback to have run");
    }

    [TestMethod]
    public void BoxartRenderer_KeepsAspectRatioInsideTheRequestedBox()
    {
        // A square cover into a 128x115 box is height-limited, so it should come out 115x115.
        var png = new BoxartRenderer().Render(Cover(600, 600), new RenderOptions
        {
            Width = 128,
            Height = 115,
            KeepAspectRatio = true,
        });

        var header = ReadPngHeader(png, "square");

        Assert.AreEqual(115, header.Width);
        Assert.AreEqual(115, header.Height);
    }

    [TestMethod]
    public void BoxartRenderer_FitsTheBindingAxisExactly()
    {
        // The bound axis must land on the target, not a rounded-up pixel past it. The 2020 client scaled
        // both axes by a float ratio and took the ceiling, which turned 600x600 into 116x115.
        Assert.AreEqual(new SixLabors.ImageSharp.Size(115, 115), BoxartRenderer.FitToAspectRatio(600, 600, 128, 115));
        Assert.AreEqual(new SixLabors.ImageSharp.Size(128, 64), BoxartRenderer.FitToAspectRatio(1000, 500, 128, 115));
        Assert.AreEqual(new SixLabors.ImageSharp.Size(102, 115), BoxartRenderer.FitToAspectRatio(1600, 1800, 128, 115));

        // Absurd ratios still produce a drawable image rather than a zero dimension.
        Assert.AreEqual(new SixLabors.ImageSharp.Size(128, 1), BoxartRenderer.FitToAspectRatio(10_000, 1, 128, 115));
        Assert.AreEqual(new SixLabors.ImageSharp.Size(1, 115), BoxartRenderer.FitToAspectRatio(1, 10_000, 128, 115));
    }

    [TestMethod]
    public void BoxartRenderer_KeepsAspectRatioInsideTheFrameNotAroundIt()
    {
        // The DSi frame reserves 4px a side, so the artwork is fitted into 120x107 and the canvas grows
        // back to 128x115. Fitting the ratio to the full box first (as the 2020 client did) would have
        // squashed the cover by the inset.
        var png = new BoxartRenderer().Render(Cover(600, 600), new RenderOptions
        {
            Width = 128,
            Height = 115,
            KeepAspectRatio = true,
            BorderStyle = BoxartBorderStyle.NintendoDsi,
        });

        var header = ReadPngHeader(png, "dsi-aspect");

        Assert.AreEqual(115, header.Width);
        Assert.AreEqual(115, header.Height);
    }

    [TestMethod]
    public void BoxartRenderer_PaintsTheBorderOverTheCanvasEdge()
    {
        var source = Cover(600, 540);

        foreach (var style in new[] { BoxartBorderStyle.Line, BoxartBorderStyle.NintendoDsi, BoxartBorderStyle.Nintendo3Ds })
        {
            var options = new RenderOptions
            {
                Width = 128,
                Height = 115,
                KeepAspectRatio = false,
                BorderStyle = style,
                BorderThickness = 2,
                BorderColor = 0xFFFF0000,
            };

            var renderer = new BoxartRenderer();
            var bordered = Decode(renderer.Render(source, options));
            var plain = Decode(renderer.Render(source, options with { BorderStyle = BoxartBorderStyle.None }));

            Assert.AreNotEqual(plain[0, 0], bordered[0, 0], $"{style}: top-left pixel was left untouched");
            Assert.AreNotEqual(
                plain[options.Width - 1, options.Height - 1],
                bordered[options.Width - 1, options.Height - 1],
                $"{style}: bottom-right pixel was left untouched");
        }
    }

    [TestMethod]
    public void BoxartRenderer_ClampsAbusiveDimensions()
    {
        var png = new BoxartRenderer().Render(Cover(400, 400), new RenderOptions
        {
            Width = 100_000,
            Height = 100_000,
            KeepAspectRatio = false,
        });

        var header = ReadPngHeader(png, "clamped");

        Assert.AreEqual(RenderOptions.MaxWidth, header.Width);
        Assert.AreEqual(RenderOptions.MaxHeight, header.Height);
    }

    [TestMethod]
    public void CacheDiscriminator_FoldsBorderColourAndThicknessWhenTheStyleDoesNotReadThem()
    {
        // Without a Line border bc/bt never reach the compositor, so leaving them in the key would
        // let ?bc= mint unlimited cache entries for byte-identical renders.
        var plain = new RenderOptions { BorderColor = 0x11223344, BorderThickness = 3 };
        var other = new RenderOptions { BorderColor = 0xFFFFFFFF, BorderThickness = 5 };
        Assert.AreEqual(plain.Normalized().CacheDiscriminator(), other.Normalized().CacheDiscriminator());

        var lineA = new RenderOptions { BorderStyle = BoxartBorderStyle.Line, BorderColor = 0x11223344 };
        var lineB = new RenderOptions { BorderStyle = BoxartBorderStyle.Line, BorderColor = 0xFFFFFFFF };
        Assert.AreNotEqual(lineA.Normalized().CacheDiscriminator(), lineB.Normalized().CacheDiscriminator(),
            "a Line border's colour genuinely changes the bytes");
    }

    [TestMethod]
    public void BoxartRenderer_UnpacksBorderColourAsArgb()
    {
        // 0xFF000000 is opaque black, not transparent red. Feeding it to ImageSharp's Rgba32(uint) - as
        // the 2020 server did - reads it as 0xRRGGBBAA and yields the latter.
        var black = BoxartRenderer.ToPixel(0xFF000000);

        Assert.AreEqual(new Rgba32(0, 0, 0, 255), black);
        Assert.AreEqual(new Rgba32(0x12, 0x34, 0x56, 0xAB), BoxartRenderer.ToPixel(0xAB123456));
    }

    /// <summary>A smooth, plausible cover: compresses well, so it exercises the direct encode path.</summary>
    private static ArtBlob Cover(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)(x * 255 / Math.Max(1, accessor.Width - 1)),
                        (byte)(y * 255 / Math.Max(1, accessor.Height - 1)),
                        0x40,
                        byte.MaxValue);
                }
            }
        });

        return ToBlob(image);
    }

    /// <summary>Full-colour noise in blocks large enough to survive the downscale.</summary>
    private static ArtBlob NoiseBlocks(int width, int height, int block)
    {
        var random = new Random(20260720);
        using var image = new Image<Rgba32>(width, height);

        for (var blockY = 0; blockY < height; blockY += block)
        {
            for (var blockX = 0; blockX < width; blockX += block)
            {
                var color = new Rgba32(
                    (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), byte.MaxValue);

                for (var y = blockY; y < Math.Min(blockY + block, height); y++)
                {
                    for (var x = blockX; x < Math.Min(blockX + block, width); x++)
                    {
                        image[x, y] = color;
                    }
                }
            }
        }

        return ToBlob(image);
    }

    private static ArtBlob ToBlob(Image<Rgba32> image)
    {
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return new ArtBlob(buffer.ToArray(), "test://synthetic", "image/png");
    }

    private static Image<Rgba32> Decode(byte[] png) => Image.Load<Rgba32>(png);

    /// <summary>Parses IHDR directly, so "is this a real PNG" is asserted rather than assumed.</summary>
    private static (int Width, int Height, int BitDepth, int ColorType) ReadPngHeader(byte[] png, string label)
    {
        Assert.IsTrue(png.Length > 33, $"{label}: too short to be a PNG");
        CollectionAssert.AreEqual(PngSignature, png[..8], $"{label}: missing PNG signature");
        Assert.AreEqual("IHDR", Encoding.ASCII.GetString(png, 12, 4), $"{label}: first chunk is not IHDR");

        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)),
            png[24],
            png[25]);
    }
}
