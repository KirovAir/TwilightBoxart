using System.Text;
using Microsoft.Extensions.Logging;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Art;

/// <summary>
/// libretro-thumbnails boxart, addressed by the canonical No-Intro name. Covers every console we
/// support, but only for titles the index could name, so it runs after
/// <see cref="GameTdbArtSource"/>'s exact title-id lookup.
/// </summary>
public sealed class LibRetroArtSource(
    IHttpClientFactory httpClientFactory,
    ILogger<LibRetroArtSource> logger)
    : HttpArtSource(httpClientFactory, logger), IArtSource
{
    /// <summary>
    /// Hit <c>raw.githubusercontent.com</c> directly, and do NOT "tidy" this back to
    /// <c>github.com/libretro-thumbnails/{repo}/raw/...</c>. That form is what the 2020 client used and
    /// it 302-redirects here, costing a wasted round trip on every single request, 18,000 of them over
    /// a full library. It also breaks any browser-side use, because the redirect carries an empty
    /// <c>Access-Control-Allow-Origin</c> that matches neither <c>*</c> nor the origin.
    /// </summary>
    public const string BaseUrl = "https://raw.githubusercontent.com/libretro-thumbnails/";

    /// <summary>
    /// libretro's own thumbnail host, used as a fallback. Two differences from
    /// <see cref="BaseUrl"/>: it addresses systems by their CANONICAL name (spaces, not underscores),
    /// and it carries no branch in the path.
    /// </summary>
    /// <remarks>
    /// That second property is the point. <see cref="BaseUrl"/> hardcodes <c>/master/</c>, and while
    /// all 12 systems we support are on <c>master</c> today, 12 of the 131 repositories in the
    /// libretro-thumbnails organisation are already on <c>main</c> (measured 2026-07-20). If one of
    /// ours is ever renamed the primary URL starts returning 404, which is indistinguishable from
    /// "this game has no box art": the exact silent-failure shape that hid the DSi bug for six years.
    /// Falling through to a branch-independent mirror turns that outage into a slow path instead of a
    /// wrong answer. The mirror is slower (measured 0.377s vs 0.062s) and sends no CORS headers, so it
    /// stays a fallback rather than the primary.
    /// </remarks>
    public const string MirrorBaseUrl = "https://thumbnails.libretro.com/";

    /// <summary>
    /// Characters libretro replaces with <c>_</c> when it names a thumbnail file.
    /// See https://docs.libretro.com/guides/roms-playlists-thumbnails/
    /// Verified against real filenames: "Asterix &amp; Obelix (Europe)" is stored as
    /// "Asterix _ Obelix (Europe)": a character-for-character swap, not a word replacement.
    /// </summary>
    private const string IllegalNameCharacters = @"&*/:`<>?\|";

    public int Order => 10;

    protected override string SourceName => "libretro";

    public bool CanHandle(RomIdentity identity)
    {
        if (identity.ConsoleType == ConsoleType.Unknown || string.IsNullOrWhiteSpace(identity.CanonicalName))
        {
            return false;
        }

        // libretro-thumbnails holds exactly 13 DSi box arts (measured 2026-07-20) against a DSiWare
        // library in the thousands, so this source is a dead end for the platform. GameTDB, keyed by
        // title id, is the only viable DSi source and it has already run by the time we get here.
        // Declining outright turns ~1,073 guaranteed 404s per full DSi scan into zero requests.
        return identity.ConsoleType != ConsoleType.NintendoDsi;
    }

    public async Task<ArtBlob?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default)
    {
        if (!CanHandle(identity))
        {
            return null;
        }

        var url = BuildUrl(identity.ConsoleType, identity.CanonicalName);
        if (url is null)
        {
            return null;
        }

        var blob = await TryGetAsync(url, ct);
        if (blob is not null)
        {
            return blob;
        }

        // Primary missed. That is usually a genuine "no art for this title", but it is also what a
        // renamed default branch looks like, so give the branch-independent mirror a chance before
        // reporting a miss. See MirrorBaseUrl.
        var mirrorUrl = BuildMirrorUrl(identity.ConsoleType, identity.CanonicalName);
        return mirrorUrl is null ? null : await TryGetAsync(mirrorUrl, ct);
    }

    /// <summary>
    /// libretro-thumbnails stores revision and variant duplicates as git symlinks ("Donkey Kong
    /// Country (USA) (Rev 2).png" links to "Donkey Kong Country (USA).png"), and
    /// raw.githubusercontent serves the symlink blob verbatim: a tiny text body holding the target
    /// file name. Resolving it against the same directory turns those covers into one extra request
    /// on the fast primary instead of a warning plus a mirror round trip.
    /// </summary>
    protected override string? TryResolveSymlink(string url, byte[] body) => ResolveSymlinkTarget(url, body);

    /// <summary>Exposed so the accept/reject rules can be asserted without a network call.</summary>
    public static string? ResolveSymlinkTarget(string url, byte[] body)
    {
        // A symlink target is a bare file name; anything bigger is a real (broken) payload.
        if (body.Length is 0 or > 300)
        {
            return null;
        }

        var target = Encoding.UTF8.GetString(body);
        if (!target.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Same-directory names only: a separator would mean traversal, and libretro never nests.
        // Control characters catch multi-line bodies, U+FFFD catches binary that failed to decode.
        foreach (var c in target)
        {
            if (c is '/' or '\\' or < ' ' or '\uFFFD')
            {
                return null;
            }
        }

        return url[..(url.LastIndexOf('/') + 1)] + Uri.EscapeDataString(target);
    }

    /// <summary>
    /// Applies libretro's own naming rule. Ordinal by construction: it compares individual characters,
    /// never case-folds, and so cannot repeat the 2020 client's culture-sensitive matching bugs.
    /// </summary>
    public static string SanitizeName(string name) =>
        string.Create(name.Length, name, static (destination, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = IllegalNameCharacters.Contains(source[i]) ? '_' : source[i];
            }
        });

    /// <summary>
    /// Builds the thumbnail URL, or null when the identity cannot address one. Exposed so the shape can
    /// be asserted without a network call.
    /// </summary>
    public static string? BuildUrl(ConsoleType consoleType, string? canonicalName)
    {
        if (consoleType == ConsoleType.Unknown || string.IsNullOrWhiteSpace(canonicalName))
        {
            return null;
        }

        // Percent-encode after sanitising: No-Intro names are full of spaces, brackets and commas, and
        // the 2020 client pasted them into the URL raw.
        var file = Uri.EscapeDataString(SanitizeName(canonicalName));
        return $"{BaseUrl}{consoleType.LibRetroRepository()}/master/Named_Boxarts/{file}.png";
    }

    /// <summary>
    /// Builds the mirror URL on <see cref="MirrorBaseUrl"/>. Note the system segment uses the CANONICAL
    /// name with spaces; the underscored repository form 404s here (verified).
    /// </summary>
    public static string? BuildMirrorUrl(ConsoleType consoleType, string? canonicalName)
    {
        if (consoleType == ConsoleType.Unknown || string.IsNullOrWhiteSpace(canonicalName))
        {
            return null;
        }

        var system = Uri.EscapeDataString(consoleType.Description());
        var file = Uri.EscapeDataString(SanitizeName(canonicalName));
        return $"{MirrorBaseUrl}{system}/Named_Boxarts/{file}.png";
    }
}
