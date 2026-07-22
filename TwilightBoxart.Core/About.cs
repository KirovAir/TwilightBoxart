namespace TwilightBoxart.Core;

/// <summary>
/// The project's public coordinates, in one place so the things that cite them - the outbound
/// User-Agents, the legacy endpoint's release pointer, the desktop's logo link - can never drift.
/// </summary>
public static class About
{
    public const string RepositoryUrl = "https://github.com/KirovAir/TwilightBoxart";

    public const string ReleasesUrl = RepositoryUrl + "/releases";

    /// <summary>
    /// major.minor, read from the assembly so it follows <c>Version</c> in Directory.Build.props.
    /// Hardcoding it here meant a release could ship telling upstreams the wrong number.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Honest and contactable, so an upstream operator can find us rather than block us.</summary>
    public static string UserAgent { get; } = $"TwilightBoxart/{Version} (+{RepositoryUrl})";

    private static string ReadVersion()
    {
        var version = typeof(About).Assembly.GetName().Version;
        return version is null ? "0.0" : $"{version.Major}.{version.Minor}";
    }
}
