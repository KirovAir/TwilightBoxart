using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Render;

/// <summary>
/// Resizes upstream art, composites a border, and encodes a PNG that TWiLightMenu++ will actually
/// display. Ported from the 2020 client's renderer, minus the
/// <c>SixLabors.ImageSharp.Drawing</c> dependency: that package existed only for the Line style's
/// <c>DrawLines</c>, which is four filled rectangles, and it lags the core package badly.
/// </summary>
public sealed class BoxartRenderer : IBoxartRenderer
{
    // Upstream art carries Adobe XMP blocks and colour profiles we neither need nor want to re-emit,
    // and animated GIFs would otherwise decode every frame.
    private static readonly DecoderOptions Decoder = new() { SkipMetadata = true, MaxFrames = 1 };

    /// <summary>
    /// Quantization ladder for the size fallback, worst-case last. PNG palettes may only be 1, 2, 4 or
    /// 8 bits per pixel, but trimming the palette below 256 still shrinks the deflate stream, so the
    /// first few rungs keep 8-bit indices and just reduce colour count.
    /// </summary>
    private static readonly (int Colors, PngBitDepth Depth)[] PaletteLadder =
    [
        (256, PngBitDepth.Bit8),
        (128, PngBitDepth.Bit8),
        (64, PngBitDepth.Bit8),
        (32, PngBitDepth.Bit8),
        (16, PngBitDepth.Bit4),
        (8, PngBitDepth.Bit4),
        (4, PngBitDepth.Bit2),
        (2, PngBitDepth.Bit1),
    ];

    /// <summary>
    /// Renders <paramref name="source"/> to a PNG no larger than the options' normalized byte budget:
    /// <see cref="RenderOptions.TwilightMaxPngBytes"/> for DS-displayable sizes, wider for renders
    /// beyond what TWiLightMenu++ can show (see <see cref="RenderOptions.Normalized"/>).
    /// </summary>
    /// <exception cref="ImageFormatException">The blob is not an image this build can decode.</exception>
    public byte[] Render(ArtBlob source, RenderOptions options)
    {
        var settings = options.Normalized();

        // The blob's declared Content-Type is ignored on purpose: ImageSharp sniffs the container,
        // which is more reliable than an upstream header.
        using var artwork = Image.Load<Rgba32>(Decoder, source.Data);

        var sprite = BorderSprite.For(settings.BorderStyle);
        var inset = ResolveInset(sprite, settings);

        var innerWidth = settings.Width - inset.Horizontal;
        var innerHeight = settings.Height - inset.Vertical;

        var innerSize = settings.KeepAspectRatio
            ? FitToAspectRatio(artwork.Width, artwork.Height, innerWidth, innerHeight)
            : new Size(innerWidth, innerHeight);

        artwork.Mutate(context => context.Resize(innerSize));

        // Sizing the artwork inside the frame and then growing the canvas back out keeps the aspect
        // ratio honest. The old code fitted the ratio to the full box and only then subtracted the
        // border, which squashed every bordered render by the inset.
        using var canvas = new Image<Rgba32>(
            innerSize.Width + inset.Horizontal,
            innerSize.Height + inset.Vertical);

        if (sprite is { OpaqueBackground: true })
        {
            FillRows(canvas, Color.White.ToPixel<Rgba32>());
        }

        canvas.Mutate(context => context.DrawImage(artwork, new Point(inset.Left, inset.Top), 1f));

        if (sprite is not null)
        {
            using var frame = BuildFrame(sprite, canvas.Width, canvas.Height);
            canvas.Mutate(context => context.DrawImage(frame, Point.Empty, 1f));
        }
        else if (settings.BorderStyle == BoxartBorderStyle.Line)
        {
            // Line has no inset: the frame is painted over the outermost pixels of the artwork, as it was
            // in the 2020 client, so a 128x115 request still yields 128x115 of cover.
            PaintLineFrame(canvas, ToPixel(settings.BorderColor), settings.BorderThickness);
        }

        return Encode(canvas, settings.MaxPngBytes);
    }

    /// <summary>
    /// Scales <c>source</c> to fit inside <c>target</c> without distortion, filling the binding axis
    /// exactly. Replaces <c>ImgDownloader.GetSizeWithCorrectAspectRatio</c>.
    /// </summary>
    /// <remarks>
    /// Which axis binds is decided by cross-multiplying integers, and the bound axis is then assigned
    /// the target outright. The old version scaled both axes by a <c>float</c> ratio and took the
    /// ceiling, so a square cover in a 128x115 box came out 116x115: <c>115f / 600 * 600</c> lands a
    /// hair above 115 and <c>Math.Ceiling</c> turns that hair into a whole pixel.
    /// </remarks>
    public static Size FitToAspectRatio(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return new Size(targetWidth, targetHeight);
        }

        int width, height;
        if ((long)sourceWidth * targetHeight <= (long)sourceHeight * targetWidth)
        {
            height = targetHeight;
            width = (int)Math.Round((double)sourceWidth * targetHeight / sourceHeight, MidpointRounding.AwayFromZero);
        }
        else
        {
            width = targetWidth;
            height = (int)Math.Round((double)sourceHeight * targetWidth / sourceWidth, MidpointRounding.AwayFromZero);
        }

        return new Size(Math.Clamp(width, 1, targetWidth), Math.Clamp(height, 1, targetHeight));
    }

    private static BorderInset ResolveInset(BorderSprite? sprite, RenderOptions settings)
    {
        if (sprite is null)
        {
            return BorderInset.Zero;
        }

        // A request small enough that the frame would consume the whole canvas gets the frame painted
        // over the artwork instead of around it. Ugly at 8x8, but it renders rather than throwing.
        var inset = sprite.Content;
        return settings.Width - inset.Horizontal >= 1 && settings.Height - inset.Vertical >= 1
            ? inset
            : BorderInset.Zero;
    }

    /// <summary>
    /// Expands the nine-slice sheet to <paramref name="width"/> x <paramref name="height"/>. Built as a
    /// standalone transparent layer so the corners' anti-aliased edges blend over the artwork exactly
    /// once, rather than being copied on top of it.
    /// </summary>
    private static Image<Rgba32> BuildFrame(BorderSprite sprite, int width, int height)
    {
        var atlas = sprite.Pixels;
        var size = sprite.AtlasSize;
        var corner = sprite.CornerSize;
        var thickness = sprite.Thickness;

        var frame = new Rgba32[width * height];

        Blit(atlas, size, 0, 0, frame, width, height, 0, 0, corner, corner);
        Blit(atlas, size, size - corner, 0, frame, width, height, width - corner, 0, corner, corner);
        Blit(atlas, size, size - corner, size - corner, frame, width, height, width - corner, height - corner, corner, corner);
        Blit(atlas, size, 0, size - corner, frame, width, height, 0, height - corner, corner, corner);

        // The sheet's middle column and row are the repeats; corner + 1 skips past the corner block.
        for (var x = corner; x < width - corner; x++)
        {
            Blit(atlas, size, corner + 1, 0, frame, width, height, x, 0, 1, thickness);
            Blit(atlas, size, corner + 1, size - thickness, frame, width, height, x, height - thickness, 1, thickness);
        }

        for (var y = corner; y < height - corner; y++)
        {
            Blit(atlas, size, 0, corner + 1, frame, width, height, 0, y, thickness, 1);
            Blit(atlas, size, size - thickness, corner + 1, frame, width, height, width - thickness, y, thickness, 1);
        }

        return Image.LoadPixelData<Rgba32>(frame, width, height);
    }

    /// <summary>Copies a rectangle between row-major buffers, clipped to the destination.</summary>
    private static void Blit(
        ReadOnlySpan<Rgba32> source, int sourceStride, int sourceX, int sourceY,
        Span<Rgba32> destination, int destinationStride, int destinationHeight, int destinationX, int destinationY,
        int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var targetY = destinationY + row;
            if (targetY < 0 || targetY >= destinationHeight)
            {
                continue;
            }

            for (var column = 0; column < width; column++)
            {
                var targetX = destinationX + column;
                if (targetX < 0 || targetX >= destinationStride)
                {
                    continue;
                }

                destination[(targetY * destinationStride) + targetX] =
                    source[((sourceY + row) * sourceStride) + sourceX + column];
            }
        }
    }

    private static void PaintLineFrame(Image<Rgba32> canvas, Rgba32 color, int thickness)
    {
        if (thickness <= 0)
        {
            return;
        }

        var depth = Math.Min(thickness, Math.Max(1, Math.Min(canvas.Width, canvas.Height) / 2));

        canvas.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var edge = Math.Min(depth, row.Length);

                if (y < depth || y >= accessor.Height - depth)
                {
                    BlendOver(row, color);
                }
                else
                {
                    BlendOver(row[..edge], color);
                    BlendOver(row[^edge..], color);
                }
            }
        });
    }

    private static void FillRows(Image<Rgba32> canvas, Rgba32 color) =>
        canvas.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                accessor.GetRowSpan(y).Fill(color);
            }
        });

    /// <summary>Source-over composite of a single colour onto a span. Degenerates to a fill when opaque.</summary>
    private static void BlendOver(Span<Rgba32> row, Rgba32 color)
    {
        if (color.A == byte.MaxValue)
        {
            row.Fill(color);
            return;
        }

        for (var i = 0; i < row.Length; i++)
        {
            var destination = row[i];
            var sourceAlpha = color.A / 255f;
            var destinationAlpha = destination.A / 255f * (1f - sourceAlpha);
            var outAlpha = sourceAlpha + destinationAlpha;

            row[i] = outAlpha <= 0f
                ? default
                : new Rgba32(
                    (byte)(((color.R * sourceAlpha) + (destination.R * destinationAlpha)) / outAlpha),
                    (byte)(((color.G * sourceAlpha) + (destination.G * destinationAlpha)) / outAlpha),
                    (byte)(((color.B * sourceAlpha) + (destination.B * destinationAlpha)) / outAlpha),
                    (byte)(outAlpha * 255f));
        }
    }

    /// <summary>
    /// Unpacks <see cref="RenderOptions.BorderColor"/>, which is <c>0xAARRGGBB</c>, the packing the
    /// client's settings file has always carried (its default <c>0xFF000000</c> is opaque black, and
    /// <c>0xFFFFFFFF</c> opaque white). The 2020 server fed the same value to ImageSharp's
    /// <c>Rgba32(uint)</c>, which reads it as <c>0xRRGGBBAA</c>, so the default black border was in fact
    /// rendered as fully transparent red.
    /// </summary>
    public static Rgba32 ToPixel(uint argb) => new(
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb,
        (byte)(argb >> 24));

    /// <summary>
    /// Encodes at PNG level 9, dropping to a quantized palette until the result fits under
    /// <paramref name="maxBytes"/> (see <see cref="RenderOptions.MaxPngBytes"/>, which defaults to
    /// <see cref="RenderOptions.TwilightMaxPngBytes"/>). TWiLightMenu++ allocates 40 cache slots of
    /// exactly 0xB000 bytes and silently refuses anything larger (ThemeTextures.cpp:964), so an
    /// oversized PNG does not fail loudly; it just becomes a cover the user can never explain the
    /// absence of.
    /// </summary>
    private static byte[] Encode(Image<Rgba32> canvas, int maxBytes)
    {
        var direct = EncodeWith(canvas, new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.Level9,
            BitDepth = PngBitDepth.Bit8,
            // Only pay for an alpha channel when something is actually translucent; the DSi frame's
            // anti-aliased corners need it, a plain resize does not.
            ColorType = HasTransparency(canvas) ? PngColorType.RgbWithAlpha : PngColorType.Rgb,
            InterlaceMethod = PngInterlaceMode.None,
            FilterMethod = PngFilterMethod.Adaptive,
            SkipMetadata = true,
        });

        if (direct.Length <= maxBytes)
        {
            return direct;
        }

        foreach (var (colors, depth) in PaletteLadder)
        {
            var candidate = EncodeWith(canvas, new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.Level9,
                ColorType = PngColorType.Palette,
                BitDepth = depth,
                InterlaceMethod = PngInterlaceMode.None,
                FilterMethod = PngFilterMethod.Adaptive,
                SkipMetadata = true,
                // No dithering: it trades banding for high-frequency noise, and noise is precisely what
                // inflates a deflate stream. Shrinking the file is the entire point of this ladder.
                Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = colors, Dither = null }),
            });

            if (candidate.Length <= maxBytes)
            {
                return candidate;
            }
        }

        // Unreachable for any size RenderOptions permits: 256x192 at 1 bit per pixel is ~6 KB.
        throw new InvalidOperationException(
            $"Could not encode a {canvas.Width}x{canvas.Height} render under " +
            $"{maxBytes} bytes even at 2 colours.");
    }

    private static byte[] EncodeWith(Image<Rgba32> canvas, PngEncoder encoder)
    {
        using var buffer = new MemoryStream();
        canvas.Save(buffer, encoder);
        return buffer.ToArray();
    }

    private static bool HasTransparency(Image<Rgba32> canvas)
    {
        var opaque = true;

        canvas.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && opaque; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A != byte.MaxValue)
                    {
                        opaque = false;
                        break;
                    }
                }
            }
        });

        return !opaque;
    }
}
