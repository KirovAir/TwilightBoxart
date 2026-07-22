using System.Globalization;
using System.Text;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>Per-console row counts and how many of them carry each identifier.</summary>
public sealed record ConsoleCoverage(ConsoleType Console, int Rows, int WithSerial, int WithCrc32, int WithSha1)
{
    public double SerialPercent => Percentage(WithSerial, Rows);

    public double Crc32Percent => Percentage(WithCrc32, Rows);

    public double Sha1Percent => Percentage(WithSha1, Rows);

    internal static double Percentage(int part, int whole) => whole == 0 ? 0 : part * 100.0 / whole;
}

/// <summary>Serial coverage for one DAT, the granularity the coverage baseline was measured at.</summary>
public sealed record SourceCoverage(string SourceName, ConsoleType Console, int Rows, int WithSerial)
{
    public double SerialPercent => ConsoleCoverage.Percentage(WithSerial, Rows);
}

/// <summary>The build's outcome: the artifact it produced and everything worth printing about it.</summary>
public sealed record BuildResult
{
    /// <summary>The build stamp written to <c>meta.version</c> inside the database.</summary>
    public required string Version { get; init; }

    public required int RowCount { get; init; }

    public required string DatabasePath { get; init; }

    public required DedupeReport Dedupe { get; init; }

    public required IReadOnlyList<ConsoleCoverage> Coverage { get; init; }

    public required IReadOnlyList<SourceCoverage> SourceCoverage { get; init; }

    /// <summary>Sources that could not be fetched. Non-empty means the index is thinner than it should be.</summary>
    public required IReadOnlyList<string> MissingSources { get; init; }
}

/// <summary>
/// Formats the build summary.
///
/// <para>The serial-coverage tables are not decoration. The whole identification ladder
/// is premised on header serials being near-universal on DS/DSi/GBA/Mega Drive and
/// near-absent everywhere else. If a DAT format change or a stale mapping quietly halves DS serial
/// coverage, the ladder silently degrades to hashing without anything failing, so the measured numbers
/// are printed next to the baseline and any drift is called out.</para>
///
/// <para>There are two tables because the two questions are different. The per-console table is what the
/// index actually contains. The per-source table is what validates the premise: several No-Intro sets
/// deliberately fold into one <see cref="ConsoleType"/>, and folding Download Play (almost no serials)
/// into Nintendo DS drags that console's blended figure ~6 points below the DS set's own 97.8%. That is
/// correct behaviour, not a regression, so the drift check runs per source where the baseline is exact.</para>
/// </summary>
public static class BuildReport
{
    /// <summary>
    /// Serial coverage per DAT, as measured over the original 42,296-row corpus. A regression
    /// baseline, not a requirement: a real No-Intro update moves these by tenths of a point.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> ExpectedSerialCoverage =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nintendo - Nintendo DS"] = 97.8,
            ["Nintendo - Nintendo DSi"] = 99.3,
            ["Nintendo - Nintendo DSi (Digital)"] = 99.3,
            ["Nintendo - Game Boy Advance"] = 87.7,
            ["Sega - Mega Drive - Genesis"] = 93.3,
            ["Nintendo - Family Computer Disk System"] = 86.7,
            ["Nintendo - Game Boy Color"] = 3.1,
            ["Nintendo - Super Nintendo Entertainment System"] = 0.3,
            ["Nintendo - Game Boy"] = 0.1,
            ["Nintendo - Nintendo Entertainment System"] = 0.0,
            ["Sega - Game Gear"] = 0.0,
            ["Sega - Master System - Mark III"] = 0.0,
        };

    /// <summary>How far a source may drift from its baseline before the build says something.</summary>
    public const double DriftWarningPoints = 5.0;

    public static IReadOnlyList<ConsoleCoverage> Measure(IReadOnlyList<DatEntry> entries) =>
        entries
            .GroupBy(e => e.Console)
            .Select(g => new ConsoleCoverage(
                g.Key,
                g.Count(),
                g.Count(e => e.Serial is not null),
                g.Count(e => e.Crc32 is not null),
                g.Count(e => e.Sha1 is not null)))
            .OrderBy(c => (int)c.Console)
            .ToList();

    public static IReadOnlyList<SourceCoverage> MeasureBySource(IReadOnlyList<DatEntry> entries) =>
        entries
            .GroupBy(e => (e.SourceName, e.Console))
            .Select(g => new SourceCoverage(g.Key.SourceName, g.Key.Console, g.Count(), g.Count(e => e.Serial is not null)))
            .OrderBy(c => (int)c.Console)
            .ThenBy(c => c.SourceName, StringComparer.Ordinal)
            .ToList();

    public static string Render(BuildResult result)
    {
        // Everything below formats invariantly: a build log should read the same on a Dutch developer's
        // machine and on an en-US CI runner, or the two cannot be diffed.
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        text.AppendLine();
        text.AppendLine("Index contents, by console");
        text.AppendLine("  console        rows    serial %     crc32 %      sha1 %");
        text.AppendLine("  ---------  --------  ----------  ----------  ----------");

        foreach (var row in result.Coverage)
        {
            text.AppendLine(string.Create(invariant,
                $"  {row.Console.Slug(),-9}  {row.Rows,8:N0}  {row.SerialPercent,9:F1}%  {row.Crc32Percent,9:F1}%  {row.Sha1Percent,9:F1}%"));
        }

        var totals = result.Coverage.Aggregate(
            (Rows: 0, Serial: 0, Crc: 0, Sha: 0),
            (acc, c) => (acc.Rows + c.Rows, acc.Serial + c.WithSerial, acc.Crc + c.WithCrc32, acc.Sha + c.WithSha1));

        text.AppendLine("  ---------  --------  ----------  ----------  ----------");
        text.AppendLine(string.Create(invariant,
            $"  {"total",-9}  {totals.Rows,8:N0}  {ConsoleCoverage.Percentage(totals.Serial, totals.Rows),9:F1}%  " +
            $"{ConsoleCoverage.Percentage(totals.Crc, totals.Rows),9:F1}%  {ConsoleCoverage.Percentage(totals.Sha, totals.Rows),9:F1}%"));

        text.AppendLine();
        text.AppendLine("Serial coverage by source, as published in the DAT (baseline)");
        text.AppendLine("  source                                              rows    serial %    baseline");
        text.AppendLine("  ----------------------------------------------  --------  ----------  ----------");

        foreach (var row in result.SourceCoverage)
        {
            var known = ExpectedSerialCoverage.TryGetValue(row.SourceName, out var baseline);
            var expected = known ? baseline.ToString("F1", invariant) + "%" : "-";

            text.Append(string.Create(invariant,
                $"  {Truncate(row.SourceName, 46),-46}  {row.Rows,8:N0}  {row.SerialPercent,9:F1}%  {expected,10}"));

            if (known && Math.Abs(row.SerialPercent - baseline) > DriftWarningPoints)
            {
                text.Append(string.Create(invariant, $"   <- drifted {row.SerialPercent - baseline:+0.0;-0.0} pts"));
            }

            text.AppendLine();
        }

        var dedupe = result.Dedupe;
        text.AppendLine();
        text.AppendLine("Dedupe");
        text.AppendLine(string.Create(invariant, $"  parsed rows                {dedupe.InputRows,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  identical rows dropped     {dedupe.ExactDuplicates,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  same-sha1 collapsed        {dedupe.Sha1Duplicates,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  same-crc32 collapsed       {dedupe.Crc32Duplicates,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  bad dumps superseded       {dedupe.BadDumpsSuperseded,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  crc32 cleared (collision)  {dedupe.Crc32CollisionsCleared,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  serial cleared (ambiguous) {dedupe.AmbiguousSerialsCleared,10:N0}"));
        text.AppendLine(string.Create(invariant, $"  rows written               {dedupe.OutputRows,10:N0}"));

        if (result.MissingSources.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(string.Create(invariant,
                $"Missing sources ({result.MissingSources.Count}) - the index is thinner than it should be:"));
            foreach (var missing in result.MissingSources)
            {
                text.AppendLine($"  - {missing}");
            }
        }

        text.AppendLine();
        text.AppendLine("Artifacts");
        text.AppendLine($"  {result.DatabasePath}");
        text.AppendLine(string.Create(invariant,
            $"    version   {result.Version}   rows {result.RowCount:N0}   {new FileInfo(result.DatabasePath).Length / 1024.0 / 1024.0:F1} MiB"));

        return text.ToString();
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";
}
