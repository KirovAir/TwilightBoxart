namespace TwilightBoxart.Core;

/// <summary>
/// The project's public coordinates, in one place so the things that cite them - the outbound
/// User-Agents, the legacy endpoint's release pointer, the desktop's logo link - can never drift.
/// </summary>
public static class About
{
    public const string RepositoryUrl = "https://github.com/KirovAir/TwilightBoxart";

    public const string ReleasesUrl = RepositoryUrl + "/releases";

    /// <summary>The GitHub API for the newest published release; what the desktop update check polls.</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/KirovAir/TwilightBoxart/releases/latest";

    /// <summary>
    /// major.minor, read from the assembly so it follows <c>Version</c> in Directory.Build.props.
    /// Hardcoding it here meant a release could ship telling upstreams the wrong number.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// The running build with all four components defined, so it compares cleanly against a release tag:
    /// a <c>2.0.0</c> tag would otherwise read as newer than a <c>2.0</c> assembly on <see cref="System.Version"/>'s
    /// unspecified-component rule. Used by the update check.
    /// </summary>
    public static Version CurrentVersion { get; } = Normalize(typeof(About).Assembly.GetName().Version);

    /// <summary>Fills unspecified (-1) version components with 0, so tags of differing part counts compare by value.</summary>
    public static Version Normalize(Version? version) =>
        version is null
            ? new Version(0, 0, 0, 0)
            : new Version(Math.Max(version.Major, 0), Math.Max(version.Minor, 0), Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>Honest and contactable, so an upstream operator can find us rather than block us.</summary>
    public static string UserAgent { get; } = $"TwilightBoxart/{Version} (+{RepositoryUrl})";

    private static string ReadVersion()
    {
        var version = typeof(About).Assembly.GetName().Version;
        return version is null ? "0.0" : $"{version.Major}.{version.Minor}";
    }
}
