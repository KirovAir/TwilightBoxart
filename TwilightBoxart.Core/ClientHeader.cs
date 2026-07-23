namespace TwilightBoxart.Core;

/// <summary>
/// The optional header a first-party client uses to say which client it is, e.g. <c>web/2.0</c>.
/// </summary>
/// <remarks>
/// Usage reporting only: the server counts and logs the value, and grants nothing on it. The DSi
/// build says the same thing through its User-Agent instead, because its hand-rolled HTTP stack
/// sends one anyway; the 2020 clients say nothing and are recognised by the route they call. The
/// web client carries its own copy in <c>api.js</c> because wwwroot has no build step.
/// </remarks>
public static class ClientHeader
{
    public const string Name = "X-Twilight-Client";

    /// <summary>What the desktop app calls itself when talking to a backend.</summary>
    public static string Desktop { get; } = $"desktop/{About.Version}";
}
