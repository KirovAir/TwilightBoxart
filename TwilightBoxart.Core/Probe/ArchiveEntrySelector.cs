namespace TwilightBoxart.Core.Probe;

/// <summary>One archive member, reduced to the three things entry selection actually needs.</summary>
/// <param name="Index">Position in the container, in stored order. The first ROM wins, so order matters.</param>
/// <param name="Name">The name exactly as the container records it, directory components included.</param>
/// <param name="UncompressedSize">Unpacked size in bytes.</param>
public readonly record struct ArchiveEntryCandidate(int Index, string Name, long UncompressedSize);

/// <summary>
/// Decides which member of an archive is the ROM. Shared by <see cref="ZipRomProbe"/> and
/// <see cref="SevenZipRomProbe"/> so the two containers cannot drift apart in behaviour.
/// </summary>
public static class ArchiveEntrySelector
{
    /// <summary>
    /// How far a nameless blob must out-weigh everything else before we will believe it is the ROM.
    /// A No-Intro "Nintendo DSi (Digital)" zip is a raw CDN drop: one content blob named
    /// <c>00000000</c> next to a ~2.5 KB <c>tik</c> ticket and a ~1 KB <c>tmd.N</c> title-metadata
    /// file. The true ratio there is two to four orders of magnitude, so 4x is a deliberately timid
    /// floor that admits the DSiWare case (945 of 1,069 files) while refusing to
    /// guess inside an archive whose members are all of comparable size.
    /// </summary>
    private const int DominanceFactor = 4;

    /// <summary>
    /// Below this, a "dominant" entry is far likelier to be junk than a ROM. The smallest DSiWare
    /// content blob is a 256 KiB CDN chunk, so 64 KiB leaves plenty of room underneath it.
    /// </summary>
    private const long MinDominantBytes = 64 * 1024;

    /// <summary>
    /// Extensions that are never a ROM. Only consulted on the size-dominance fallback - an entry
    /// whose extension is in <see cref="SupportedFiles.Rom"/> has already won by then. Ordinal and
    /// case-insensitive: a culture-sensitive compare is what made the old client skip every
    /// <c>.ZIP</c> under a Turkish locale.
    /// </summary>
    private static readonly IReadOnlySet<string> NeverRom = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".nfo", ".diz", ".md", ".readme", ".html", ".htm", ".xml", ".json", ".ini", ".cfg",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".pdf", ".doc", ".docx",
        ".sav", ".srm", ".sram", ".state", ".cht", ".cheats",
        ".ips", ".bps", ".ups", ".xdelta", ".ppf", ".patch",
        ".dat", ".sfv", ".md5", ".sha1", ".cue", ".m3u",
        ".exe", ".dll", ".bat", ".cmd", ".sh", ".url", ".lnk",
    };

    /// <summary>
    /// Picks the ROM out of <paramref name="entries"/>, or null when nothing in the archive looks
    /// like one. Never throws: a container we cannot make sense of is a miss, not an error.
    /// </summary>
    public static ArchiveEntryCandidate? Select(IReadOnlyList<ArchiveEntryCandidate> entries)
    {
        // First pass: the first entry, in stored order, whose extension we recognise.
        foreach (var entry in entries)
        {
            if (IsIgnorable(entry))
            {
                continue;
            }

            if (SupportedFiles.Rom.Contains(Path.GetExtension(LeafName(entry.Name))))
            {
                return entry;
            }
        }

        // Second pass: nothing matched by extension. This is the DSi (Digital) shape - extension-less
        // CDN blobs - where the ROM is simply the member that dwarfs its neighbours. Requiring
        // dominance rather than just "largest" is what stops us guessing on ordinary archives.
        ArchiveEntryCandidate? largest = null;
        foreach (var entry in entries)
        {
            if (IsIgnorable(entry) || NeverRom.Contains(Path.GetExtension(LeafName(entry.Name))))
            {
                continue;
            }

            if (largest is null || entry.UncompressedSize > largest.Value.UncompressedSize)
            {
                largest = entry;
            }
        }

        if (largest is not { } winner || winner.UncompressedSize < MinDominantBytes)
        {
            return null;
        }

        // The runner-up is measured over *every* other member, including the ones excluded above:
        // a 10 MB screenshot sitting beside a 1 MB blob means this archive is not a CDN drop.
        long runnerUp = 0;
        foreach (var entry in entries)
        {
            if (entry.Index != winner.Index && !IsDirectory(entry.Name))
            {
                runnerUp = Math.Max(runnerUp, entry.UncompressedSize);
            }
        }

        return winner.UncompressedSize >= runnerUp * DominanceFactor ? winner : null;
    }

    /// <summary>
    /// The entry name with any directory component stripped, so callers see the same shape whether
    /// the ROM came out of an archive or off the filesystem. Handles both separators because a zip
    /// written on Windows by a non-conformant tool can store backslashes.
    /// </summary>
    public static string LeafName(string entryName)
    {
        var cut = entryName.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? entryName : entryName[(cut + 1)..];
    }

    /// <summary>
    /// Directories and zero-byte members are never the ROM. Excluding empty ones matters beyond
    /// tidiness: a zip records a CRC32 of 0 for a zero-length entry, and that is a *real* digest
    /// rather than the 7z "unknown" sentinel, so letting one through would send a genuine-looking
    /// 0x00000000 into the index lookup.
    /// </summary>
    private static bool IsIgnorable(ArchiveEntryCandidate entry) =>
        entry.UncompressedSize <= 0 || IsDirectory(entry.Name);

    private static bool IsDirectory(string entryName) =>
        entryName.Length == 0 || entryName[^1] is '/' or '\\';
}
