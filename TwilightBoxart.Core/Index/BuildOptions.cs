using System.Globalization;

namespace TwilightBoxart.Core.Index;

/// <summary>
/// Everything one index build needs to know. Every default is overridable; nothing about a
/// particular mirror is compiled in as a constant.
/// </summary>
public sealed record BuildOptions
{
    public string OutputPath { get; init; } = Path.Combine("artifacts", IndexWriter.DefaultFileName);

    /// <summary>Build from DATs already on disk instead of downloading. What the tests use.</summary>
    public string? InputDirectory { get; init; }

    /// <summary>JSON source list replacing the built-in catalog. See <see cref="DatCatalog.Load"/>.</summary>
    public string? SourcesPath { get; init; }

    /// <summary>URL template containing <c>{name}</c>. Defaults to the libretro No-Intro mirror.</summary>
    public string BaseUrlTemplate { get; init; } = DatCatalog.DefaultBaseUrlTemplate;

    /// <summary>HTTP cache for downloaded DATs: a rebuild re-fetches only what changed.</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// The build stamp written to <c>meta.version</c>. Defaults to now, in UTC, to the second. This
    /// is the one and only timestamp anywhere in the output: pass it explicitly to get a
    /// byte-for-byte reproduction of an earlier build from the same DATs.
    /// </summary>
    public string Version { get; init; } = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Fail the build when any source is missing, rather than warning and continuing.</summary>
    public bool Strict { get; init; }
}
