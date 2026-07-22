using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;

namespace TwilightBoxart.Core.Identify;

/// <summary>
/// Read-only lookup over the generated No-Intro index (<c>nointro.db</c>).
///
/// This is deliberately raw <see cref="Microsoft.Data.Sqlite"/> rather than EF Core. The file is a
/// build artifact: the DatBuilder regenerates it wholesale from the published DATs and the deployment
/// swaps the file, so there is nothing to migrate and no mutable state to model. EF would only add a
/// change tracker and a translation layer to four hand-written statements on the hottest path in the
/// service.
///
/// Concurrency: a <see cref="SqliteConnection"/> and its compiled statements are not thread-safe, so
/// each caller rents a <see cref="Reader"/> holding its own connection with every statement already
/// prepared. Readers live for the lifetime of the index: the point is that a lookup never pays for
/// opening a connection or compiling SQL.
/// </summary>
public sealed class SqliteMetadataIndex : IMetadataIndex, IDisposable
{
    /// <summary>The only <c>meta.schema</c> value this reader understands.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Minimum similarity, measured on the title stem, for <see cref="SearchByName"/> to return a row.
    ///
    /// Filename matching is the last rung of the ladder and the one that can be confidently wrong: FTS5
    /// always has *some* best row, and a plausible-looking wrong cover is worse for the user than a
    /// blank one, because a blank one is obviously something to fix. The coverage numbers make the same
    /// point from the other side: ~59.6% of DAT rows carry no serial, so misses are structural and the
    /// product answer is one-click manual correction, not a looser threshold.
    ///
    /// The threshold is applied to the <i>stem</i> only (everything before the first bracket), with the
    /// parenthesised tags used solely to break ties between equally good stems. Scoring the whole string
    /// at once conflates two very different differences: "Castlevania (USA)" against
    /// "Castlevania (USA) (Rev 1)" is the same game, while "Super Mario Bros." against
    /// "Super Mario Bros. 2" is not, yet a character metric scores the harmless one *further* apart
    /// because "(Rev 1)" is longer than "2". Splitting first puts the threshold on the part that carries
    /// the meaning, and lets the tags do what they are actually good at: choosing between the region and
    /// revision variants of a title that has already matched.
    /// </summary>
    public const double MinimumNameConfidence = 0.80;

    // How many FTS candidates to rescore. The trigram index ranks by bm25, which is a relevance
    // ordering rather than a similarity, so the real decision is made by rescoring a shortlist.
    private const int CandidateLimit = 12;

    // FTS5's trigram tokenizer emits nothing at all for a term shorter than three characters, so a
    // two-letter term in a MATCH expression matches no rows and would veto the whole AND query.
    private const int MinTermLength = 3;

    // Long names are all tail: "(USA, Europe) (Rev 1) (Beta) (Proto)". Past a handful of terms the
    // query stops discriminating and starts costing, and an unbounded caller-supplied name is a cheap
    // way to make the server do quadratic work.
    private const int MaxTerms = 8;
    private const int MaxNameLength = 200;

    private const string Projection = "console, name, serial, crc32, sha1";

    private const string CrcSql = $"SELECT {Projection} FROM entry WHERE crc32 = $crc LIMIT 1;";
    private const string Sha1Sql = $"SELECT {Projection} FROM entry WHERE sha1 = $sha1 LIMIT 1;";
    private const string SerialSql =
        $"SELECT {Projection} FROM entry WHERE console = $console AND serial = $serial LIMIT 1;";

    // $console = 0 (ConsoleType.Unknown) means "every partition"; see the remarks on SearchByName.
    private static readonly string NameSql = $"""
        SELECT e.console, e.name, e.serial, e.crc32, e.sha1
        FROM entry_fts
        JOIN entry e ON e.id = entry_fts.rowid
        WHERE entry_fts MATCH $q AND ($console = 0 OR e.console = $console)
        ORDER BY bm25(entry_fts)
        LIMIT {CandidateLimit};
        """;

    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly ConcurrentBag<Reader> _readers = [];
    private readonly SemaphoreSlim _slots;
    private volatile bool _disposed;

    /// <summary>ISO-8601 build stamp of the index file, from <c>meta.version</c>.</summary>
    public string Version { get; }

    /// <summary>Rows in <c>entry</c>, from <c>meta.rowCount</c>.</summary>
    public int RowCount { get; }

    /// <summary>Absolute path of the index file that was opened.</summary>
    public string Path { get; }

    /// <summary>
    /// Opens an existing index. Throws if the file is missing, is not our schema, or is unreadable;
    /// prefer <see cref="Open"/>, which degrades to a null-object index instead of taking the app down.
    /// </summary>
    public SqliteMetadataIndex(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _logger = logger;
        Path = System.IO.Path.GetFullPath(path);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            // Read-only is a correctness statement as much as a permission one: nothing in the running
            // service may write to a file the next deployment replaces wholesale.
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            // We pool connections ourselves and hold them for the index's lifetime, so ADO.NET pooling
            // would add nothing except keeping the file handle alive past Dispose, which on Windows
            // blocks the deployment that swaps the file.
            Pooling = false,
        }.ConnectionString;

        // Two per core: lookups are microseconds and never block on I/O once the pages are cached, so
        // this exists to bound connections under a request spike, not to unlock parallelism.
        _slots = new SemaphoreSlim(Math.Max(4, Environment.ProcessorCount * 2));

        var reader = CreateReader();
        try
        {
            (Version, RowCount) = ReadMeta(reader.Connection);
        }
        catch
        {
            reader.Dispose();
            _slots.Dispose();
            throw;
        }

        _readers.Add(reader);
        _logger.LogInformation(
            "No-Intro index opened: {Path}, version {Version}, {RowCount} rows.", Path, Version, RowCount);
    }

    /// <summary>
    /// Opens the index, falling back to a <see cref="NullMetadataIndex"/> that misses everything when
    /// the file is absent or unusable.
    ///
    /// The fallback is not cosmetic. DS, DSi and GBA carry a title id in the ROM header and their art
    /// is keyed on exactly that, so the serial-bearing platforms (the majority of what TWiLightMenu++
    /// users actually have) keep working with no index at all. Booting and serving those is strictly
    /// better than refusing to start, provided the operator cannot miss what happened.
    /// </summary>
    public static IMetadataIndex Open(string path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogError("No No-Intro index path configured. Only header-serial matching will work.");
            return new NullMetadataIndex("no path configured");
        }

        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            // Warning, not Error, and deliberately free of any "run X to fix it" remedy. On a server
            // this is the ordinary first-boot state: IndexBuildService sees the same absent file and
            // starts building one, so an operator who reads "ERROR ... restore the file" goes looking
            // for a problem that fixes itself seconds later. The genuinely bad outcomes still shout:
            // a build that fails logs its own error, and an index present but unreadable is Critical
            // below. Callers that DO know a remedy (the server knows it is about to build one) say so
            // themselves rather than having Core guess on their behalf.
            logger.LogWarning(
                "No-Intro index not found at {Path}. Serial-bearing platforms (DS/DSi/GBA) still " +
                "resolve from the ROM header; CRC32, SHA-1 and filename matching stay disabled until " +
                "an index is available.", full);
            return new NullMetadataIndex($"file not found: {full}");
        }

        try
        {
            return new SqliteMetadataIndex(full, logger);
        }
        catch (Exception e)
        {
            logger.LogCritical(
                e,
                "No-Intro index at {Path} could not be opened and will be ignored. CRC32, SHA-1 and " +
                "filename matching are all disabled.", full);
            return new NullMetadataIndex($"unreadable: {e.Message}");
        }
    }

    /// <inheritdoc />
    public bool TryByCrc32(uint crc32, out IndexEntry entry)
    {
        // A CRC of 0 is a legitimate lookup here; it is the *caller* that must know a 7z reporting 0
        // means "not recorded" rather than "zero". IdentificationLadder does.
        var reader = Rent();
        try
        {
            // SQLite INTEGER is signed, so the builder stores the CRC reinterpreted as int32. Undo the
            // same reinterpretation on the way in, or every CRC with the high bit set misses.
            reader.ByCrc.Parameters[0].Value = unchecked((int)crc32);
            return TryReadOne(reader.ByCrc, out entry);
        }
        finally
        {
            ReturnReader(reader);
        }
    }

    /// <inheritdoc />
    public bool TryBySha1(string sha1, out IndexEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(sha1))
        {
            return false;
        }

        // Stored lowercase hex. ToLowerInvariant, never ToLower - see ArchiveEntrySelector for the
        // Turkish-locale story.
        var normalized = sha1.Trim().ToLowerInvariant();

        var reader = Rent();
        try
        {
            reader.BySha1.Parameters[0].Value = normalized;
            return TryReadOne(reader.BySha1, out entry);
        }
        finally
        {
            ReturnReader(reader);
        }
    }

    /// <inheritdoc />
    public bool TryBySerial(ConsoleType console, string serial, out IndexEntry entry)
    {
        entry = null!;
        if (console is ConsoleType.Unknown || string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        // Serials are uppercase throughout the No-Intro DATs and ix_entry_serial is a plain BINARY
        // index, so normalising here keeps the lookup on the index instead of forcing a COLLATE
        // NOCASE override that would drop it to a scan.
        var normalized = serial.Trim().ToUpperInvariant();

        var reader = Rent();
        try
        {
            reader.BySerial.Parameters[0].Value = (int)console;
            reader.BySerial.Parameters[1].Value = normalized;
            return TryReadOne(reader.BySerial, out entry);
        }
        finally
        {
            ReturnReader(reader);
        }
    }

    /// <summary>
    /// Fuzzy name lookup over the FTS5 trigram index, returning null below
    /// <see cref="MinimumNameConfidence"/>.
    /// </summary>
    /// <param name="console">
    /// Partition to search, or <see cref="ConsoleType.Unknown"/> to search every partition. The
    /// wildcard matters because the caller often reaches this rung precisely because header detection
    /// failed; narrowing by console when it is known is what stops "Mario Bros." resolving to whichever
    /// of its six platforms bm25 happens to rank first.
    /// </param>
    /// <param name="name">
    /// The ROM's own file name. For an archive this must be the inner entry name, never the archive's
    /// own name.
    /// </param>
    public IndexEntry? SearchByName(ConsoleType console, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var query = NameParts.From(name);
        var terms = Terms(query.Full);
        if (terms.Count == 0)
        {
            // Nothing three characters or longer survived, so the trigram index cannot say anything.
            return null;
        }

        var reader = Rent();
        try
        {
            reader.ByName.Parameters[1].Value = (int)console;

            // Precision first: require every term. A No-Intro name carries tags the user's filename
            // will not have, but the reverse (a scene tag like "goodnes" in the filename that appears
            // nowhere in the DAT) would veto an otherwise perfect AND match, so fall back to ANY.
            return Best(reader, query, BuildMatch(terms, " AND "))
                ?? Best(reader, query, BuildMatch(terms, " OR "));
        }
        catch (SqliteException e)
        {
            // Belt and braces alongside the quoting in BuildMatch: a malformed MATCH is a bad request,
            // not a server fault, and a fuzzy lookup failing is just a miss.
            _logger.LogWarning(e, "FTS5 name lookup failed for {Name}; treating as a miss.", name);
            return null;
        }
        finally
        {
            ReturnReader(reader);
        }
    }

    private IndexEntry? Best(Reader reader, NameParts query, string match)
    {
        reader.ByName.Parameters[0].Value = match;

        IndexEntry? best = null;
        var bestStem = 0d;
        var bestTags = 0d;

        using var rows = reader.ByName.ExecuteReader();
        while (rows.Read())
        {
            var candidate = Read(rows);
            var parts = NameParts.From(candidate.Name);

            if (SequelMismatch(query.Stem, parts.Stem))
            {
                continue;
            }

            var stem = Similarity(query.Stem, parts.Stem);

            // Tags only ever decide between candidates whose titles are equally good: they pick the
            // (USA) row over the (Japan) one, and never promote a row past the threshold.
            var tags = Similarity(query.Tags, parts.Tags);

            // Strictly greater on both, so a total tie keeps bm25's ordering.
            if (stem > bestStem || (stem == bestStem && tags > bestTags))
            {
                bestStem = stem;
                bestTags = tags;
                best = candidate;
            }
        }

        if (best is null || bestStem < MinimumNameConfidence)
        {
            return null;
        }

        _logger.LogDebug(
            "Name match {Query} -> {Name} (title {Stem:F2}, tags {Tags:F2}).",
            query.Full, best.Name, bestStem, bestTags);
        return best;
    }

    /// <summary>
    /// True when two titles are separated by a sequel number, which no similarity metric can be trusted
    /// to catch: "Super Mario Bros." and "Super Mario Bros. 2" differ by one character and score 0.90,
    /// while the harmless "Castlevania" against "Castlevania (Rev 1)" scores lower. Sequel numbering is
    /// the single most common way two genuinely different games share a title, so it gets an explicit
    /// rule rather than a threshold that would have to be set high enough to reject real matches too.
    ///
    /// A trailing number present on one side and absent on the other counts as a mismatch, because that
    /// is the same situation seen from the other end.
    /// </summary>
    private static bool SequelMismatch(string stemA, string stemB) =>
        !string.Equals(TrailingOrdinal(stemA), TrailingOrdinal(stemB), StringComparison.Ordinal);

    /// <summary>
    /// Rewrites a trailing roman numeral as arabic, so "Final Fantasy VI" and "Final Fantasy 6" score as
    /// the same title. DAT and file names disagree about this constantly, and the difference is large
    /// enough on a character metric ("y vi" against "y 6") to sink an otherwise perfect match on its own.
    /// </summary>
    private static string FoldTrailingRoman(string stem)
    {
        var space = stem.LastIndexOf(' ');
        if (space < 0)
        {
            return stem;
        }

        return RomanNumerals.TryGetValue(stem[(space + 1)..], out var arabic)
            ? string.Concat(stem.AsSpan(0, space + 1), arabic)
            : stem;
    }

    /// <summary>
    /// The trailing sequel number of a title, or empty when it has none. Only ever taken when a title
    /// precedes it, so a game actually called "1942" keeps its whole name.
    /// </summary>
    private static string TrailingOrdinal(string stem)
    {
        var space = stem.LastIndexOf(' ');
        if (space < 0)
        {
            return string.Empty;
        }

        var last = stem[(space + 1)..];
        if (last.Length is 0 or > 4)
        {
            return string.Empty;
        }

        if (RomanNumerals.TryGetValue(last, out var arabic))
        {
            return arabic;
        }

        return last.All(char.IsAsciiDigit) ? last.TrimStart('0') : string.Empty;
    }

    // 1-10 only: past that a trailing roman numeral in a game title is far more likely to be a word.
    private static readonly Dictionary<string, string> RomanNumerals = new(StringComparer.Ordinal)
    {
        ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5",
        ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    };

    /// <summary>
    /// Blended token/trigram Dice similarity in [0, 1].
    ///
    /// Token overlap alone is brittle around punctuation ("Bros." vs "Bros") and trigram overlap alone
    /// is too forgiving about a missing word, which is the exact failure mode that matters, because
    /// "Super Mario Bros" and "Super Mario Bros 2" are different games. Averaging them lets each cover
    /// the other's blind spot, and the trigram half deliberately mirrors what the FTS5 index itself is
    /// built on.
    /// </summary>
    internal static double Similarity(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1d;
        }

        return (Dice(Tokens(a), Tokens(b)) + Dice(Trigrams(a), Trigrams(b))) / 2d;
    }

    private static double Dice(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0d;
        }

        var shared = a.Count(b.Contains);
        return 2d * shared / (a.Count + b.Count);
    }

    private static HashSet<string> Tokens(string normalized) =>
        new(normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static HashSet<string> Trigrams(string normalized)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 3 <= normalized.Length; i++)
        {
            set.Add(normalized.Substring(i, 3));
        }

        return set;
    }

    /// <summary>
    /// A name split into the title and its No-Intro tags, both normalised. "Castlevania (USA) (Rev 1)"
    /// becomes stem "castlevania", tags "usa rev 1".
    /// </summary>
    private readonly record struct NameParts(string Stem, string Tags, string Full)
    {
        public static NameParts From(string name)
        {
            var value = StripExtension(name.Trim());
            if (value.Length > MaxNameLength)
            {
                value = value[..MaxNameLength];
            }

            // Everything from the first bracket on is metadata: region, revision, dump status, scene
            // tags. No-Intro is rigidly consistent about this, which is what makes the split reliable.
            var cut = value.AsSpan().IndexOfAny('(', '[');
            var stem = FoldTrailingRoman(NormalizeName(cut >= 0 ? value[..cut] : value));
            var tags = NormalizeName(cut >= 0 ? value[cut..] : string.Empty);

            // A name that is nothing but tags still has to be scored on something.
            return new NameParts(stem.Length > 0 ? stem : tags, tags, NormalizeName(value));
        }

        private static string StripExtension(string value)
        {
            // Only strip a suffix we actually recognise. SupportedFiles is OrdinalIgnoreCase, so ".NDS"
            // and ".nds" both go; blind Path.GetExtension would eat the "1.1" out of "Zelda v1.1".
            var extension = System.IO.Path.GetExtension(value);
            return extension.Length > 0 &&
                   (SupportedFiles.Rom.Contains(extension) || SupportedFiles.Archive.Contains(extension))
                ? value[..^extension.Length]
                : value;
        }
    }

    /// <summary>
    /// Folds a name fragment to the shape both sides are scored in: lowercase invariant, alphanumerics
    /// only, single-spaced.
    /// </summary>
    private static string NormalizeName(string value)
    {
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = sb.Length > 0;
            }
        }

        return sb.ToString();
    }

    private static List<string> Terms(string normalized)
    {
        var terms = new List<string>(MaxTerms);
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < MinTermLength || terms.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            terms.Add(token);
            if (terms.Count == MaxTerms)
            {
                break;
            }
        }

        return terms;
    }

    /// <summary>
    /// Builds an FTS5 MATCH expression. Every term is emitted as a quoted string literal with any
    /// embedded double quote doubled, which is FTS5's own escape and the only thing that keeps a name
    /// like <c>Mario" OR name:"</c> from being parsed as query syntax. NormalizeName has already
    /// removed everything but ASCII alphanumerics; the quoting stays because that is the invariant a
    /// future change to NormalizeName must not be able to break silently.
    /// </summary>
    private static string BuildMatch(List<string> terms, string op)
    {
        var sb = new StringBuilder();
        foreach (var term in terms)
        {
            if (sb.Length > 0)
            {
                sb.Append(op);
            }

            sb.Append('"').Append(term.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        return sb.ToString();
    }

    private static bool TryReadOne(SqliteCommand command, out IndexEntry entry)
    {
        using var rows = command.ExecuteReader();
        if (!rows.Read())
        {
            entry = null!;
            return false;
        }

        entry = Read(rows);
        return true;
    }

    private static IndexEntry Read(SqliteDataReader rows)
    {
        var console = rows.GetInt32(0);
        return new IndexEntry(
            // A value outside the enum would otherwise travel all the way into an art URL as a number.
            // Surfacing it as Unknown keeps a DatBuilder mistake from becoming a broken public route.
            Enum.IsDefined((ConsoleType)console) ? (ConsoleType)console : ConsoleType.Unknown,
            rows.GetString(1),
            rows.IsDBNull(2) ? null : rows.GetString(2),
            rows.IsDBNull(3) ? null : unchecked((uint)rows.GetInt32(3)),
            rows.IsDBNull(4) ? null : rows.GetString(4));
    }

    private (string Version, int RowCount) ReadMeta(SqliteConnection connection)
    {
        string? version = null;
        string? rowCount = null;
        string? schema = null;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM meta;";
            using var rows = cmd.ExecuteReader();
            while (rows.Read())
            {
                switch (rows.GetString(0))
                {
                    case "version": version = rows.GetString(1); break;
                    case "rowCount": rowCount = rows.GetString(1); break;
                    case "schema": schema = rows.GetString(1); break;
                }
            }
        }

        // Refuse rather than guess. The reader and the builder share one hand-written schema, so a
        // version this reader has never seen means the column meanings are unknown, and silently
        // reading them the old way would produce confident nonsense.
        if (!int.TryParse(schema, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schemaVersion) ||
            schemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Index schema '{schema ?? "(missing)"}' is not supported; this build reads schema {SupportedSchemaVersion}.");
        }

        if (!int.TryParse(rowCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows2))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM entry;";
            rows2 = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return (version ?? "unknown", rows2);
    }

    private Reader Rent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _slots.Wait();
        try
        {
            return _readers.TryTake(out var reader) ? reader : CreateReader();
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    private void ReturnReader(Reader reader)
    {
        if (_disposed)
        {
            reader.Dispose();
        }
        else
        {
            _readers.Add(reader);
        }

        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // Dispose won the race while this lookup was in flight. The lookup itself completed on a
            // live connection; throwing out of its finally would replace that answer with a 500.
        }
    }

    private Reader CreateReader()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        try
        {
            return new Reader(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        while (_readers.TryTake(out var reader))
        {
            reader.Dispose();
        }

        _slots.Dispose();
    }

    /// <summary>One connection with all four statements compiled and their parameters bound once.</summary>
    private sealed class Reader : IDisposable
    {
        public SqliteConnection Connection { get; }
        public SqliteCommand ByCrc { get; }
        public SqliteCommand BySha1 { get; }
        public SqliteCommand BySerial { get; }
        public SqliteCommand ByName { get; }

        public Reader(SqliteConnection connection)
        {
            Connection = connection;
            ByCrc = Prepared(connection, CrcSql, "$crc");
            BySha1 = Prepared(connection, Sha1Sql, "$sha1");
            BySerial = Prepared(connection, SerialSql, "$console", "$serial");
            ByName = Prepared(connection, NameSql, "$q", "$console");
        }

        private static SqliteCommand Prepared(SqliteConnection connection, string sql, params string[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var name in parameters)
            {
                // Seeded so Prepare has a fully bound statement; every call overwrites Value in place.
                command.Parameters.AddWithValue(name, DBNull.Value);
            }

            command.Prepare();
            return command;
        }

        public void Dispose()
        {
            ByCrc.Dispose();
            BySha1.Dispose();
            BySerial.Dispose();
            ByName.Dispose();
            Connection.Dispose();
        }
    }
}

/// <summary>
/// Stand-in index used when <c>nointro.db</c> is missing or unreadable: every lookup misses.
///
/// It exists so a missing build artifact degrades the service instead of stopping it. DS, DSi and GBA
/// resolve from the ROM header's title id and need no index at all, so the platforms that make up most
/// of a TWiLightMenu++ card keep working. <see cref="Reason"/> is carried so a health endpoint can say
/// why rather than just reporting zero rows.
/// </summary>
public sealed class NullMetadataIndex(string reason) : IMetadataIndex
{
    /// <summary>Why the real index is not in use, for health reporting and logs.</summary>
    public string Reason { get; } = reason;

    public bool TryByCrc32(uint crc32, out IndexEntry entry)
    {
        entry = null!;
        return false;
    }

    public bool TryBySha1(string sha1, out IndexEntry entry)
    {
        entry = null!;
        return false;
    }

    public bool TryBySerial(ConsoleType console, string serial, out IndexEntry entry)
    {
        entry = null!;
        return false;
    }

    public IndexEntry? SearchByName(ConsoleType console, string name) => null;

    public string Version => "absent";

    public int RowCount => 0;
}
