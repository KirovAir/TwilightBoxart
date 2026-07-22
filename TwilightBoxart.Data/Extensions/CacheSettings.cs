namespace TwilightBoxart.Data.Extensions;

/// <summary>
/// The two-layer art cache: two configurable disk budgets, and constants for everything else.
/// </summary>
/// <remarks>
/// Disk is the one thing that genuinely differs between a NAS with a spare terabyte and a Pi with an
/// 8 GB card, so the two budgets are bound from configuration (<c>Twilight__Cache__*</c>) and clamped
/// by <see cref="Normalized"/>. The timings below are not: sweep cadence, hysteresis and flush
/// interval are consequences of how the cache works rather than preferences about it, and no operator
/// has the information needed to pick better ones than the values here.
///
/// Deliberately holds NO filesystem paths. The eviction sweep DELETES the files these budgets govern, so
/// the cache root stays a deployment fact in environment configuration rather than a value carried here.
/// </remarks>
public class CacheSettings
{
    /// <summary>
    /// Byte budget for the originals layer - upstream art exactly as downloaded. Large, because every
    /// entry costs a network round-trip to a volunteer-run site to replace, and because it is shared:
    /// one original backs every size and border variant of that game.
    /// </summary>
    public long OriginalsBudgetBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Byte budget for the renders layer. Small on purpose: a render is ~5 ms to regenerate from its
    /// original, so it is cheaper to evict aggressively than to hoard every requested size.
    /// </summary>
    public long RendersBudgetBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// How long an ORIGINAL is retained before it is dropped regardless of popularity.
    /// </summary>
    /// <remarks>
    /// A retention limit, not a performance one, which is why it is a constant and not a budget.
    /// Full-resolution covers are owned by the publishers, and neither GameTDB nor libretro-thumbnails
    /// licenses them onward (the thumbnails repositories carry no licence at all). Holding them under
    /// an LRU budget alone means a popular cover stays on disk forever, which is a permanent partial
    /// mirror of somebody else's artwork. A clock-based ceiling keeps the layer what it is meant to be,
    /// a transient buffer that absorbs a scan burst, while costing only one re-fetch per cover per week.
    /// Making it configurable would mean shipping a supported way to turn that back into a mirror.
    ///
    /// The renders layer is deliberately exempt: a 128x115 thumbnail is a different thing from a
    /// byte-identical copy of the source, and regenerating one is ~5 ms.
    /// </remarks>
    public static readonly TimeSpan OriginalsMaxAge = TimeSpan.FromDays(7);

    /// <summary>How often the eviction sweep runs. It is a background pass, never on the request path.</summary>
    public static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How far under budget a sweep evicts to. Evicting to exactly the budget would leave the next
    /// request over it again, so a sweep that just ran would immediately need to run again; taking a
    /// slice off gives hysteresis.
    /// </summary>
    public const double EvictionLowWaterFraction = 0.9;

    /// <summary>
    /// How often buffered cache hits are written back; see <see cref="Entities.CacheEntry"/> for the
    /// hot-path rule this cadence serves.
    /// </summary>
    public static readonly TimeSpan HitFlushInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a known-miss is remembered before an upstream source is asked again. Without this,
    /// a library full of unmatched ROMs re-asks GameTDB for every one of them on every rescan.
    /// </summary>
    public static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromHours(12);

    /// <summary>Clamps both budgets into a range that cannot fill the disk or starve the cache.</summary>
    public CacheSettings Normalized() => new()
    {
        // 16 MiB floor: below roughly this the sweep evicts art faster than it can be used, and the
        // cache becomes pure overhead. 1 TiB ceiling is a typo guard, not a real limit.
        OriginalsBudgetBytes = Math.Clamp(OriginalsBudgetBytes, 16L * 1024 * 1024, 1024L * 1024 * 1024 * 1024),
        RendersBudgetBytes = Math.Clamp(RendersBudgetBytes, 16L * 1024 * 1024, 1024L * 1024 * 1024 * 1024),
    };
}
