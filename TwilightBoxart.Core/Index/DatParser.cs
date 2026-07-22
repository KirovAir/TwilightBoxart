using System.Text;
using System.Xml;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>A parsed DAT: its declared identity plus one row per rom.</summary>
public sealed record DatDocument
{
    /// <summary>The DAT's own <c>header/name</c>, e.g. "Nintendo - Game Boy". Null when it declared none.</summary>
    public string? HeaderName { get; init; }

    /// <summary>The DAT's own <c>header/version</c>. Reported in the build log so a CI diff is explainable.</summary>
    public string? HeaderVersion { get; init; }

    public required IReadOnlyList<DatEntry> Entries { get; init; }

    /// <summary>Number of <c>game</c> elements, which is less than <c>Entries.Count</c> for multi-rom games.</summary>
    public int GameCount { get; init; }
}

/// <summary>
/// Reads both DAT dialects No-Intro data reaches us in:
/// <list type="bullet">
/// <item>the Logiqx XML datafile that datomatic.no-intro.org serves;</item>
/// <item>the ClrMamePro text format that libretro-database mirrors it in.</item>
/// </list>
/// The dialect is sniffed, not configured, because the same logical DAT arrives in either depending on
/// which mirror CI reached. The 2020 crawler had a hand-rolled text parser whose own comment read
/// "The code is horrible and so is the format": it flattened the tree into a token list and matched
/// property names by reflection, so a nested block silently overwrote its parent's fields.
/// </summary>
public static class DatParser
{
    /// <summary>Parses a DAT of either dialect, attributing every row to <paramref name="console"/>.</summary>
    public static DatDocument Parse(string text, ConsoleType console, string sourceName)
    {
        return LooksLikeXml(text)
            ? ParseXml(text, console, sourceName)
            : ParseClrMamePro(text, console, sourceName);
    }

    /// <summary>True when the payload is a Logiqx XML datafile rather than ClrMamePro text.</summary>
    public static bool LooksLikeXml(string text)
    {
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == '﻿')
            {
                continue;
            }

            return c == '<';
        }

        return false;
    }

    // Logiqx XML

    private static XmlReader CreateXmlReader(string text)
    {
        // DtdProcessing.Ignore + a null resolver: every No-Intro DAT carries a DOCTYPE pointing at
        // logiqx.com, and a build server must not make that request (nor be XXE-reachable through it).
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
        };

        return XmlReader.Create(new StringReader(text), settings);
    }

    // NOTE on reader discipline below: ReadElementContentAsString() already advances past the element
    // it consumed. Every loop here therefore uses the "advance only when nothing else did" shape --
    // a plain `while (reader.Read())` around those calls silently skips the following sibling, which
    // would have dropped the first <game> after the </header> in every DAT.
    private static DatDocument ParseXml(string text, ConsoleType console, string sourceName)
    {
        var entries = new List<DatEntry>();
        string? headerName = null;
        string? headerVersion = null;
        var gameCount = 0;

        using var reader = CreateXmlReader(text);
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.Name)
            {
                case "header":
                    (headerName, headerVersion) = ReadHeaderElement(reader);
                    break;

                // "machine" is the MAME-flavoured spelling; some mirrors emit it.
                case "game" or "machine":
                    gameCount++;
                    ReadGameElement(reader, console, sourceName, entries);
                    break;

                default:
                    // Descend: this is <datafile> itself, or an element we do not model.
                    reader.Read();
                    break;
            }
        }

        return new DatDocument
        {
            HeaderName = headerName,
            HeaderVersion = headerVersion,
            Entries = entries,
            GameCount = gameCount,
        };
    }

    /// <summary>Reads a &lt;header&gt;, leaving the reader on the node that follows it.</summary>
    private static (string? Name, string? Version) ReadHeaderElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return (null, null);
        }

        string? name = null;
        string? version = null;
        var depth = reader.Depth;
        reader.Read();

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                reader.Read();
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.Name == "name")
                {
                    name = DatFields.Clean(reader.ReadElementContentAsString());
                    continue;
                }

                if (reader.Name == "version")
                {
                    version = DatFields.Clean(reader.ReadElementContentAsString());
                    continue;
                }
            }

            reader.Read();
        }

        return (name, version);
    }

    /// <summary>Reads a &lt;game&gt;, leaving the reader on the node that follows it.</summary>
    private static void ReadGameElement(XmlReader reader, ConsoleType console, string sourceName, List<DatEntry> entries)
    {
        var name = DatFields.Clean(reader.GetAttribute("name"));
        // A <serial> child on the game applies to every rom that does not carry its own.
        string? gameSerial = null;
        string? description = null;
        var roms = new List<RomAttributes>();

        if (reader.IsEmptyElement)
        {
            reader.Read();
            Emit(console, sourceName, name, gameSerial, roms, entries);
            return;
        }

        var depth = reader.Depth;
        reader.Read();

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                reader.Read();
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "rom":
                        roms.Add(new RomAttributes(
                            reader.GetAttribute("crc"),
                            reader.GetAttribute("sha1"),
                            reader.GetAttribute("status"),
                            reader.GetAttribute("serial")));
                        break;

                    case "serial":
                        gameSerial = reader.ReadElementContentAsString();
                        continue;

                    case "description":
                        description = DatFields.Clean(reader.ReadElementContentAsString());
                        continue;
                }
            }

            reader.Read();
        }

        Emit(console, sourceName, name ?? description, gameSerial, roms, entries);
    }

    // ClrMamePro text

    private static DatDocument ParseClrMamePro(string text, ConsoleType console, string sourceName)
    {
        var entries = new List<DatEntry>();
        var tokens = Tokenize(text);
        var index = 0;
        string? headerName = null;
        string? headerVersion = null;
        var gameCount = 0;

        while (TryReadBlock(tokens, ref index, out var block))
        {
            if (block!.Kind is "clrmamepro" or "header" or "datafile")
            {
                headerName ??= block.Value("name");
                headerVersion ??= block.Value("version");
                continue;
            }

            if (block.Kind is not ("game" or "machine" or "set"))
            {
                continue;
            }

            gameCount++;
            var roms = block.Children
                .Where(c => c.Kind == "rom")
                .Select(c => new RomAttributes(c.Value("crc"), c.Value("sha1"), c.Value("status") ?? c.Value("flags"), c.Value("serial")))
                .ToList();

            Emit(console, sourceName, block.Value("name") ?? block.Value("description"), block.Value("serial"), roms, entries);
        }

        return new DatDocument
        {
            HeaderName = headerName,
            HeaderVersion = headerVersion,
            Entries = entries,
            GameCount = gameCount,
        };
    }

    /// <summary>A ClrMamePro block: <c>kind ( key value ... nested ( ... ) )</c>.</summary>
    private sealed class Block(string kind)
    {
        public string Kind { get; } = kind;

        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<Block> Children { get; } = [];

        public string? Value(string key) => Values.TryGetValue(key, out var v) ? v : null;
    }

    private static bool TryReadBlock(List<string> tokens, ref int index, out Block? block)
    {
        block = null;

        // Scan forward for `IDENT (`. Anything else at top level is noise we can safely ignore.
        while (index < tokens.Count)
        {
            if (tokens[index] is "(" or ")")
            {
                index++;
                continue;
            }

            if (index + 1 < tokens.Count && tokens[index + 1] == "(")
            {
                var kind = tokens[index].ToLowerInvariant();
                index += 2;
                block = ReadBody(tokens, ref index, kind);
                return true;
            }

            index++;
        }

        return false;
    }

    private static Block ReadBody(List<string> tokens, ref int index, string kind)
    {
        var block = new Block(kind);

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (token == ")")
            {
                index++;
                return block;
            }

            if (token == "(")
            {
                // A bare `(` with no preceding key: malformed, but skip its body rather than desync.
                index++;
                _ = ReadBody(tokens, ref index, "");
                continue;
            }

            index++;
            if (index < tokens.Count && tokens[index] == "(")
            {
                index++;
                block.Children.Add(ReadBody(tokens, ref index, token.ToLowerInvariant()));
                continue;
            }

            if (index < tokens.Count && tokens[index] != ")" && tokens[index] != "(")
            {
                // First writer wins: a repeated key in one block is a DAT bug, and the first is the
                // one a human reading the file would take as authoritative.
                block.Values.TryAdd(token, tokens[index]);
                index++;
            }
        }

        return block;
    }

    /// <summary>
    /// Splits ClrMamePro text into parens, quoted strings and bare words. Parentheses are
    /// self-delimiting so <c>sha1 abc)</c> tokenises correctly even without the customary space.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;

        void Flush()
        {
            if (buffer.Length > 0)
            {
                tokens.Add(buffer.ToString());
                buffer.Clear();
            }
        }

        foreach (var c in text)
        {
            if (inQuotes)
            {
                if (c == '"')
                {
                    inQuotes = false;
                    tokens.Add(buffer.ToString());
                    buffer.Clear();
                }
                else
                {
                    buffer.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    Flush();
                    inQuotes = true;
                    break;
                case '(' or ')':
                    Flush();
                    tokens.Add(c.ToString());
                    break;
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        Flush();
                    }
                    else
                    {
                        buffer.Append(c);
                    }

                    break;
            }
        }

        Flush();
        return tokens;
    }

    // Shared

    private readonly record struct RomAttributes(string? Crc, string? Sha1, string? Status, string? Serial);

    private static void Emit(
        ConsoleType console,
        string sourceName,
        string? gameName,
        string? gameSerial,
        List<RomAttributes> roms,
        List<DatEntry> entries)
    {
        var name = DatFields.Clean(gameName);
        if (name is null)
        {
            // A game with no name cannot be looked up by name and cannot key an art URL. Drop it.
            return;
        }

        var fallbackSerial = NormalizeSerial(console, DatFields.ParseSerial(gameSerial));

        if (roms.Count == 0)
        {
            // Name-and-serial-only games do happen (nodump placeholders). They still earn a row: the
            // header-serial rung of the ladder can match them even with no hash at all.
            entries.Add(new DatEntry
            {
                Console = console,
                Name = name,
                Serial = fallbackSerial,
                SourceName = sourceName,
            });
            return;
        }

        foreach (var rom in roms)
        {
            entries.Add(new DatEntry
            {
                Console = console,
                Name = name,
                Serial = NormalizeSerial(console, DatFields.ParseSerial(rom.Serial)) ?? fallbackSerial,
                Crc32 = DatFields.ParseCrc32(rom.Crc),
                Sha1 = DatFields.ParseSha1(rom.Sha1),
                Status = DatEntryQuality.Normalize(rom.Status),
                SourceName = sourceName,
            });
        }
    }

    /// <summary>
    /// Console-specific serial fix-ups. FDS is the only case: No-Intro writes FDS serials with a
    /// manufacturer prefix ("FMC-ZEL") but the disk header carries only the bare 3-character code (see
    /// FamicomDiskSystemHeaderParser), so the prefix is stripped here to let the header-serial rung match
    /// by straight equality. Everything else passes through unchanged.
    /// </summary>
    private static string? NormalizeSerial(ConsoleType console, string? serial)
    {
        if (console != ConsoleType.FamicomDiskSystem || string.IsNullOrEmpty(serial))
        {
            return serial;
        }

        // No-Intro writes FDS serials as "FMC-ZEL" (and occasionally "FMC-ZEL-REGION"), but the disk
        // header carries only the middle game code ("ZEL") that FamicomDiskSystemHeaderParser reads. Take
        // that second dash-delimited segment so a straight-equality lookup matches the header; a serial
        // that is already the bare code passes through untouched.
        var parts = serial.Split('-');
        return parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : serial;
    }
}
