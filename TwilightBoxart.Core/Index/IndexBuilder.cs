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

        log("");
        log("Resolving box art names against libretro-thumbnails..");
        var (named, unresolvedArt) = await ResolveArtNamesAsync(ordered, ct);
        ordered = named;

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
            UnresolvedArtConsoles = unresolvedArt
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

    /// <summary>
    /// Fills in <see cref="DatEntry.ArtName"/> for every row whose console has a thumbnails repository.
    /// <para>
    /// Doing this at build time rather than per request is the whole point. The two naming schemes have
    /// drifted apart, and discovering that at runtime would mean tens of thousands of speculative 404s
    /// per library scan, each one indistinguishable from "this game genuinely has no cover".
    /// </para>
    /// </summary>
    private async Task<(IReadOnlyList<DatEntry> Entries, IReadOnlyList<ConsoleType> Unresolved)> ResolveArtNamesAsync(
        IReadOnlyList<DatEntry> entries, CancellationToken ct)
    {
        // An --input build is an offline build: it must not silently depend on GitHub being reachable,
        // and it has to produce the same index twice in a row for the same directory.
        var offline = options.InputDirectory is not null;
        using var fetcher = new ThumbnailFetcher(options.ThumbnailCacheDirectory ?? options.CacheDirectory, offline);
        var indexes = new Dictionary<ConsoleType, ThumbnailIndex>();
        var unresolved = new List<ConsoleType>();

        foreach (var console in entries.Select(e => e.Console).Distinct().Order())
        {
            // DSi is the one console libretro-thumbnails does not usefully cover (13 covers against a
            // library in the thousands), and LibRetroArtSource declines it outright; GameTDB serves it
            // by title id instead. Asking would cost a request to learn nothing.
            if (console is ConsoleType.Unknown or ConsoleType.NintendoDsi)
            {
                continue;
            }

            ThumbnailListing? listing;
            try
            {
                listing = await fetcher.FetchAsync(console, ct);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                // One console's listing going bad must not cost the other twenty-four their names, and
                // must not take the build down. Same reasoning as the DAT loop above.
                log($"  ! {console.Slug(),-5} {ex.Message}");
                listing = null;
            }

            if (listing is null)
            {
                // Build on regardless: a console without resolved names still serves every cover whose
                // canonical name happens to match, and half an index beats none. But this must never be
                // quiet. Unresolved rows are indistinguishable from "this console has no art", which is
                // the failure the whole step exists to end, so it is recorded in three places that
                // outlive the build log: the result, the report, and the artifact's own provenance.
                log($"  ! {console.Slug(),-5} NO THUMBNAIL LISTING - names unresolved for this console");
                unresolved.Add(console);
                _provenance[$"thumbnails:{console.Slug()}"] = "unavailable";
                continue;
            }

            // LibRetroArtSource addresses covers on "master" (see its BaseUrl). Resolving names against
            // a listing from a different branch would publish an index whose art coverage looks healthy
            // and whose every request 404s: the same whole-console silent failure this step exists to
            // stop, just moved from the name to the branch.
            if (!string.Equals(listing.Branch, "master", StringComparison.Ordinal))
            {
                log($"  ! {console.Slug(),-5} listed from '{listing.Branch}', but covers are fetched from " +
                    "'master'; requests will fall through to the mirror");
            }

            // Offline is the asked-for behaviour for an --input build, so only an online build falling
            // back to disk is worth remarking on: there, the cache is standing in for an upstream we
            // could not reach, and the listing may be months old.
            if (listing.FromCache && !offline)
            {
                log($"  - {console.Slug(),-5} upstream unreachable, using the cached listing");
            }

            indexes[console] = new ThumbnailIndex(listing.FileNames);
            _provenance[$"thumbnails:{console.Slug()}"] =
                (listing.FromCache && !offline ? "cached " : "") + $"{listing.Branch}@{listing.TreeSha}";
        }

        var resolved = new List<DatEntry>(entries.Count);
        var hits = new Dictionary<ConsoleType, int>();
        foreach (var entry in entries)
        {
            var match = indexes.TryGetValue(entry.Console, out var index) ? index.Resolve(entry.Name) : null;
            if (match is null)
            {
                resolved.Add(entry);
                continue;
            }

            hits[entry.Console] = hits.GetValueOrDefault(entry.Console) + 1;
            resolved.Add(entry with { ArtName = match.FileName, ArtTier = match.Tier });
        }

        foreach (var (console, index) in indexes)
        {
            var total = entries.Count(e => e.Console == console);
            var found = hits.GetValueOrDefault(console);
            log(string.Create(CultureInfo.InvariantCulture,
                $"  {console.Slug(),-5} {found,6:N0} / {total,6:N0} rows have art ({(double)found / total:P1}), {index.Count:N0} covers listed"));
        }

        return (resolved, unresolved);
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
