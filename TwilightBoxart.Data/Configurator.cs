using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwilightBoxart.Data;

public static class Configurator
{
    // Named for what it registers: a factory, NOT a scoped AppDbContext. Every store resolves
    // IDbContextFactory<AppDbContext> and takes a short-lived context per unit of work, which is what
    // lets the background services (cache eviction, the hit-count flush) create contexts with no request
    // scope. Deliberately not called AddDbContext: that would shadow EF's own extension of the same name
    // and read as if a scoped context were being registered, which it is not.
    public static void AddAppDbContextFactory(this IServiceCollection services)
    {
        services.AddDbContextFactory<AppDbContext>(Configure);
    }

    private static void Configure(IServiceProvider sp, DbContextOptionsBuilder options)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var connectionString = config.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        options.UseSqlite(connectionString,
            o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }

    public static async Task Initialize(this AppDbContext context, ILogger? logger = null)
    {
        // SQLite creates the file but not the directory above it. On a first run the data volume is
        // an empty mount and this is the difference between "migrated" and "unable to open database".
        var directory = Path.GetDirectoryName(context.Database.GetDbConnection().DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await BackupBeforeMigration(context, logger);
        await context.Database.MigrateAsync();
        // WAL keeps art reads (the hot path) from blocking the periodic cache-hit flush and the
        // eviction sweep, which are the only writers in the common case.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    /// <summary>
    /// Snapshots the database next to itself as <c>.bak</c> before a migration runs, and only then.
    /// A failed migration on a hobby deployment is otherwise unrecoverable.
    /// </summary>
    private static async Task BackupBeforeMigration(AppDbContext context, ILogger? logger)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        logger?.LogInformation("Applying {Count} pending migration(s): {Names}",
            pending.Count, string.Join(", ", pending));

        var dbPath = context.Database.GetDbConnection().DataSource;
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            return;
        }

        var backupPath = dbPath + ".bak";
        // Not File.Copy: the database runs in WAL mode, so committed transactions can still sit in
        // the -wal file beside the main one, and a copy of the main file alone would miss them.
        // VACUUM INTO writes one consistent snapshot - but refuses to overwrite, so any stale
        // backup goes first.
        File.Delete(backupPath);
        await context.Database.ExecuteSqlAsync($"VACUUM INTO {backupPath}");
        logger?.LogInformation("Database backed up to {BackupPath}", backupPath);
    }
}
