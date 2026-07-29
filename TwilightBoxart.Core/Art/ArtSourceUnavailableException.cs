namespace TwilightBoxart.Core.Art;

/// <summary>
/// A source could not reach its upstream (timeout, connection reset, DNS) even after retrying.
/// </summary>
/// <remarks>
/// Deliberately distinct from a miss. A miss is the upstream answering "no art for this title", an
/// ordinary outcome that returns null; this is "we never got an answer". The fetch ladder catches it
/// and records a FAILURE rather than a miss, which is what stops a passing outage from reading as "this
/// title has no cover" and earning the long negative-cache back-off a genuine miss deserves. Before this
/// existed, <see cref="HttpArtSource"/> swallowed every transport error into a miss, so one 20-second
/// GameTDB timeout during a scan hid a title's art for the full negative-cache duration.
/// </remarks>
public sealed class ArtSourceUnavailableException(string source, string url, Exception inner)
    : Exception($"{source}: upstream unavailable for {url}", inner)
{
    /// <summary>Short source name, e.g. "gametdb". Named to avoid hiding <see cref="Exception.Source"/>.</summary>
    public string SourceName { get; } = source;

    /// <summary>The URL whose fetch failed. In logs already; never surfaced to an unauthenticated caller.</summary>
    public string Url { get; } = url;
}
