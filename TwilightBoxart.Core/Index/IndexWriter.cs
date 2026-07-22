using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TwilightBoxart.Core.Index;

/// <summary>
/// Writes the generated No-Intro index.
///
/// <para><b>This file is a contract.</b> <c>SqliteMetadataIndex</c> in TwilightBoxart.Core reads the
/// schema below with raw <c>Microsoft.Data.Sqlite</c> and no EF model to reconcile against, so the DDL
/// here and the reader's SQL have to be kept in step by hand. Change <see cref="SchemaSql"/> and you
/// have changed the reader's world; bump <see cref="SchemaVersion"/> when you do.</para>
///
/// <para>There are no migrations, and that is the design: the index is a
/// generated artifact that CI replaces wholesale every month. You never migrate a file you overwrite.</para>
/// </summary>
public static class IndexWriter
{
    /// <summary>Written to <c>meta.schema</c>. Readers should refuse a value they were not built for.</summary>
    public const int SchemaVersion = 1;

    public const string DefaultFileName = "nointro.db";

    /// <summary>
    /// The complete schema.
    /// <para><c>crc32</c> is INTEGER because SQLite has no unsigned type: a CRC is stored as the signed
    /// 32-bit reinterpretation of its bits and the reader casts back. Storing it as the unsigned value
    /// in a 64-bit column would work too, but then half the corpus sorts differently between writer and
    /// reader, and nobody would notice until a range query appeared.</para>
    /// <para>The three lookup indexes are partial (<c>WHERE ... IS NOT NULL</c>). Roughly 60% of rows
    /// have no serial and NES rows have no serial at all, so the partial form keeps the index off
    /// tens of thousands of NULLs it could never answer a query from.</para>
    /// <para><c>entry_fts</c> is external-content FTS5 over <c>entry.name</c> with the trigram tokenizer.
    /// Baking it in at build time is what made SQLite good enough to keep: it is the direct answer to
    /// PostgreSQL's <c>pg_trgm</c>, which was the only real argument for a server.</para>
    /// </summary>
    public const string SchemaSql = """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);

        CREATE TABLE entry (
            id        INTEGER PRIMARY KEY,
            console   INTEGER NOT NULL,
            name      TEXT    NOT NULL,
            serial    TEXT    NULL,
            crc32     INTEGER NULL,
            sha1      TEXT    NULL,
            status    TEXT    NULL
        );
        """;

    /// <summary>Created after the bulk insert: building them up-front costs several times the runtime.</summary>
    public const string IndexSql = """
        CREATE INDEX ix_entry_crc32   ON entry(crc32)   WHERE crc32 IS NOT NULL;
        CREATE INDEX ix_entry_sha1    ON entry(sha1)    WHERE sha1  IS NOT NULL;
        CREATE INDEX ix_entry_serial  ON entry(console, serial) WHERE serial IS NOT NULL;
        """;

    public const string FtsSql =
        "CREATE VIRTUAL TABLE entry_fts USING fts5(name, content='entry', content_rowid='id', tokenize='trigram');";

    /// <summary>
    /// Builds the index at <paramref name="path"/>. Writes to a sibling <c>.tmp</c> and moves it into
    /// place only once VACUUM has succeeded, so a build that dies halfway leaves the previous
    /// index intact rather than a truncated one every client would treat as valid.
    /// </summary>
    /// <param name="path">Final path, conventionally <c>nointro.db</c>.</param>
    /// <param name="entries">Rows in write order; see <see cref="EntryDeduplicator.Order"/>. Row i gets id i+1.</param>
    /// <param name="version">The build stamp recorded in <c>meta.version</c>. ISO-8601.</param>
    /// <param name="provenance">
    /// Extra <c>meta</c> rows describing where the data came from (source URL template, per-DAT header
    /// versions, attribution), so a copy of the file found in the wild explains itself. Additive:
    /// readers only look up the keys they know.
    /// </param>
    /// <returns>The number of rows written.</returns>
    public static int Write(string path, IReadOnlyList<DatEntry> entries, string version,
        IReadOnlyDictionary<string, string>? provenance = null)
    {
        var temp = AtomicFile.TempPathFor(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        AtomicFile.TryDelete(temp);
        AtomicFile.TryDelete(temp + "-wal");
        AtomicFile.TryDelete(temp + "-shm");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = temp,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // No pooling: a pooled handle keeps the file open after Dispose, and on Windows the
            // File.Move that publishes the build then fails with a sharing violation.
            Pooling = false,
        }.ToString();

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            // Determinism knobs (the "same inputs, same bytes" requirement): an explicit page size, no
            // WAL sidecar files, and no auto-vacuum free-page map. With the insert order fixed by the
            // caller, the same SQLite build then lays the file out identically run to run.
            Execute(connection, "PRAGMA page_size = 4096;");
            Execute(connection, "PRAGMA journal_mode = DELETE;");
            Execute(connection, "PRAGMA auto_vacuum = NONE;");

            // Safe: this is a scratch file. If the machine dies mid-build there is nothing to recover,
            // because the file is only published by the atomic move at the end.
            Execute(connection, "PRAGMA synchronous = OFF;");

            Execute(connection, SchemaSql);
            InsertEntries(connection, entries);
            InsertMeta(connection, version, entries.Count, provenance);
            Execute(connection, IndexSql);

            Execute(connection, FtsSql);

            // External-content FTS5 holds no copy of the text; 'rebuild' walks entry and indexes it.
            Execute(connection, "INSERT INTO entry_fts(entry_fts) VALUES('rebuild');");

            // ANALYZE before VACUUM: ANALYZE writes sqlite_stat1, VACUUM then packs it with everything
            // else. The reverse order leaves the stats table scattered at the end of the file.
            Execute(connection, "ANALYZE;");
            Execute(connection, "VACUUM;");
        }

        AtomicFile.Commit(temp, path);
        return entries.Count;
    }

    private static void InsertEntries(SqliteConnection connection, IReadOnlyList<DatEntry> entries)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO entry (id, console, name, serial, crc32, sha1, status) VALUES ($id, $console, $name, $serial, $crc32, $sha1, $status);";

        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var console = command.Parameters.Add("$console", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var serial = command.Parameters.Add("$serial", SqliteType.Text);
        var crc32 = command.Parameters.Add("$crc32", SqliteType.Integer);
        var sha1 = command.Parameters.Add("$sha1", SqliteType.Text);
        var status = command.Parameters.Add("$status", SqliteType.Text);
        command.Prepare();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            id.Value = i + 1;
            console.Value = (int)entry.Console;
            name.Value = entry.Name;
            serial.Value = (object?)entry.Serial ?? DBNull.Value;

            // Signed 32-bit reinterpretation; the reader does unchecked((uint)value) to get it back.
            crc32.Value = entry.Crc32 is { } crc ? unchecked((int)crc) : DBNull.Value;
            sha1.Value = (object?)entry.Sha1 ?? DBNull.Value;
            status.Value = (object?)entry.Status ?? DBNull.Value;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void InsertMeta(SqliteConnection connection, string version, int rowCount,
        IReadOnlyDictionary<string, string>? provenance)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value);";
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Text);

        // Sorted by key so the rows land in a fixed order regardless of how this method evolves -
        // meta must not be the one table that breaks the same-inputs-same-bytes guarantee.
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["rowCount"] = rowCount.ToString(CultureInfo.InvariantCulture),
            ["schema"] = SchemaVersion.ToString(CultureInfo.InvariantCulture),
            ["version"] = version,
        };

        foreach (var (k, v) in provenance ?? new Dictionary<string, string>())
        {
            rows[k] = v;
        }

        foreach (var (k, v) in rows)
        {
            key.Value = k;
            value.Value = v;
            command.ExecuteNonQuery();
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
