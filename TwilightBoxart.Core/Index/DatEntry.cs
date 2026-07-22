using System.Globalization;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>
/// One ROM row parsed out of a DAT file, normalised to the shape of the generated index. A DAT
/// <c>game</c> with several <c>rom</c> children yields one <see cref="DatEntry"/> per rom: each is a
/// separately identifiable file and must be findable by its own CRC32/SHA-1.
/// </summary>
public sealed record DatEntry
{
    public required ConsoleType Console { get; init; }

    /// <summary>The game's canonical No-Intro name (the <c>game</c> element's name, never the rom's file name).</summary>
    public required string Name { get; init; }

    /// <summary>Cartridge serial / title id, when the DAT carries one. Upper-cased and invariant.</summary>
    public string? Serial { get; init; }

    public uint? Crc32 { get; init; }

    /// <summary>Lower-case hex, exactly as the index stores it.</summary>
    public string? Sha1 { get; init; }

    /// <summary>No-Intro dump status: <c>baddump</c>, <c>nodump</c>, <c>good</c>, <c>verified</c>, or null.</summary>
    public string? Status { get; init; }

    /// <summary>Which DAT this came from. Diagnostics only; never written to the index.</summary>
    public string SourceName { get; init; } = "";
}

/// <summary>
/// How much a row deserves to win when two rows compete for the same lookup key. Lower is better.
/// The rule is explicit and decided once at build time, and rows are written in rank order so the
/// winner also has the lowest rowid, which is what an unordered
/// <c>SELECT ... WHERE crc32 = ? LIMIT 1</c> in the reader will return.
/// </summary>
public static class DatEntryQuality
{
    /// <summary>Sort rank. Dump quality dominates; release kind breaks ties.</summary>
    public static int Rank(DatEntry entry) => (DumpRank(entry.Status) * 10) + ReleaseRank(entry.Name);

    /// <summary>
    /// Dump status rank. An unrecognised status sorts *between* good and baddump: we do not know what it
    /// means, so it should not beat a known-good dump and should not be discarded like a known-bad one.
    /// </summary>
    public static int DumpRank(string? status) => Normalize(status) switch
    {
        null or "good" or "verified" => 0,
        "baddump" => 2,
        "nodump" => 3,
        _ => 1,
    };

    /// <summary>
    /// A shipped release describes a title better than a prototype of it. Only consulted when dump
    /// status ties, so it can never promote a baddump over a good dump.
    /// </summary>
    public static int ReleaseRank(string name)
    {
        if (Contains(name, "(Proto") || Contains(name, "(Beta"))
        {
            return 2;
        }

        if (Contains(name, "(Demo") || Contains(name, "(Sample") || Contains(name, "(Kiosk") || Contains(name, "(Promo"))
        {
            return 1;
        }

        return 0;
    }

    /// <summary>Trimmed, invariant-lower-cased status; null for anything blank. Never culture-sensitive.</summary>
    public static string? Normalize(string? status)
    {
        var trimmed = status?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Field-level normalisation shared by both DAT parsers. All of it is culture-invariant.</summary>
public static class DatFields
{
    /// <summary>
    /// Parses an 8-hex-digit DAT CRC. Note a DAT <c>crc="00000000"</c> is a real declared value and is
    /// kept: the "0 means unknown" trap belongs to the 7z *reader*, which never asks
    /// the index about 0 in the first place, so keeping the row here cannot mis-identify anything.
    /// </summary>
    public static uint? ParseCrc32(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Lower-case hex SHA-1, or null when absent or not 40 hex characters.</summary>
    public static string? ParseSha1(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length != 40)
        {
            return null;
        }

        foreach (var c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                return null;
            }
        }

        return text.ToLowerInvariant();
    }

    /// <summary>
    /// Normalises a serial. DATs occasionally list several comma-separated serials for one dump
    /// (multi-region carts); the first is the primary and is the one the header will carry.
    /// </summary>
    public static string? ParseSerial(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var comma = text.IndexOf(',');
        if (comma > 0)
        {
            text = text[..comma].Trim();
        }

        return text.Length == 0 ? null : text.ToUpperInvariant();
    }

    public static string? Clean(string? raw)
    {
        var text = raw?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
