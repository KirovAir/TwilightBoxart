namespace TwilightBoxart.Data.Entities;

/// <summary>
/// Everything the server knows about one art key: which game it is, which cached original blob backs
/// it, and when we last failed to find one.
///
/// This is the table that makes <c>GET /v2/art/{platform}/{key}.png</c> resolvable on its own.
/// <c>POST /v2/identify</c> collapses an unbounded fingerprint space onto a bounded TITLE space,
/// and this table IS that title space: when identify resolves a ROM it
/// records the identity here, so a later art request carrying only <c>{platform}/{key}</c> can still
/// reach a name-addressed source like libretro-thumbnails. Serial-bearing platforms need no prior
/// identify at all - the key is the title id.
///
/// It is a table rather than the directory of JSON files it used to be for two reasons.
/// <see cref="MissUntil"/> is the negative-cache backoff that the design assigns to EF explicitly.
/// And one row per title beats ~42,000 individual small files on a volume an operator has to back up.
///
/// The BYTES are still on disk - see <see cref="CacheEntry"/>. This row only ever holds the pointer.
/// </summary>
public class ArtRecord : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>Console the key belongs to. Art keys are only unique within a console.</summary>
    public ConsoleType ConsoleType { get; set; }

    /// <summary>
    /// The art key: the 4-char title id for serial-bearing platforms ("ASME"), otherwise the 16-hex
    /// digest of the canonical name, exactly as in <c>TwilightBoxart.Core.Models.RomIdentity.Key</c>.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>Title id / game code, when the key came from a header serial. Then it equals the key.</summary>
    public string? Serial { get; set; }

    /// <summary>
    /// Canonical No-Intro name. Load-bearing for libretro-thumbnails, which is name-addressed:
    /// without it a 16-hex digest key cannot be turned back into a fetchable URL.
    /// </summary>
    public string? CanonicalName { get; set; }

    /// <summary>Internal title from the ROM header, when the client read one.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Region character from the header (NDS/DSi byte 0x0F), as a one-character string. Stored as
    /// text rather than as a <c>char</c> so the column is legible in a SQLite browser and so a
    /// provider that maps <c>char</c> to an integer code point cannot silently change its meaning.
    /// </summary>
    public string? RegionId { get; set; }

    /// <summary>SHA-256 of the original art blob on disk. Null until art has actually been fetched.</summary>
    public string? Sha256 { get; set; }

    public string? SourceUrl { get; set; }

    public string? ContentType { get; set; }

    /// <summary>Which source produced it: "gametdb" or "libretro".</summary>
    public string? Source { get; set; }

    /// <summary>
    /// Negative cache: no upstream had art, do not ask again before this instant. Persisted rather
    /// than held in memory because the miss set is structural - ~59.6% of the index carries no serial
    /// - and a restart must not mean re-hammering a volunteer-run upstream
    /// with the same known-dead lookups.
    /// </summary>
    public DateTime? MissUntil { get; set; }

    /// <summary>True once a cached original is attached to this key.</summary>
    public bool HasArt => !string.IsNullOrEmpty(Sha256);
}

public class ArtRecordConfiguration : IEntityTypeConfiguration<ArtRecord>
{
    public void Configure(EntityTypeBuilder<ArtRecord> builder)
    {
        builder.ToTable(nameof(ArtRecord));
        builder.HasKey(e => e.Id);

        // Derived from Sha256; a column would be a second copy of the same fact.
        builder.Ignore(e => e.HasArt);

        builder.Property(e => e.ConsoleType).HasConversion<string>();

        // NOCASE because keys arrive from URL paths, and "ASME" and "asme" are the same game.
        // Letting the DB fold them stops two spellings of one key from both being insertable and
        // the art path then serving whichever SQLite happened to return.
        builder.Property(e => e.Key).IsRequired().UseCollation("NOCASE");
        builder.Property(e => e.Serial).UseCollation("NOCASE");
        builder.Property(e => e.Sha256).UseCollation("NOCASE");

        // Serves the read that starts EVERY art request: "SELECT ... WHERE ConsoleType = @c AND
        // Key = @k". Unique because a key describes exactly one title, which is what makes the write
        // side an upsert rather than a duplicate nobody notices.
        builder.HasIndex(e => new { e.ConsoleType, e.Key }).IsUnique();
    }
}
