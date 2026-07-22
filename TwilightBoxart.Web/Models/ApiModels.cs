using TwilightBoxart.Pipeline;

namespace TwilightBoxart.Web.Models;

/// <summary>
/// Hard, non-configurable limits on the public API surface. These are correctness boundaries, not
/// tuning knobs - an operator who could raise them could turn a single request into an OOM.
/// </summary>
public static class ApiLimits
{
    /// <summary>Maximum fingerprints in one <c>POST /v2/identify</c> batch.</summary>
    public const int MaxIdentifyItems = 500;

    /// <summary>Maximum <c>POST /v2/identify</c> body, in bytes (1 MB).</summary>
    public const long MaxIdentifyBodyBytes = 1_048_576;

    /// <summary>
    /// Maximum <c>POST /api</c> body. A real v0.7 request is ~1 KB (a 512-byte header sample in
    /// base64 plus a handful of form fields), so 16 KB is generous without being an allocation.
    /// </summary>
    public const long MaxLegacyBodyBytes = 16 * 1024;

    /// <summary>
    /// Longest accepted free-text field (file names). Owned by <see cref="PipelineLimits"/>, which
    /// validates against it too; re-exported so the two cannot drift.
    /// </summary>
    public const int MaxTextLength = PipelineLimits.MaxTextLength;
}

/// <summary>Unauthenticated liveness/observability payload. Deliberately carries no paths and no keys.</summary>
/// <summary>
/// All <c>/v2/health</c> says: "ok", or "degraded" when there is no usable index. The detail that
/// used to live here moved to nothing; it was already in <c>AdminStats</c>, which is the only
/// place anything ever read it from.
/// </summary>
public sealed record HealthResponse
{
    public required string Status { get; init; }
}

public sealed record IndexHealth(string Version, int RowCount, bool Available, string? Reason = null)
{
    /// <summary>
    /// The index as an endpoint sees it, shared by health and the admin panel so the two can never
    /// describe it differently. Version and row count are read defensively - the index may
    /// legitimately be missing on a first run, and both callers must still answer. The null-object
    /// stand-in's reason is surfaced so a degraded instance explains itself instead of only
    /// reporting zero rows.
    /// </summary>
    public static IndexHealth From(IMetadataIndex index, TwilightSettings settings)
    {
        var reason = index switch
        {
            Services.ReloadableMetadataIndex reloadable => reloadable.UnavailableReason,
            TwilightBoxart.Core.Identify.NullMetadataIndex nullIndex => nullIndex.Reason,
            _ => null,
        };

        string? version;
        int rowCount;
        try
        {
            version = index.Version;
            rowCount = index.RowCount;
        }
        catch (Exception)
        {
            // A file that exists but cannot be read is a different failure from a missing one, and
            // must not report the missing-index shape: say "corrupt" and mark the index unavailable.
            return new IndexHealth("corrupt", 0, Available: false,
                "the index file exists but could not be read");
        }

        return new IndexHealth(version ?? "unavailable", rowCount, File.Exists(settings.IndexPath), reason);
    }
}

public sealed record CacheHealth(string Name, int Files, long Bytes, long BudgetBytes, bool Scanned);
