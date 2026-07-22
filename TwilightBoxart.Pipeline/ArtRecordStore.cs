using TwilightBoxart.Data;
using TwilightBoxart.Data.Entities;

namespace TwilightBoxart.Pipeline;

/// <summary>
/// Reads and writes the title space: the <see cref="ArtRecord"/> rows.
/// </summary>
/// <remarks>
/// This used to be a directory of JSON files with a ConcurrentDictionary in front. It is a table now
/// because <see cref="ArtRecord.MissUntil"/> is precisely the "negative-cache backoff, miss tracking"
/// that the cache design assigns to EF. What did NOT follow is the art itself: the blobs are still
/// files, because a PNG in a database row is a PNG you cannot serve cheaply.
///
/// Short-lived contexts, one per unit of work, from the factory - the house idiom.
/// </remarks>
public sealed class ArtRecordStore(IDbContextFactory<AppDbContext> dbFactory, ILogger<ArtRecordStore> logger)
{
    /// <summary>How many titles this instance has ever resolved. One indexed count, for the admin panel.</summary>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ArtRecords.CountAsync(ct);
    }

    /// <summary>The record for a key, or null when identify has never seen it. Starts every art request.</summary>
    public async Task<ArtRecord?> TryGetRecordAsync(
        ConsoleType console, string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ArtRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ConsoleType == console && r.Key == key, ct);
    }

    /// <summary>
    /// Loads (or creates) the record for a key, applies <paramref name="apply"/> and saves. Returns
    /// the saved entity, whose audit dates the caller usually needs.
    /// </summary>
    public async Task<ArtRecord> UpsertAsync(
        ConsoleType console, string key, Action<ArtRecord> apply, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var record = await LoadOrCreateAsync(db, console, key, ct);
        apply(record);

        try
        {
            await db.SaveChangesAsync(ct);
            return record;
        }
        catch (DbUpdateException ex)
        {
            // Read-then-insert is not atomic, so a concurrent POST /v2/identify and
            // GET /v2/art/{platform}/{key}.png for the same new key both create the row and the
            // unique index rejects the loser. Reload the winner's row and apply to that instead:
            // failing here would turn an otherwise successful fetch into a 500. Same pattern, and
            // same reasoning, as RememberIdentityAsync below.
            logger.LogDebug(ex, "Concurrent record write for {Console}/{Key}; reloading",
                console.Slug(), key);

            await using var retry = await dbFactory.CreateDbContextAsync(ct);
            var winner = await LoadOrCreateAsync(retry, console, key, ct);
            apply(winner);
            await retry.SaveChangesAsync(ct);
            return winner;
        }
    }

    /// <summary>
    /// Records what identify learned, without clobbering art already attached to the key. Called on
    /// every matched identify result, which is how a 16-hex digest key becomes resolvable later by an
    /// art request that carries nothing but the URL.
    /// </summary>
    public async Task RememberIdentityAsync(RomIdentity identity, CancellationToken ct = default)
    {
        if (!identity.IsMatched)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var record = await LoadOrCreateAsync(db, identity.ConsoleType, identity.Key, ct);

        // Null-coalescing rather than assignment throughout: identify runs repeatedly over the same
        // library and a later, thinner match (filename only, no header) must not erase what an
        // earlier, richer one established.
        record.Serial ??= identity.Serial;
        record.CanonicalName ??= identity.CanonicalName;
        record.Title ??= identity.Title;
        record.RegionId ??= identity.RegionId?.ToString();

        if (db.Entry(record).State == EntityState.Unchanged)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Two identify batches racing on the same new key: the unique index rejects the loser.
            // The winner wrote the same facts, so there is nothing to retry and nothing lost.
            logger.LogDebug(ex, "Concurrent identity write for {Console}/{Key}",
                identity.ConsoleType.Slug(), identity.Key);
        }
    }

    /// <summary>
    /// Deletes rows that carry nothing but an expired negative-cache stamp: no identity, no art, and
    /// a <see cref="ArtRecord.MissUntil"/> in the past. The next request for such a key recreates the
    /// row for free, so keeping them only grows the one table unknown-key traffic can grow unbounded.
    /// </summary>
    public async Task<int> PruneUnresolvedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        return await db.ArtRecords
            .Where(r => r.Sha256 == null && r.CanonicalName == null && r.Title == null &&
                        r.Serial == null && r.MissUntil != null && r.MissUntil < now)
            .ExecuteDeleteAsync(ct);
    }

    private static async Task<ArtRecord> LoadOrCreateAsync(
        AppDbContext db, ConsoleType console, string key, CancellationToken ct)
    {
        var record = await db.ArtRecords.FirstOrDefaultAsync(r => r.ConsoleType == console && r.Key == key, ct);
        if (record is not null)
        {
            return record;
        }

        record = new ArtRecord { ConsoleType = console, Key = key };
        db.ArtRecords.Add(record);
        return record;
    }
}
