using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TwilightBoxart.Core;

namespace TwilightBoxart.Desktop.Services;

/// <summary>What the update check found: the newer release's number, its notes and where to get it.</summary>
public sealed record UpdateInfo(Version Version, string TagName, string ReleaseUrl, string Notes);

/// <summary>
/// Polls GitHub for the latest release and reports one only when it is genuinely newer than the running
/// build - the classic app's "a new update is available" nudge, minus the certificate override it shipped.
/// Best-effort: any failure (offline, rate-limited, malformed) yields no update rather than an error.
/// </summary>
public sealed class UpdateService(IHttpClientFactory httpFactory)
{
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, About.LatestReleaseApiUrl);
            // GitHub rejects anonymous calls without a User-Agent; the Accept header pins the API version.
            request.Headers.UserAgent.ParseAdd(About.UserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var release = await response.Content.ReadFromJsonAsync<GithubRelease>(ct);
            if (release is null || release.PreRelease || string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            if (!TryGetNewer(About.CurrentVersion, release.TagName, out var version))
            {
                return null;
            }

            return new UpdateInfo(version, release.TagName, ReleaseLink(release), ShortNotes(release.Body));
        }
        catch (Exception)
        {
            // Offline, rate-limited, or a shape we did not expect: never surface the update check as a failure.
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="tagName"/> parses to a version strictly greater than <paramref name="current"/>.
    /// Tolerates a leading "v" and differing component counts. Static and side-effect free, so it is unit tested
    /// without touching the network.
    /// </summary>
    public static bool TryGetNewer(Version current, string tagName, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var trimmed = tagName.TrimStart('v', 'V').Trim();
        if (!Version.TryParse(trimmed, out var parsed))
        {
            return false;
        }

        version = About.Normalize(parsed);
        return version > current;
    }

    private static string ReleaseLink(GithubRelease release) =>
        string.IsNullOrWhiteSpace(release.HtmlUrl) ? About.ReleasesUrl : release.HtmlUrl;

    /// <summary>Release bodies split the user-facing summary from the mechanical notes with a "---"; keep the first half.</summary>
    private static string ShortNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        var head = body.Split(["---"], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return head.Length > 700 ? head[..700].TrimEnd() + "…" : head;
    }

    private sealed record GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; init; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("prerelease")] public bool PreRelease { get; init; }
    }
}
