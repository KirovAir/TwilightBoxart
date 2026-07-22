using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Render;

/// <summary>Pixels a border style reserves on each side of the canvas for its frame.</summary>
internal readonly record struct BorderInset(int Left, int Top, int Right, int Bottom)
{
    public static readonly BorderInset Zero = new(0, 0, 0, 0);

    public int Horizontal => Left + Right;

    public int Vertical => Top + Bottom;
}

/// <summary>
/// A nine-slice border frame, carried over from the 2020 client.
/// </summary>
/// <remarks>
/// The sprite sheet is square and laid out as <c>corner | 1px | corner</c>, so it is always
/// <c>2 * CornerSize + 1</c> across. The four corners come from the sheet's own four corners; the
/// single middle row and column are the strips that tile along each edge, of which only the outer
/// <see cref="Thickness"/> pixels are frame. The old code expressed the same geometry as negative
/// draw offsets into scratch canvases (<c>ImgDownloader.WriteCorner</c>/<c>WriteRow</c>); the pixels
/// produced here are identical, but the source rectangles are stated rather than derived.
/// </remarks>
internal sealed class BorderSprite
{
    private readonly Lazy<Rgba32[]> _pixels;

    private BorderSprite(string base64, int cornerSize, int thickness, BorderInset content, bool opaqueBackground)
    {
        CornerSize = cornerSize;
        Thickness = thickness;
        Content = content;
        OpaqueBackground = opaqueBackground;
        _pixels = new Lazy<Rgba32[]>(() => Decode(base64), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Side length of each square corner block, in sprite-sheet pixels.</summary>
    public int CornerSize { get; }

    /// <summary>How deep the frame runs inward from the canvas edge.</summary>
    public int Thickness { get; }

    /// <summary>Where the artwork sits inside the frame. Not symmetric for every style.</summary>
    public BorderInset Content { get; }

    /// <summary>True when the frame expects an opaque white backing behind the artwork (the 3DS case).</summary>
    public bool OpaqueBackground { get; }

    /// <summary>Sheet side length, in pixels.</summary>
    public int AtlasSize => (CornerSize * 2) + 1;

    /// <summary>Row-major RGBA pixels of the sheet. Decoded once, then read-only.</summary>
    public ReadOnlySpan<Rgba32> Pixels => _pixels.Value;

    public static BorderSprite? For(BoxartBorderStyle style) => style switch
    {
        BoxartBorderStyle.NintendoDsi => Dsi,
        BoxartBorderStyle.Nintendo3Ds => Nintendo3Ds,
        _ => null,
    };

    /// <summary>13x13 sheet: 6px corners, a 4px frame, artwork inset 4px all round.</summary>
    public static readonly BorderSprite Dsi = new(
        """
        iVBORw0KGgoAAAANSUhEUgAAAA0AAAANCAYAAABy6+R8AAAA/ElEQVR42n2SMW7CQBBFVwlKh9KlAymKFCoKmnCGdMgcw9fwMdy69BF8BZc+
        glsXSFgCKct/K8YajJTiIfbP/NmZWYe+71/FzzAMsaqqWBRFzLJsgjO64iflvYu3oJ+tOBPM8zwl1XU94XXlwRrTFCjLMjZNk6A68B/dGwOB
        uQG6rkvY2YzkB7vFG6g2jmPC63ZbYFiq0D+VlXgRX2IlGDy2bZuwJQXbECYCSvwTH+JFbLgN3ZbyZHKt7cWv+OaM/mCymRAZkhb9TJzRiU8z
        +e2Zkcq05A0P25u/kyXYO3nD/Z0umJbiakYL+pZmX8QO06c4IPz37d0NR7G4Af9+7GrVq3NWAAAAAElFTkSuQmCC
        """,
        cornerSize: 6,
        thickness: 4,
        content: new BorderInset(4, 4, 4, 4),
        opaqueBackground: false);

    /// <summary>
    /// 25x25 sheet: 12px corners and a 10px frame. The artwork inset is deliberately asymmetric
    /// (a 3DS box has a wider spine at the bottom) and matches the 2020 client's
    /// <c>Resize(width - 12, height - 11)</c> drawn at <c>(6, 4)</c>.
    /// </summary>
    public static readonly BorderSprite Nintendo3Ds = new(
        """
        iVBORw0KGgoAAAANSUhEUgAAABkAAAAZCAYAAADE6YVjAAAEYElEQVR42o1WbW/cRBDe87mX9V1ytS9pcm6igouKEtQIKSkpFSA+lp/AZ34k
        EhIS/VQhoJ9ACTSQIynFR2hiJ9eL93xvPLP3uLVCeLH0aO3dmXlmZndmXTk42P1AvX484C7wHvAW0FJGLXLeU1c/E6VVivEX4AnwNfAD8Aw4
        B4YuBSvANWATeAjDWxjnjTJjrfVAK33mBcE+5kbAlDpOliXLxhgPqEHmbcz5IFzAuEiyp0BakMj4DvAJsG0kAmUqvt8+DIJAPDoDXgJYUuPC
        Ka3DjjiTJEmUpt07SumqVmpH5myEM50LlxHcsQRG3YdXkFODKIqeat9P8X2I7xckyiR8OqZprBWG4TDwvJNOp7NpjAoRfR2rIncKnAjJEvAx
        CB7AYBMCvTCKfsIoBJLn58CxhC1ekcQBaoBNDeRO4dB6tLHRizudbXyvIH3bIDrB+m9CsgWCD5GH6zDch1ditAvBI4y/ihBJzhnJuLSHdSCR
        tEBeyN+E/kIcx+uwt6JhF0TPXW72ojLGVb6fKa27+P6Dp+OIkUjYfSAvkYjuHIlzztWUTZVeVWm6hnEZczsiGHIfcmzyMT37U6Ih2QtGMeBm
        FqcrL5Eqpq8BwExwGKdpi+mPXKRK82iCWJ9ygxN6n/KEDGhsWqqPKfdH9qlKeQvYOaZDE9j3HNRClQpTLDpU6hEXJW+nVxTilA4OSnp92Cls
        TmA/d+jtkPkd0eiAc8NSOv7pmVImv6Tb4NrAQTiXDU1K41T9v2daeP43PaNch0exWsrttdLosib+7alQpijsGsce7TQcFpQ1jNMwZiNscJyj
        cuU/CGrsAFI30styEohu08GRktbgmhlJIJO20eEo8r1OJxwaLaNK442SznWpeDNbq0rdOHpW0WOMtSxJ2hS+AaxwDBitLqXSpfceHVmkrBTf
        Euzc0jPncoy7LlS/w+ZIB9ZpmjaDMPRJUFS4ovGiIEeX2orIi3NrUthCAjsrrwpWq2+kGHs41wcI8a7JslocdzbCMMpL3bao5LNSg6xwv+YZ
        hZCsCqB/D3a09ryRNFnYr0okX9LYDa28tTRO5tGuN9HqpTgDkBf5vopEUhVA7jbk6mj1902StECAvUZZaNs59l1eRN+iNW+hOn0INEVhb29v
        J2q3n/lhKCTnmIvZDEdFhDAuaWmmcXyz0+2sY4tGlkC6h9IX7IFpBXf8PW7qMug+g7EH4p0Rb7PMWmtH0T6a3u9sF2PIiGMubsTVbkeMyxHw
        rBG2pz4+9vAuWfpcSN5l3qv8cfgUNj7iqcI9n1VmnnnDUjewdzzWRA8n9NU/hvwTJCD4Ee+PSLLrsjKLoykd9yvt68cge4j0vaGNd6twwrwu
        yom28DLbQrS8axmPoCdXwxfAYyGQvZRIllhoTqmCJfKbUHgfo5Asg3DBrhm7ama1Zbv23Kzo7P0jEXzP3yPZw34UbdhfovyKRicn6QSKP/Nk
        tWCwTidGJCnqRbM4X/KiOy11cJvevwBUhddluP9VFgAAAABJRU5ErkJggg==
        """,
        cornerSize: 12,
        thickness: 10,
        content: new BorderInset(6, 4, 6, 7),
        opaqueBackground: true);

    // Convert.FromBase64String ignores the newlines the raw string literal introduces, so the payload
    // can be wrapped for readability without a runtime cost or a build-time transform.
    private static Rgba32[] Decode(string base64)
    {
        using var image = Image.Load<Rgba32>(Convert.FromBase64String(base64));
        var pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }
}
