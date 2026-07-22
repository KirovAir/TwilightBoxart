namespace TwilightBoxart.Core;

/// <summary>
/// The header every first-party client sends on <c>/v2</c>, and the fixed value it carries.
/// </summary>
/// <remarks>
/// <b>This is not a secret and must never be treated as one.</b> The value ships inside the browser
/// bundle, inside the DSi homebrew binary and inside this open-source repository, so anyone who wants
/// it has it in about ten seconds. It buys exactly one thing: a request that reaches <c>/v2</c>
/// without it was not written against this API, which sorts out drive-by scanners, hotlinkers and
/// people pointing a scraper at the art routes (all of which cost us upstream bandwidth from
/// volunteer-run servers) from actual clients. Anything that genuinely needs to be unguessable
/// (the admin panel) uses a password from the environment instead.
///
/// The one route deliberately NOT gated is the v0.7 shim at <c>POST /api</c>: those clients were
/// compiled in 2020 and cannot be asked to send anything new. That endpoint is tightened by the shape
/// of what it sends instead; see <c>LegacyEndpoints</c>.
/// </remarks>
public static class ApiKey
{
    /// <summary>The request header carrying <see cref="Value"/>.</summary>
    public const string HeaderName = "X-Twilight-Key";

    /// <summary>
    /// The fixed value. Versioned in the prefix so that if it ever does need rotating, a server can
    /// tell "old client" apart from "not a client" in the logs.
    /// </summary>
    public const string Value = "tb2_9f4c1d7a3e8b5062";
}
