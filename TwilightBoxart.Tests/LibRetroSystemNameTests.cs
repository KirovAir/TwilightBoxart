using System.Net;
using System.Text.RegularExpressions;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests;

/// <summary>
/// Guards the system names in <see cref="ConsoleType"/> against libretro's authoritative list.
/// </summary>
/// <remarks>
/// This exists because of a bug that survived six years. The 2020 client described the DSi as
/// "Nintendo - Nintendo DSi (Digital)" (DAT-o-MATIC's name for the DSiWare set), but no libretro
/// repository has ever answered to it, so every DSi cover 404'd. Nothing caught it, because a wrong
/// system name and a genuinely missing cover produce the identical 404. No URL-shape test can find
/// this: the URL was perfectly well formed. Only comparing against the real list can.
///
/// <para>
/// Network-gated on purpose. A libretro outage must not turn CI red, so an unreachable host yields
/// Inconclusive rather than a failure, but when the host IS reachable and a name has drifted, that is
/// a real defect and this fails loudly.
/// </para>
/// </remarks>
[TestClass]
public class LibRetroSystemNameTests
{
    /// <summary>
    /// The thumbnail host serves a plain Apache directory index at the root, one directory per system,
    /// named canonically (spaces, not the underscored repository form). That makes the set of valid
    /// system names enumerable instead of guessed.
    /// </summary>
    private const string SystemIndexUrl = "https://thumbnails.libretro.com/";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    [TestCategory("Network")]
    public async Task EveryConsoleTypeDescription_MatchesALibRetroSystem()
    {
        var systems = await TryFetchSystemNamesAsync();
        if (systems is null)
        {
            Assert.Inconclusive($"{SystemIndexUrl} unreachable; skipping the name-drift check.");
            return;
        }

        Assert.IsTrue(systems.Count > 100,
            $"Only parsed {systems.Count} systems from {SystemIndexUrl}; the index format has probably " +
            "changed and this test is no longer checking anything meaningful.");

        var drifted = Enum.GetValues<ConsoleType>()
            .Where(c => c != ConsoleType.Unknown)
            .Select(c => (Console: c, Name: c.Description()))
            .Where(x => !systems.Contains(x.Name))
            .ToList();

        Assert.AreEqual(0, drifted.Count,
            "These ConsoleType descriptions do not match any libretro system, so their box art will " +
            "silently 404 for every title:" + Environment.NewLine +
            string.Join(Environment.NewLine, drifted.Select(d => $"  {d.Console} => \"{d.Name}\"")));
    }

    /// <summary>
    /// Fetches and parses the directory index. Returns null when the host cannot be reached, so the
    /// caller can distinguish "offline" from "a name is wrong".
    /// </summary>
    private static async Task<HashSet<string>?> TryFetchSystemNamesAsync()
    {
        using var client = new HttpClient { Timeout = Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TwilightBoxart/2.0 (+https://github.com/KirovAir/TwilightBoxart)");

        string html;
        try
        {
            using var response = await client.GetAsync(SystemIndexUrl);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            html = await response.Content.ReadAsStringAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        // Directory entries look like: <a href="Nintendo%20-%20Game%20Boy/">Nintendo - Game Boy/</a>
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(html, "<a href=\"([^\"?][^\"]*)/\">", RegexOptions.IgnoreCase))
        {
            var name = Uri.UnescapeDataString(match.Groups[1].Value);
            if (name != "..")
            {
                names.Add(name);
            }
        }

        return names;
    }
}
