namespace TwilightBoxart.Core.Art;

/// <summary>
/// Magic-byte check on a downloaded payload. Deliberately does not consult <c>Content-Type</c>: a soft
/// 404 served as <c>text/html</c> is the same problem as one served as <c>application/octet-stream</c>,
/// and only the bytes tell the truth.
/// </summary>
public static class ImageSniffer
{
    /// <summary>True when <paramref name="data"/> starts with a container signature the renderer can decode.</summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> data) =>
        StartsWith(data, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]) // PNG
        || StartsWith(data, [0xFF, 0xD8, 0xFF])                                           // JPEG
        || StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8)                    // GIF
        || StartsWith(data, "BM"u8)                                                        // BMP
        || StartsWith(data, [0x49, 0x49, 0x2A, 0x00])                                      // TIFF little-endian
        || StartsWith(data, [0x4D, 0x4D, 0x00, 0x2A])                                      // TIFF big-endian
        || IsWebp(data);

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) =>
        data.Length >= signature.Length && data[..signature.Length].SequenceEqual(signature);

    private static bool IsWebp(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && StartsWith(data, "RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8);
}
