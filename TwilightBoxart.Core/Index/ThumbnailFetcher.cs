using System.Globalization;
using System.Net;
using System.Text.Json;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>One console's box art file names, plus enough provenance to reproduce the build.</summary>
public sealed record ThumbnailListing(ConsoleType Console, string Branch, string TreeSha, IReadOnlyList<string> FileNames)
{
    /// <summary>
    /// True when this came off disk rather than from GitHub, so the caller can say which listing it
    /// built from. A silently stale listing is how "no art for this whole console" gets published.
    /// </summary>
    public bool FromCache { get; init; }

    /// <summary>Snapshot file name, e.g. <c>Nintendo_-_Game_Boy.txt</c>.</summary>
    public static string SnapshotFileName(ConsoleType console)
    {
        return console.LibRetroRepository() + ".txt";
    }
}

/// <summary>
/// Lists the <c>Named_Boxarts</c> directory of each libretro-thumbnails repository, so the build can
/// resolve No-Intro names against the covers that actually exist.
/// <para>
/// One request per console gets the whole tree, which is the cheap way round: the alternative is
/// discovering the same thing at runtime through tens of thousands of 404s. The listings are small
/// (about 48,000 names across all 26 consoles) and are stored as plain text, one name per line.
/// </para>
/// <para>
/// That directory is an HTTP cache, not a snapshot, exactly as <see cref="DatFetcher"/>'s is: an
/// online build always refetches and overwrites it, and it only answers when GitHub is unreachable or
/// rate-limits us. Constructing it with <c>offline</c> reads that directory and nothing else, which is
/// what an <c>--input</c> build and the tests want, and what makes such a run reproducible.
/// </para>
/// </summary>
public sealed class ThumbnailFetcher : IDisposable
{
    private const string TreeUrl = "https://api.github.com/repos/libretro-thumbnails/{0}/git/trees/{1}?recursive=1";
    private const string BoxartPrefix = "Named_Boxarts/";

    // The default branch is not uniform across the organisation: 12 of the 131 repositories had moved
    // to "main" when last measured, and a repository of ours moving would otherwise look exactly like
    // "this console has no art at all".
    private static readonly string[] Branches = ["master", "main"];

    private readonly HttpClient _http;
    private readonly string? _cacheDirectory;
    private readonly bool _offline;

    public ThumbnailFetcher(string? cacheDirectory = null, bool offline = false, HttpMessageHandler? handler = null)
    {
        _cacheDirectory = cacheDirectory;
        _offline = offline;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, true);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(About.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        // Anonymous callers get 60 requests an hour, and a full build needs 26 of them. A token is
        // not required, but a CI box rebuilding repeatedly will want one.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token.Trim());
        }

        if (_cacheDirectory is not null)
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    /// <summary>
    /// Fetches one console's listing, falling back to the cached copy when GitHub is unreachable or
    /// rate-limits us. Returns null only when there is no listing and no cache, which the caller must
    /// treat as "unknown", never as "no art exists".
    /// </summary>
    public async Task<ThumbnailListing?> FetchAsync(ConsoleType console, CancellationToken ct = default)
    {
        if (_offline)
        {
            return _cacheDirectory is null ? null : ReadSnapshot(_cacheDirectory, console);
        }

        var repository = console.LibRetroRepository();
        foreach (var branch in Branches)
        {
            var url = string.Format(TreeUrl, Uri.EscapeDataString(repository), branch);
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    break;
                }

                var listing = Parse(console, branch, await response.Content.ReadAsStringAsync(ct));
                if (listing is not null)
                {
                    Cache(listing);
                    return listing;
                }
            }
            // InvalidDataException is a truncated tree: a bad answer, not a reason to abandon the whole
            // build. Every failure here has to land on the cache and then on "unresolved", because the
            // caller's contract is that it never gets a partial listing and never gets an exception.
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                           or InvalidDataException &&
                                       !ct.IsCancellationRequested)
            {
                break;
            }
        }

        return _cacheDirectory is null ? null : ReadSnapshot(_cacheDirectory, console);
    }

    /// <summary>
    /// Reads a listing from a cache directory, the format <see cref="Cache"/> writes. This is what a
    /// reproducible build and the tests use: no network, no rate limit, no drift between runs.
    /// </summary>
    public static ThumbnailListing? ReadSnapshot(string directory, ConsoleType console)
    {
        var path = Path.Combine(directory, ThumbnailListing.SnapshotFileName(console));
        if (!File.Exists(path))
        {
            return null;
        }

        var lines = File.ReadAllLines(path);

        // The header is "# <branch> <sha> <count>", and the count is the point of it. A file truncated
        // cleanly between lines is otherwise indistinguishable from a complete one, and accepting it
        // would report every missing name as "this game has no cover": the exact failure this whole
        // change exists to end. No header, or a count that does not match, means refuse.
        if (lines.Length == 0 || !lines[0].StartsWith("# ", StringComparison.Ordinal))
        {
            return null;
        }

        var header = lines[0][2..].Split(' ');
        if (header.Length < 3 ||
            !int.TryParse(header[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
        {
            return null;
        }

        var names = lines.Skip(1).Where(l => l.Length > 0).ToList();
        return names.Count == 0 || names.Count != expected
            ? null
            : new ThumbnailListing(console, header[0], header[1], names) { FromCache = true };
    }

    private static ThumbnailListing? Parse(ConsoleType console, string branch, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // A truncated tree is a partial answer that would read as "these covers do not exist". None of
        // our repositories is anywhere near the limit, so refuse rather than guess. The flag must be
        // present AND false: an answer that does not say whether it is complete has not been verified,
        // and guessing "complete" is the failure direction that costs a whole console its art.
        if (!root.TryGetProperty("truncated", out var truncated) || truncated.ValueKind != JsonValueKind.False)
        {
            throw new InvalidDataException(
                $"{console.LibRetroRepository()}: GitHub did not confirm a complete tree listing.");
        }

        if (!root.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = new List<string>();
        foreach (var node in tree.EnumerateArray())
        {
            if (!node.TryGetProperty("path", out var pathValue))
            {
                continue;
            }

            var path = pathValue.GetString();
            if (path is null || !path.StartsWith(BoxartPrefix, StringComparison.Ordinal) ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(path[BoxartPrefix.Length..^4]);
        }

        if (names.Count == 0)
        {
            return null;
        }

        names.Sort(StringComparer.Ordinal);
        var sha = root.TryGetProperty("sha", out var shaValue) ? shaValue.GetString() ?? "" : "";
        return new ThumbnailListing(console, branch, sha, names);
    }

    private void Cache(ThumbnailListing listing)
    {
        if (_cacheDirectory is null)
        {
            return;
        }

        var path = Path.Combine(_cacheDirectory, ThumbnailListing.SnapshotFileName(listing.Console));
        var body = string.Create(CultureInfo.InvariantCulture,
                       $"# {listing.Branch} {listing.TreeSha} {listing.FileNames.Count}\n") +
                   string.Join('\n', listing.FileNames) + "\n";

        // Via a temporary file: a build killed mid-write would otherwise leave a truncated listing that
        // the next offline build reads as "these covers do not exist".
        var temp = AtomicFile.TempPathFor(path);
        File.WriteAllText(temp, body);
        AtomicFile.Commit(temp, path);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
