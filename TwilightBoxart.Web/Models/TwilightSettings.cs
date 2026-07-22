namespace TwilightBoxart.Web.Models;

/// <summary>
/// Everything about this deployment that is not a constant. It is a short list on purpose.
/// </summary>
/// <remarks>
/// <see cref="Load"/> below is the ONLY place configuration is read, so the complete set of knobs
/// this server has is the set of <c>configuration[...]</c> lookups in that one method, five of them.
/// Everything else that used to live in <c>appsettings.json</c> (the index file name, the render
/// defaults, the upstream politeness limits, every cache timing) is now a constant next to the
/// reasoning that produced it, because in six years nobody has had a reason to set them to anything
/// else and offering the choice only invited someone to get it wrong.
///
/// Nothing here is editable over HTTP: <see cref="DataPath"/> would be an arbitrary-write primitive.
/// </remarks>
public sealed class TwilightSettings
{
    /// <summary>
    /// The generated No-Intro index, under <see cref="DataPath"/>. A constant: it is an
    /// implementation detail of the data volume that also appears in a route template, and the only
    /// thing configuring it ever achieved was giving a path-traversal check something to defend.
    /// </summary>
    public const string IndexFileName = "nointro.db";

    /// <summary>The art records and cache bookkeeping, under <see cref="DataPath"/>. No credentials, ever.</summary>
    public const string DatabaseFileName = "twilightboxart.db";

    /// <summary>
    /// Root for everything the server owns: the generated index, both cache layers and the art
    /// records. Deliberately NOT under wwwroot - the 2020 backend served its cache through
    /// UseStaticFiles, which made every cached image publicly enumerable.
    /// </summary>
    public required string DataPath { get; init; }

    public required SecuritySettings Security { get; init; }

    /// <summary>
    /// Reverse proxy whose <c>X-Forwarded-For</c> may be trusted, or null. Null is the default and
    /// the safe one: rate limiting partitions on the remote IP, so trusting a forwarded header from
    /// anyone else lets a caller pick its own bucket.
    /// </summary>
    public System.Net.IPAddress? TrustedProxy { get; init; }

    /// <summary>
    /// Reads the five environment-supplied settings and fills in everything else. Values are clamped
    /// and sanitised here rather than at the point of use, so a hand-typed value cannot reach a
    /// consumer unchecked.
    /// </summary>
    public static TwilightSettings Load(IConfiguration configuration, IHostEnvironment environment)
    {
        // Resolved to a full path so every consumer works from the same absolute root - a relative
        // "./data" is what a checkout wants (nothing is created at the filesystem root just by running
        // the thing), but it breaks the moment something needs a rooted path or logs where it wrote.
        var dataPath = configuration["Twilight:DataPath"];
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath = environment.IsDevelopment() ? "./data" : "/data";
        }

        System.Net.IPAddress.TryParse(configuration["Twilight:TrustedProxy"], out var proxy);

        return new TwilightSettings
        {
            DataPath = Path.GetFullPath(dataPath.Trim()),
            TrustedProxy = proxy,
            Security = new SecuritySettings
            {
                AdminPassword = Trimmed(configuration["Twilight:Security:AdminPassword"]),
                AllowedOrigins = LoadAllowedOrigins(configuration, environment),
            },
        };
    }

    /// <summary>
    /// Origins allowed to POST cross-origin. Empty in production: the shipped browser client is
    /// served from this same origin and therefore needs no CORS grant at all, so an entry here is
    /// only ever for someone hosting the UI separately. In Development the Vite dev server's two
    /// spellings of localhost are added automatically - that is a fact about the toolchain, not a
    /// choice a developer should have to make in a config file.
    /// </summary>
    private static string[] LoadAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetSection("Twilight:Security:AllowedOrigins").Get<string[]>() ?? [];
        string[] dev = environment.IsDevelopment()
            ? ["http://localhost:5173", "http://127.0.0.1:5173"]
            : [];

        return configured
            .Concat(dev)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public string OriginalsPath => Path.Combine(DataPath, "cache", "originals");

    public string RendersPath => Path.Combine(DataPath, "cache", "renders");

    public string IndexPath => Path.Combine(DataPath, IndexFileName);

    public string DatabasePath => Path.Combine(DataPath, DatabaseFileName);
}

public sealed class SecuritySettings
{
    /// <summary>
    /// Origins allowed to POST to identify. GETs are open to everyone - they are public data served
    /// without credentials.
    /// </summary>
    public required string[] AllowedOrigins { get; init; }

    /// <summary>
    /// Password for the admin panel at <c>/admin.html</c>. Supply it as the
    /// <c>Twilight__Security__AdminPassword</c> environment variable - never in appsettings.json.
    /// When unset the admin endpoints FAIL CLOSED (503) rather than falling back to an open or
    /// default credential.
    /// </summary>
    public string? AdminPassword { get; init; }
}
