namespace TwilightBoxart.Core.Art;

/// <summary>
/// How this process behaves towards the art upstreams (GameTDB, libretro-thumbnails).
/// </summary>
/// <remarks>
/// Constants, not configuration. These are manners rather than tuning: both upstreams are
/// volunteer-run and serve us for free, so a modest per-source concurrency cap, a real timeout and a
/// contactable User-Agent are the correct values for every deployment there will ever be. Exposing
/// them as settings only ever offered an operator the chance to be ruder to someone else's server on
/// our behalf, which is not a knob worth shipping, so the values live here, where the reasoning
/// that produced them is next to the number.
/// </remarks>
public static class ArtSourceLimits
{
    /// <summary>Name of the <see cref="System.Net.Http.IHttpClientFactory"/> client every source resolves.</summary>
    public const string HttpClientName = "twilight-boxart";

    /// <summary>
    /// Sent on every upstream request. An operator who notices this traffic can reach the owner and ask
    /// us to slow down, rather than silently null-routing an anonymous crawler.
    /// </summary>
    public static string UserAgent => About.UserAgent;

    /// <summary>
    /// In-flight requests allowed per source. A politeness cap towards that source's operator, not a
    /// throughput target: a browser caps itself at 6 connections per origin, <c>HttpClient</c> will
    /// happily open a hundred, so the limit that used to come for free has to be stated.
    /// </summary>
    public const int MaxConcurrency = 4;

    /// <summary>Extra attempts after a 429/503. Only throttling responses are retried; a miss never is.</summary>
    public const int MaxRetries = 2;

    /// <summary>
    /// Largest upstream image we will buffer. GameTDB HQ covers are ~700 KB; anything past a few MB is
    /// either not art or not worth the memory, and the renderer would have to decode all of it.
    /// </summary>
    public const long MaxDownloadBytes = 8L * 1024 * 1024;

    /// <summary>Per-request timeout. Applied to the shared <c>HttpClient</c>, so it covers headers + body.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Ceiling on how long we will honour a <c>Retry-After</c> before giving up on the request. An
    /// upstream asking for ten minutes should get a miss now and a fresh attempt later, not a held thread.
    /// </summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
}
