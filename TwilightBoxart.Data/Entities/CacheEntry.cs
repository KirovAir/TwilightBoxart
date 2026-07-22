namespace TwilightBoxart.Data.Entities;

/// <summary>Which of the two cache layers a <see cref="CacheEntry"/> belongs to.</summary>
public enum CacheKind
{
    /// <summary>
    /// Upstream art exactly as downloaded, content-addressed and shared by every render of it.
    /// Expensive to replace (a network round-trip to GameTDB or libretro), so it gets the large budget.
    /// </summary>
    Original,

    /// <summary>
    /// A resized, bordered PNG for one specific <c>RenderOptions</c>. ~5 ms to regenerate from the
    /// original, so it is disposable and gets the small budget and the aggressive LRU.
    /// </summary>
    Render
}

/// <summary>
/// Bookkeeping for one file in the on-disk art cache. The bytes live on disk under
/// <see cref="CacheKey"/>; this row exists only so the eviction sweep can pick victims without
/// stat-ing the whole cache directory.
///
/// HOT-PATH RULE: serving a cached image MUST NOT write to this table. Doing an UPDATE per request
/// would put a SQLite write on the hottest path in the product (18,000 requests for one library scan),
/// which is exactly the mistake that makes people think they need PostgreSQL. Instead, callers buffer
/// hits in memory - a ConcurrentDictionary keyed on <see cref="CacheKey"/> - and flush them
/// periodically with
/// <see cref="TwilightBoxart.Data.Extensions.CacheEntryExtensions.FlushHitsAsync"/>, collapsing
/// thousands of requests into one transaction every 30 seconds.
///
/// The consequence, by design: <see cref="HitCount"/> and <see cref="LastAccessUtc"/> lag reality by
/// up to one flush interval, and unflushed hits are lost on an unclean shutdown. Both are acceptable -
/// they feed an eviction heuristic and a stats page, not billing.
/// </summary>
public class CacheEntry : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Identifies the cached file, and locates it: the layer name followed by the file's path
    /// relative to that layer's root, always with forward slashes -
    /// <c>originals/ab/cd/abcd....bin</c> or <c>renders/nds/ASME/{sourceSha16}/{discriminator}.png</c>.
    ///
    /// Those paths are already derived from content: an original's from the hash of the upstream
    /// bytes, a render's from the source hash plus <c>RenderOptions.CacheDiscriminator</c>. Storing
    /// the path rather than the bare hash means the eviction sweep can delete the file straight from
    /// the row without a second mapping to keep in step. Separators are normalised because the key is
    /// written by a Windows host and read by a Linux one (and vice versa) from the same volume.
    ///
    /// Unique across BOTH layers rather than per layer: the layer prefix already makes collisions
    /// impossible, and a global constraint means two rows can never claim one file, which would
    /// double-count it against a budget and let the sweep delete a file another row still lists.
    /// </summary>
    public string CacheKey { get; set; } = "";

    public CacheKind Kind { get; set; }

    /// <summary>Size of the file on disk. Summed per layer to decide when the sweep must run.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// When this entry was last served, as of the most recent hit flush. The LRU ordering key.
    /// Set to the insert time on creation so a never-hit entry is still evictable.
    /// </summary>
    public DateTime LastAccessUtc { get; set; }

    /// <summary>Total hits as of the most recent flush. Diagnostics only; eviction uses recency.</summary>
    public long HitCount { get; set; }

    /// <summary>
    /// SHA-256 of the upstream bytes this entry derives from: its own content for an
    /// <see cref="CacheKind.Original"/>, the originating original's hash for a
    /// <see cref="CacheKind.Render"/>. Because it is baked into every render's
    /// <see cref="CacheKey"/>, new source bytes for a title invalidate every size and border
    /// variant of its art for free, with no cascade and no enumeration.
    /// </summary>
    public string? SourceSha256 { get; set; }
}

public class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    public void Configure(EntityTypeBuilder<CacheEntry> builder)
    {
        builder.ToTable(nameof(CacheEntry));
        builder.HasKey(e => e.Id);

        // Cache keys are hex digests and render discriminators, compared case-insensitively so a key
        // spelled either way can never produce two rows pointing at one file.
        builder.Property(e => e.CacheKey).IsRequired().UseCollation("NOCASE");
        builder.HasIndex(e => e.CacheKey).IsUnique();

        builder.Property(e => e.Kind).HasConversion<string>();

        // Every maintenance query runs per layer, so every index leads with Kind:
        // - (Kind, LastAccessUtc): the LRU sweep's "coldest victims in this layer" scan.
        // - (Kind, CreatedDate): the age-retention sweep (the default-on originals cap).
        // - (Kind, SizeBytes): covers "SELECT Kind, SUM(SizeBytes), COUNT(*) GROUP BY Kind" - the
        //   budget check reads only these two columns, so it never touches the table.
        builder.HasIndex(e => new { e.Kind, e.LastAccessUtc });
        builder.HasIndex(e => new { e.Kind, e.CreatedDate });
        builder.HasIndex(e => new { e.Kind, e.SizeBytes });
    }
}
