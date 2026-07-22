// TWO DATABASES, DELIBERATELY.
//
// TwilightBoxart keeps its data in two SQLite files with completely different lifecycles:
//
//   1. nointro.db  - the generated No-Intro/LibRetro index (~44k rows, plus an FTS5 trigram table).
//      Built by the server itself (TwilightBoxart.Core.Index) on first boot and on demand, opened
//      read-only at runtime through Microsoft.Data.Sqlite with hand-written SQL. It is NOT in this
//      DbContext and has NO migrations, because it is REPLACED WHOLESALE rather than evolved: a
//      rebuild overwrites the file atomically. You never migrate a file you throw away. Putting it
//      behind EF would buy change tracking and a migration history for data that has neither, and
//      would cost the ability to swap the file under a running process.
//
//   2. twilightboxart.db - THIS context. Only state that genuinely evolves and is genuinely
//      relational: the title records and the cache bookkeeping. Small, mutable, migrated.
//
// This layer would only move to PostgreSQL under conditions that do not hold today: more than one
// app instance, a second writing process, or mutable tables past ~10M rows.

using System.Reflection;
using TwilightBoxart.Data.Entities;

namespace TwilightBoxart.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>The title space: one row per art key, holding the pointer to its cached original.</summary>
    public DbSet<ArtRecord> ArtRecords { get; set; } = null!;

    /// <summary>Bookkeeping for the two-layer art cache; drives LRU eviction.</summary>
    public DbSet<CacheEntry> CacheEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }

    private void SetAuditDates()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedDate = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedDate = now;
            }
        }
    }

    // The bool overloads are the funnel: the parameterless SaveChanges/SaveChangesAsync chain into
    // them, so overriding here stamps all four entry points.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetAuditDates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetAuditDates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
