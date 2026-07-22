namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Recognises one console family from a ROM's leading bytes.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are pure, stateless and must be safe to share across threads.
/// </para>
/// <para>
/// <b>An implementation may never throw.</b> The buffer is whatever a client sent: possibly empty,
/// possibly two bytes, possibly 512 bytes of a JPEG. Read through <see cref="HeaderSpan"/>, which
/// returns misses instead of exceptions, and treat "not enough bytes to be sure" as a miss rather
/// than as a guess.
/// </para>
/// </remarks>
public interface IConsoleHeaderParser
{
    /// <summary>Returns null when this parser does not recognise the buffer.</summary>
    HeaderDetection? TryParse(ReadOnlySpan<byte> header);
}
