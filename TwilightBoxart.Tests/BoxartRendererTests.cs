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
                BorderThickness = 2
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
        var source = NoiseBlocks(1024, 768, 8);

        // The largest DS-displayable size: the biggest render still held to the TWiLightMenu byte cap.
        var png = new BoxartRenderer().Render(source, new RenderOptions
        {
            Width = RenderOptions.TwilightMaxWidth,
            Height = RenderOptions.TwilightMaxHeight,
            KeepAspectRatio = false
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
            KeepAspectRatio = true
        });

        var header = ReadPngHeader(png, "square");

        Assert.AreEqual(115, header.Width);
        Assert.AreEqual(115, header.Height);
    }

    [TestMethod]
    public void BoxartRenderer_RefusesADecompressionBomb()
    {
        // A well-formed, CRC-valid PNG of under a hundred bytes whose header declares 81 megapixels.
        // Decoding it would allocate four bytes per pixel; the renderer must refuse it on the
        // declared dimensions alone, before the decoder gets to allocate anything.
        var bomb = PngDeclaring(9_000, 9_000);

        var refused = Assert.ThrowsExactly<InvalidImageContentException>(() =>
            new BoxartRenderer().Render(bomb, new RenderOptions()));

        StringAssert.Contains(refused.Message, "megapixel",
            "the refusal must come from the decode budget, not from the truncated pixel data");
    }

    [TestMethod]
    public void BoxartRenderer_FitsTheBindingAxisExactly()
    {
        // The bound axis must land on the target, not a rounded-up pixel past it. The 2020 client scaled
        // both axes by a float ratio and took the ceiling, which turned 600x600 into 116x115.
        Assert.AreEqual(new Size(115, 115), BoxartRenderer.FitToAspectRatio(600, 600, 128, 115));
        Assert.AreEqual(new Size(128, 64), BoxartRenderer.FitToAspectRatio(1000, 500, 128, 115));
        Assert.AreEqual(new Size(102, 115), BoxartRenderer.FitToAspectRatio(1600, 1800, 128, 115));

        // Absurd ratios still produce a drawable image rather than a zero dimension.
        Assert.AreEqual(new Size(128, 1), BoxartRenderer.FitToAspectRatio(10_000, 1, 128, 115));
        Assert.AreEqual(new Size(1, 115), BoxartRenderer.FitToAspectRatio(1, 10_000, 128, 115));
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
            BorderStyle = BoxartBorderStyle.NintendoDsi
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
                BorderColor = 0xFFFF0000
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
            KeepAspectRatio = false
        });

        var header = ReadPngHeader(png, "clamped");

        Assert.AreEqual(RenderOptions.MaxWidth, header.Width);
        Assert.AreEqual(RenderOptions.MaxHeight, header.Height);
    }

    [TestMethod]
    public void BoxartRenderer_RendersAPicoBmpRegardlessOfTheOtherKnobs()
    {
        // Deliberately hostile options: Pico's format is fixed, so the size and border requests
        // must all fold flat rather than leak into the output.
        var bmp = new BoxartRenderer().Render(Cover(600, 540), new RenderOptions
        {
            Target = RenderTarget.Pico,
            Width = 999,
            Height = 999,
            BorderStyle = BoxartBorderStyle.Nintendo3Ds,
            BorderThickness = 5
        });

        // Pico Launcher's own parser (BmpHeader::Validate + BmpFileCover.cpp) is the contract
        // here, asserted branch by branch: 'BM', a 40-byte BITMAPINFOHEADER exactly, 128x96,
        // 8bpp, uncompressed, clrUsed 0 or 256 - and the palette at 0x36 with pixel data at
        // 0x436, because the launcher reads both from those fixed offsets. An encoder upgrade
        // that drifts to a V4/V5 header would pass any looser check and show no covers at all.
        Assert.AreEqual((byte)'B', bmp[0]);
        Assert.AreEqual((byte)'M', bmp[1]);
        Assert.AreEqual(0x436, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10, 4)), "pixel data offset");
        Assert.AreEqual(40, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(14, 4)), "DIB header size");
        Assert.AreEqual(RenderOptions.PicoWidth, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18, 4)));
        Assert.AreEqual(RenderOptions.PicoHeight, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22, 4)));
        Assert.AreEqual(8, BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(28, 2)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(30, 4)), "compression must be BI_RGB");
        var clrUsed = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(46, 4));
        Assert.IsTrue(clrUsed is 0 or 256, $"clrUsed {clrUsed} is outside the launcher's accepted values");

        // The 22 columns the launcher never shows stay black. Decoded checks, so palette
        // indirection is exercised too.
        var pixels = Decode(bmp);
        Assert.AreEqual(new Rgba32(0, 0, 0, 255), pixels[RenderOptions.PicoWidth - 1, 48],
            "the padding columns must stay black");
        Assert.AreNotEqual(new Rgba32(0, 0, 0, 255), pixels[53, 48],
            "the visible area should carry the artwork");
    }

    [TestMethod]
    public void BoxartRenderer_FillsThePicoWindowWithoutEverCroppingTheArt()
    {
        // GameTDB serves every DS cover at 768x680, 2.3% off the fixed 106x96 window. Stretched
        // through, that is invisible; letterboxed it was a black line down the top and bottom of
        // every cover on the card.
        var options = new RenderOptions { Target = RenderTarget.Pico };
        var black = new Rgba32(0, 0, 0, 255);

        using var ds = Decode(new BoxartRenderer().Render(Cover(768, 680), options));
        Assert.AreNotEqual(black, ds[53, 0], "a near-fitting cover should reach the top of the window");
        Assert.AreNotEqual(black, ds[53, RenderOptions.PicoHeight - 1], "and the bottom");
        Assert.AreNotEqual(black, ds[RenderOptions.PicoVisibleWidth - 1, 48], "and the last visible column");

        // A portrait box keeps its shape, and the gap either side is a blurred copy of the cover.
        // Nothing is cropped and nothing is left black: black bars read as a broken image, and the
        // window is too small to lose the top and bottom of a box to a crop.
        using var portrait = Decode(new BoxartRenderer().Render(Cover(355, 512), options));
        Assert.AreNotEqual(black, portrait[0, 48], "the gap gets a backdrop, not a black bar");
        Assert.AreNotEqual(black, portrait[53, 48], "and the artwork sits on top of it");
        Assert.AreEqual(black, portrait[RenderOptions.PicoWidth - 1, 48],
            "the backdrop must stop at the window; the hidden columns stay black");
    }

    [TestMethod]
    public void BoxartRenderer_PicoFillIgnoresTheArtsShape()
    {
        // The escape hatch for anyone who would rather have no bars at all: ar=0 stretches to the
        // window outright, which is what PicoCover does and what the fixed geometry allows.
        var options = new RenderOptions { Target = RenderTarget.Pico, KeepAspectRatio = false };
        var black = new Rgba32(0, 0, 0, 255);

        using var portrait = Decode(new BoxartRenderer().Render(Cover(355, 512), options));

        for (var x = 0; x < RenderOptions.PicoVisibleWidth; x += 5)
        {
            Assert.AreNotEqual(black, portrait[x, 48], $"column {x} should carry artwork, not a bar");
        }
    }

    [TestMethod]
    public void CacheDiscriminator_FoldsPicosGeometryButNotItsAspectRatio()
    {
        // Two Pico requests differing only in the geometry knobs are byte-identical renders and must
        // share one cache entry, one query string and one content type.
        var a = new RenderOptions { Target = RenderTarget.Pico, Width = 999, BorderColor = 0x11223344 };
        var b = new RenderOptions { Target = RenderTarget.Pico, Height = 7, BorderStyle = BoxartBorderStyle.Line };

        Assert.AreEqual(a.Normalized().CacheDiscriminator(), b.Normalized().CacheDiscriminator());
        Assert.AreEqual(a.Normalized().ToQueryString(), b.Normalized().ToQueryString());
        Assert.AreEqual("image/bmp", a.ContentType);
        Assert.AreEqual(".bmp", a.FileExtension);
        Assert.AreNotEqual(new RenderOptions().Normalized().CacheDiscriminator(), a.Normalized().CacheDiscriminator(),
            "a Pico render must never share a cache entry with a TWiLightMenu render");

        // The aspect ratio survives Normalized, so it has to reach the key and the URL: it decides
        // whether art of a different shape keeps its proportions, which is different bytes.
        var fill = new RenderOptions { Target = RenderTarget.Pico, KeepAspectRatio = false }.Normalized();
        Assert.AreNotEqual(a.Normalized().CacheDiscriminator(), fill.CacheDiscriminator());
        Assert.AreEqual("?t=pico", a.Normalized().ToQueryString(), "the default keeps the URL Pico clients already mint");
        Assert.AreEqual("?t=pico&ar=0", fill.ToQueryString());
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

    /// <summary>
    /// A CRC-valid PNG that declares the given dimensions and carries no pixel data at all - the
    /// shape of a decompression bomb, minus the part that costs memory.
    /// </summary>
    private static ArtBlob PngDeclaring(int width, int height)
    {
        var ihdr = new byte[17];
        "IHDR"u8.CopyTo(ihdr);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(8), height);
        ihdr[12] = 8; // bit depth
        ihdr[13] = 6; // colour type: RGBA

        using var buffer = new MemoryStream();
        buffer.Write(PngSignature);
        WriteChunk(buffer, ihdr);
        WriteChunk(buffer, "IEND"u8.ToArray());
        return new ArtBlob(buffer.ToArray(), "test://bomb", "image/png");
    }

    /// <summary>One PNG chunk: big-endian data length, type + data, CRC over type + data.</summary>
    private static void WriteChunk(MemoryStream buffer, byte[] typeAndData)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, typeAndData.Length - 4);
        buffer.Write(word);
        buffer.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(word, System.IO.Hashing.Crc32.HashToUInt32(typeAndData));
        buffer.Write(word);
    }

    private static Image<Rgba32> Decode(byte[] png)
    {
        return Image.Load<Rgba32>(png);
    }

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
