using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>What the dedupe pass collapsed, for the build summary.</summary>
public sealed record DedupeReport
{
    public int InputRows { get; init; }

    public int OutputRows { get; init; }

    /// <summary>Rows identical in every field: the same dump listed by two overlapping DATs.</summary>
    public int ExactDuplicates { get; init; }

    /// <summary>Rows collapsed because another row had the same SHA-1: literally the same bytes.</summary>
    public int Sha1Duplicates { get; init; }

    /// <summary>Rows collapsed because another row had the same CRC32 and no contradicting SHA-1.</summary>
    public int Crc32Duplicates { get; init; }

    /// <summary>Rows whose CRC32 was cleared because two different ROMs genuinely collided on it.</summary>
    public int Crc32CollisionsCleared { get; init; }

    /// <summary>Rows whose serial was cleared because that serial names several unrelated games.</summary>
    public int AmbiguousSerialsCleared { get; init; }

    /// <summary>Rows that lost to a better-ranked row specifically because they were flagged baddump/nodump.</summary>
    public int BadDumpsSuperseded { get; init; }
}

/// <summary>
/// Collapses the raw union of every DAT into rows that answer each lookup key unambiguously.
///
/// <para><b>Why this exists.</b> The index is read three ways: by CRC32, by SHA-1, and by
/// (console, serial), and each read wants exactly one answer. The 2020 backend resolved the ambiguity
/// per request with <c>results.FirstOrDefault(c =&gt; !c.Status?.Contains("bad") ?? true); // lol</c>.
/// The "lol" was earned: a substring test over free text treats a game named
/// "Bad Mojo" as a bad dump, it only ran on the serial path, and it re-decided on every request
/// something the build can decide once.</para>
///
/// <para><b>The rules, in order.</b></para>
/// <list type="number">
/// <item><b>Identical rows collapse.</b> Same console, name, serial, CRC, SHA-1 and status: one row.
/// This is pure source overlap: No-Intro and libretro both list the same dump.</item>
/// <item><b>Same SHA-1 collapses.</b> A SHA-1 match means the same bytes. Only one row can describe
/// them, so the best-ranked wins and the rest are dropped. SHA-1 is treated as globally unique, not
/// per-console: a DS dump filed under both the DS and DSi sets is one ROM.</item>
/// <item><b>Same CRC32 collapses</b>, but only when nothing contradicts it. If the
/// competing rows carry <i>different</i> SHA-1s they are different ROMs that happen to collide on 32
/// bits, which over 42k rows is likely rather than exotic. Both rows are then kept and both have their
/// CRC32 cleared, because a colliding CRC cannot identify either one and a confident wrong answer is
/// worse than a miss (the whole product bet is that misses get corrected by hand).</item>
/// <item><b>The winner inherits.</b> When a row is dropped, any field the winner lacked is copied from
/// it. Same bytes means the fields describe the same ROM, so this fills gaps without inventing
/// anything: a good dump missing a serial can take the one off the baddump row it superseded.</item>
/// <item><b>Serials that name several unrelated games are cleared.</b> Not every DAT serial is a game
/// code, and even the real ones get reused. Measured on the live No-Intro set: <c>NTRJ</c> appears on
/// 175 different DS titles and Mega Drive <c>00000000-00</c> on 287, but the dangerous ones look
/// entirely legitimate. DS <c>AGEE</c> is both "GoldenEye - Rogue Agent" and "Star Wars - The Force
/// Unleashed"; <c>AH5E</c> is both "Bee Movie Game" and "Over the Hedge"; Mega Drive <c>00004049-01</c>
/// is both "Sonic The Hedgehog" and "Sonic The Hedgehog 2". Left alone, each answers a serial lookup
/// with an arbitrary one of its members, and the serial rung runs <i>first</i> in the ladder, so that
/// wrong answer preempts the CRC32 rung that would have got it right. Same principle as the CRC32
/// collision rule: a key that cannot discriminate must not answer.
/// <para>This does over-fire on regional retitles of one game (Mega Drive <c>00004012-00</c> is "Last
/// Battle" and its Japanese "Hokuto no Ken"), which is why it costs Mega Drive ~36 points of serial
/// coverage. That is the right trade: those rows stay reachable by CRC32 (which is 100% populated),
/// by SHA-1 and by name, so the loss is one rung, not one ROM. A confidently wrong cover is the outcome
/// worth paying to avoid.</para></item>
/// <item><b>Remaining serial ambiguity is left standing, deliberately.</b> Two revisions of <i>one</i>
/// game share a serial but have different CRCs, and dropping either would break CRC lookup for that
/// revision. Both rows survive; <see cref="Order"/> then writes the better one first so it gets the
/// lower rowid, which is what an unordered <c>LIMIT 1</c> in the reader returns.</item>
/// </list>
/// </summary>
public static class EntryDeduplicator
{
    /// <summary>
    /// How many distinct game titles one serial may name before it is treated as a placeholder rather
    /// than an identifier. One, because there is no useful middle ground: two unrelated games under one
    /// code means a lookup is a coin flip, and the ladder has three other rungs that will get it right.
    /// </summary>
    public const int MaxDistinctTitlesPerSerial = 1;

    public static (IReadOnlyList<DatEntry> Entries, DedupeReport Report) Deduplicate(IReadOnlyList<DatEntry> input)
    {
        var exactDuplicates = 0;
        var sha1Duplicates = 0;
        var crc32Duplicates = 0;
        var collisionsCleared = 0;
        var badDumpsSuperseded = 0;
        var ambiguousSerialsCleared = 0;

        // Identical rows collapse: pure source overlap.
        var seen = new HashSet<(ConsoleType, string, string?, uint?, string?, string?)>();
        var distinct = new List<DatEntry>(input.Count);
        foreach (var entry in input)
        {
            if (seen.Add((entry.Console, entry.Name, entry.Serial, entry.Crc32, entry.Sha1, entry.Status)))
            {
                distinct.Add(entry);
            }
            else
            {
                exactDuplicates++;
            }
        }

        // From here on rows are addressed by index and losers are struck out rather than removed, so a
        // row rewritten by one rule is the row the next rule sees.
        var rows = distinct.ToArray();
        var dropped = new bool[rows.Length];

        // Same SHA-1 means the same bytes, whatever console the DAT filed it under.
        var bySha1 = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].Sha1 is not { } sha1)
            {
                continue;
            }

            if (!bySha1.TryGetValue(sha1, out var incumbent))
            {
                bySha1[sha1] = i;
                continue;
            }

            sha1Duplicates++;
            var keep = IsBetter(rows[i], rows[incumbent]) ? i : incumbent;
            var drop = keep == i ? incumbent : i;
            CountSupersededBadDump(rows[keep], rows[drop], ref badDumpsSuperseded);
            rows[keep] = Inherit(rows[keep], rows[drop]);
            dropped[drop] = true;
            bySha1[sha1] = keep;
        }

        // Same CRC32 collapses, unless the SHA-1s contradict it. Grouped globally rather than per
        // console because that is how it is read: IMetadataIndex.TryByCrc32 takes no console, and
        // ix_entry_crc32 is on crc32 alone. Two rows sharing a CRC under different consoles would be
        // just as ambiguous to the reader as two under one console.
        var byCrc = new Dictionary<uint, List<int>>();
        for (var i = 0; i < rows.Length; i++)
        {
            if (!dropped[i] && rows[i].Crc32 is { } crc)
            {
                if (!byCrc.TryGetValue(crc, out var bucket))
                {
                    byCrc[crc] = bucket = [];
                }

                bucket.Add(i);
            }
        }

        foreach (var bucket in byCrc.Values)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            var distinctSha1s = bucket
                .Select(i => rows[i].Sha1)
                .Where(s => s is not null)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (distinctSha1s > 1)
            {
                // A genuine 32-bit collision between different ROMs -- with 42k rows the birthday odds
                // make this likely, not exotic. Keep every row, but make none of them findable by CRC:
                // the key does not discriminate here, so answering from it would be a coin flip dressed
                // up as a match. SHA-1, serial and name lookups all still reach these rows.
                foreach (var i in bucket)
                {
                    rows[i] = rows[i] with { Crc32 = null };
                    collisionsCleared++;
                }

                continue;
            }

            var keep = bucket[0];
            foreach (var i in bucket)
            {
                if (IsBetter(rows[i], rows[keep]))
                {
                    keep = i;
                }
            }

            foreach (var i in bucket)
            {
                if (i == keep)
                {
                    continue;
                }

                CountSupersededBadDump(rows[keep], rows[i], ref badDumpsSuperseded);
                var inheritedSha1 = rows[keep].Sha1 is null ? rows[i].Sha1 : null;
                rows[keep] = Inherit(rows[keep], rows[i]);
                if (inheritedSha1 is not null)
                {
                    // The hash just moved to the winner. Without this the SHA-1 map keeps naming
                    // the dropped row, and anything keyed on that hash from here on would collapse
                    // against a row that no longer exists.
                    bySha1[inheritedSha1] = keep;
                }

                dropped[i] = true;
                crc32Duplicates++;
            }
        }

        // Serials that name several unrelated games identify none of them.
        var bySerial = new Dictionary<(ConsoleType Console, string Serial), List<int>>();
        for (var i = 0; i < rows.Length; i++)
        {
            if (!dropped[i] && rows[i].Serial is { } serial)
            {
                if (!bySerial.TryGetValue((rows[i].Console, serial), out var bucket))
                {
                    bySerial[(rows[i].Console, serial)] = bucket = [];
                }

                bucket.Add(i);
            }
        }

        foreach (var bucket in bySerial.Values)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            var titles = bucket.Select(i => BaseTitle(rows[i].Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (titles <= MaxDistinctTitlesPerSerial)
            {
                continue;
            }

            foreach (var i in bucket)
            {
                rows[i] = rows[i] with { Serial = null };
                ambiguousSerialsCleared++;
            }
        }

        var output = new List<DatEntry>(rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            if (!dropped[i])
            {
                output.Add(rows[i]);
            }
        }

        return (output, new DedupeReport
        {
            InputRows = input.Count,
            OutputRows = output.Count,
            ExactDuplicates = exactDuplicates,
            Sha1Duplicates = sha1Duplicates,
            Crc32Duplicates = crc32Duplicates,
            Crc32CollisionsCleared = collisionsCleared,
            AmbiguousSerialsCleared = ambiguousSerialsCleared,
            BadDumpsSuperseded = badDumpsSuperseded,
        });
    }

    /// <summary>
    /// The game's title without its No-Intro qualifiers: "Zelda (USA) (Rev 1)" becomes "Zelda". Two
    /// rows sharing a base title are revisions or regional dumps of one game and share box art; two rows
    /// with different base titles are different games, whatever serial the DAT gave them.
    /// </summary>
    internal static string BaseTitle(string name)
    {
        var qualifier = name.IndexOf(" (", StringComparison.Ordinal);
        return qualifier < 0 ? name : name[..qualifier];
    }

    /// <summary>
    /// The write order, which is also the preference order: within any group that still shares a lookup
    /// key, the better row is written first and therefore gets the lower rowid. Beyond that the order is
    /// only required to be total and stable, so the same inputs always produce the same file (the
    /// determinism requirement: a CI diff should mean something).
    /// </summary>
    public static IReadOnlyList<DatEntry> Order(IReadOnlyList<DatEntry> entries) =>
        entries
            .OrderBy(e => (int)e.Console)
            .ThenBy(e => e.Serial ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(DatEntryQuality.Rank)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.Crc32 ?? uint.MaxValue)
            .ThenBy(e => e.Sha1 ?? string.Empty, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// True when <paramref name="candidate"/> should supersede <paramref name="incumbent"/>. Rank first;
    /// then ordinal name, so the winner depends on the data and not on which DAT happened to load first.
    /// </summary>
    private static bool IsBetter(DatEntry candidate, DatEntry incumbent)
    {
        var rankCandidate = DatEntryQuality.Rank(candidate);
        var rankIncumbent = DatEntryQuality.Rank(incumbent);

        if (rankCandidate != rankIncumbent)
        {
            return rankCandidate < rankIncumbent;
        }

        return string.CompareOrdinal(candidate.Name, incumbent.Name) < 0;
    }

    private static void CountSupersededBadDump(DatEntry winner, DatEntry loser, ref int counter)
    {
        if (DatEntryQuality.DumpRank(loser.Status) >= 2 &&
            DatEntryQuality.DumpRank(winner.Status) < DatEntryQuality.DumpRank(loser.Status))
        {
            counter++;
        }
    }

    /// <summary>Fills the winner's null fields from a row describing the same bytes. Never overwrites.</summary>
    private static DatEntry Inherit(DatEntry winner, DatEntry loser) => winner with
    {
        Serial = winner.Serial ?? loser.Serial,
        Crc32 = winner.Crc32 ?? loser.Crc32,
        Sha1 = winner.Sha1 ?? loser.Sha1,
    };
}
