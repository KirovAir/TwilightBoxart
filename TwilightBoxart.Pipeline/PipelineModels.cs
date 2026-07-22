namespace TwilightBoxart.Pipeline;

/// <summary>
/// Where the two cache layers live on disk.
/// </summary>
/// <remarks>
/// The pipeline needs exactly two directory paths and nothing else about the deployment, so this is
/// all it is given. The host owns the rest: TwilightBoxart.Web derives these from its
/// <c>TwilightSettings.DataPath</c>, and a desktop client would derive them from wherever it keeps
/// its own data. Keeping the contract this small is what lets the pipeline be hosted by something
/// that has no notion of an allowed origin or an HTTP surface at all.
///
/// Both roots must stay somewhere HTTP cannot reach - the sweep DELETES the files these paths point
/// at, and the 2020 backend cached under wwwroot and served it with UseStaticFiles, which made every
/// cached image publicly enumerable.
/// </remarks>
public sealed record CachePaths(string OriginalsPath, string RendersPath);

/// <summary>
/// Hard, non-configurable limits shared by the pipeline and the host that fronts it. These are
/// correctness boundaries, not tuning knobs.
/// </summary>
public static class PipelineLimits
{
    /// <summary>Longest accepted art key. Real keys are 4 (title id) or 16 (name digest) chars.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>Longest accepted free-text field (file names).</summary>
    public const int MaxTextLength = 512;
}

/// <summary>Per-source upstream state, so a 0% match rate can be told apart from an outage.</summary>
public sealed record UpstreamHealth(
    string Name,
    bool Healthy,
    long Successes,
    long Misses,
    long Failures,
    DateTimeOffset? LastSuccess,
    DateTimeOffset? LastFailure,
    string? LastError);
