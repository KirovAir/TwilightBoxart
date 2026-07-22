using System.Globalization;
using Microsoft.Data.Sqlite;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests.Fixtures;

/// <summary>A row to seed into the generated index.</summary>
internal sealed record IndexRow(
    ConsoleType Console,
    string Name,
    string? Serial = null,
    uint? Crc32 = null,
    string? Sha1 = null,
    string? Status = null);

/// <summary>
/// Builds a real, file-backed <c>nointro.db</c> with the exact schema the DatBuilder emits, for tests.
/// </summary>
/// <remarks>
/// Deliberately a file on disk and not <c>:memory:</c>. The subject under test opens the index
/// <c>Mode=ReadOnly</c> by path, so an in-memory database could not exercise it at all, and the schema
/// here is the written contract between the builder and the reader, so a test that stubs it would only
/// be testing itself. FTS5, its trigram tokenizer and the partial indexes are all real here.
/// </remarks>
internal sealed class NoIntroIndexFile : IDisposable
{
    private const string Schema = """
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
        CREATE INDEX ix_entry_crc32   ON entry(crc32)   WHERE crc32 IS NOT NULL;
        CREATE INDEX ix_entry_sha1    ON entry(sha1)    WHERE sha1  IS NOT NULL;
        CREATE INDEX ix_entry_serial  ON entry(console, serial) WHERE serial IS NOT NULL;
        CREATE VIRTUAL TABLE entry_fts USING fts5(name, content='entry', content_rowid='id', tokenize='trigram');
        """;

    private readonly string _directory;

    /// <summary>Full path of the generated index file.</summary>
    public string Path { get; }

    private NoIntroIndexFile(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    public static NoIntroIndexFile Create(params IndexRow[] rows) => Create("2026-07-20T00:00:00Z", 1, rows);

    public static NoIntroIndexFile Create(string version, int schemaVersion, params IndexRow[] rows)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "twilight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = System.IO.Path.Combine(directory, "nointro.db");
        var fixture = new NoIntroIndexFile(directory, path);

        // Pooling off so Dispose actually closes the handle and the temp directory can be deleted.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        Execute(connection, Schema);

        using (var transaction = connection.BeginTransaction())
        {
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText =
                    "INSERT INTO entry (console, name, serial, crc32, sha1, status) " +
                    "VALUES ($console, $name, $serial, $crc32, $sha1, $status);";

                var console = insert.Parameters.Add("$console", SqliteType.Integer);
                var name = insert.Parameters.Add("$name", SqliteType.Text);
                var serial = insert.Parameters.Add("$serial", SqliteType.Text);
                var crc32 = insert.Parameters.Add("$crc32", SqliteType.Integer);
                var sha1 = insert.Parameters.Add("$sha1", SqliteType.Text);
                var status = insert.Parameters.Add("$status", SqliteType.Text);

                foreach (var row in rows)
                {
                    console.Value = (int)row.Console;
                    name.Value = row.Name;
                    serial.Value = (object?)row.Serial?.ToUpperInvariant() ?? DBNull.Value;
                    // Signed storage, exactly as the schema specifies and the reader expects.
                    crc32.Value = row.Crc32 is { } c ? unchecked((int)c) : DBNull.Value;
                    sha1.Value = (object?)row.Sha1?.ToLowerInvariant() ?? DBNull.Value;
                    status.Value = (object?)row.Status ?? DBNull.Value;
                    insert.ExecuteNonQuery();
                }
            }

            // entry_fts is an external-content table, so it holds no data until it is told to index the
            // content table. The DatBuilder must do this too.
            Execute(connection, "INSERT INTO entry_fts(entry_fts) VALUES('rebuild');");

            using (var meta = connection.CreateCommand())
            {
                meta.CommandText = "INSERT INTO meta (key, value) VALUES ('version', $version), " +
                                   "('rowCount', $rowCount), ('schema', $schema);";
                meta.Parameters.AddWithValue("$version", version);
                meta.Parameters.AddWithValue(
                    "$rowCount", rows.Length.ToString(CultureInfo.InvariantCulture));
                meta.Parameters.AddWithValue(
                    "$schema", schemaVersion.ToString(CultureInfo.InvariantCulture));
                meta.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return fixture;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run; the OS sweeps it up.
        }
    }
}
