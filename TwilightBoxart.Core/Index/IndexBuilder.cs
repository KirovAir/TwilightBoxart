using System.Globalization;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>
/// Runs a whole build: gather DATs, parse, dedupe, order, write the index. Driven by the backend -
/// on first boot and from the admin panel - and by the tests; logging is a callback so the caller
/// decides where the narration goes.
/// </summary>
public sealed class IndexBuilder(BuildOptions options, Action<string> log)
{
    /// <summary>
    /// <c>meta</c> rows that make the artifact self-describing: which DAT versions went in and where
    /// they came from. A nointro.db copied onto an SD card carries its own provenance.
    /// </summary>
    private readonly SortedDictionary<string, string> _provenance = new(StringComparer.Ordinal);

    public async Task<BuildResult> RunAsync(CancellationToken ct = default)
    {
        var catalog = options.SourcesPath is null ? DatCatalog.Default : DatCatalog.Load(options.SourcesPath);
        if (options.SourcesPath is not null)
        {
            log($"Sources: {options.SourcesPath} ({catalog.Sources.Count} entries)");
        }

        var (parsed, missing) = options.InputDirectory is null
            ? await DownloadAsync(catalog, ct)
            : ReadLocalDirectory(catalog, options.InputDirectory);

        if (missing.Count > 0 && options.Strict)
        {
            throw new InvalidOperationException(
                $"--strict: {missing.Count} source(s) could not be read: {string.Join(", ", missing)}");
        }

        if (parsed.Count == 0)
        {
            throw new InvalidOperationException("No DAT rows were parsed; refusing to publish an empty index.");
        }

        log("");
        log(string.Create(CultureInfo.InvariantCulture, $"Parsed {parsed.Count:N0} rows. Deduplicating.."));
        var (deduped, dedupeReport) = EntryDeduplicator.Deduplicate(parsed);
        log(string.Create(CultureInfo.InvariantCulture,
            $"  {dedupeReport.InputRows:N0} -> {dedupeReport.OutputRows:N0} rows"));

        var ordered = EntryDeduplicator.Order(deduped);

        // "local" rather than the input path: a machine-specific directory has no business inside a
        // file that gets served and copied around.
        _provenance["attribution"] = "Identification data derived from No-Intro DAT files " +
            "(https://no-intro.org), fetched via the libretro-database mirror.";
        _provenance["source"] = options.InputDirectory is null ? options.BaseUrlTemplate : "local";

        log($"Writing {options.OutputPath} (version {options.Version})..");
        var rowCount = IndexWriter.Write(options.OutputPath, ordered, options.Version, _provenance);

        return new BuildResult
        {
            Version = options.Version,
            RowCount = rowCount,
            DatabasePath = Path.GetFullPath(options.OutputPath),
            Dedupe = dedupeReport,

            // Console coverage describes the index as built. Source coverage is measured on the raw
            // parse instead, because it exists to be compared against the recorded coverage baseline --
            // and that baseline counted serials in the DAT, before the builder suppressed the
            // non-discriminating ones.
            Coverage = BuildReport.Measure(ordered),
            SourceCoverage = BuildReport.MeasureBySource(parsed),
            MissingSources = missing,
        };
    }

    private async Task<(List<DatEntry> Entries, List<string> Missing)> DownloadAsync(DatCatalog catalog, CancellationToken ct)
    {
        var entries = new List<DatEntry>();
        var missing = new List<string>();

        using var fetcher = new DatFetcher(options.CacheDirectory);

        foreach (var source in catalog.Sources)
        {
            FetchedDat? fetched;
            try
            {
                fetched = await fetcher.FetchAsync(source, options.BaseUrlTemplate, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                // One unreachable mirror should not cost the other eleven consoles their index.
                log($"  ! {source.Name}: {ex.Message}");
                missing.Add(source.Name);
                continue;
            }

            if (fetched is null)
            {
                var note = source.Optional ? "not published by this mirror" : "NOT FOUND";
                log($"  {(source.Optional ? "-" : "!")} {source.Name}: {note}");
                if (!source.Optional)
                {
                    missing.Add(source.Name);
                }

                continue;
            }

            entries.AddRange(Ingest(source.Name, source.Console, fetched));
        }

        return (entries, missing);
    }

    private (List<DatEntry> Entries, List<string> Missing) ReadLocalDirectory(DatCatalog catalog, string directory)
    {
        var entries = new List<DatEntry>();
        var missing = new List<string>();
        var seenConsoles = new HashSet<ConsoleType>();

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"--input directory '{directory}' does not exist.");
        }

        foreach (var path in DatFetcher.EnumerateLocalDats(directory))
        {
            var fetched = DatFetcher.ReadLocal(path);

            // Resolve by file stem first, then by the DAT's own declared header name. Stems get renamed
            // by mirrors and by hand; the header is the DAT's own claim about what it is. The header
            // route is a throwaway full parse whose rows are discarded, which a CI build can afford
            // for the odd renamed file.
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!catalog.TryResolve(stem, out var console) &&
                !catalog.TryResolve(DatParser.Parse(fetched.Text, ConsoleType.Unknown, stem).HeaderName, out console))
            {
                log($"  ? {Path.GetFileName(path)}: no console mapping, skipped");
                continue;
            }

            entries.AddRange(Ingest(stem, console, fetched));
            seenConsoles.Add(console);
        }

        foreach (var source in catalog.Sources.Where(s => !s.Optional && !seenConsoles.Contains(s.Console)))
        {
            log($"  ! {source.Name}: no DAT found in {directory}");
            missing.Add(source.Name);
        }

        return (entries, missing);
    }

    private IReadOnlyList<DatEntry> Ingest(string sourceName, ConsoleType console, FetchedDat fetched)
    {
        var document = DatParser.Parse(fetched.Text, console, sourceName);

        var version = document.HeaderVersion is null
            ? ""
            : $"  dat-version {document.HeaderVersion}";

        if (document.HeaderVersion is not null)
        {
            _provenance[$"dat:{sourceName}"] = document.HeaderVersion;
        }

        log(string.Create(CultureInfo.InvariantCulture,
            $"  {console.Slug(),-5} {sourceName,-48} {document.GameCount,7:N0} games  {document.Entries.Count,7:N0} roms{version}"));

        // A mismatch means the mapping is stale - the DAT thinks it is something else, and every row it
        // contributes is about to be filed under the wrong console.
        if (document.HeaderName is not null &&
            !string.Equals(document.HeaderName, sourceName, StringComparison.OrdinalIgnoreCase))
        {
            log($"    ! DAT declares itself '{document.HeaderName}' but was mapped from '{sourceName}'");
        }

        return document.Entries;
    }
}
