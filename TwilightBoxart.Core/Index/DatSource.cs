using System.Text.Json;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Index;

/// <summary>One DAT to ingest: where it comes from and which console its rows belong to.</summary>
public sealed record DatSource
{
    /// <summary>
    /// The DAT's No-Intro name, e.g. "Nintendo - Game Boy". Doubles as the default file name and, on a
    /// local build, as the stem matched against files on disk.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The console every row from this DAT is attributed to.</summary>
    public required ConsoleType Console { get; init; }

    /// <summary>Absolute URL override. When null the URL comes from the base template.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// When true a 404 is a warning, not a failure. Set on DATs that only some mirrors carry; the DS
    /// Download Play and plain DSi sets in particular come and go.
    /// </summary>
    public bool Optional { get; init; }

    /// <summary>
    /// Resolves the download URL. <paramref name="baseUrlTemplate"/> must contain <c>{name}</c>, which is
    /// replaced with the URL-escaped DAT name. Keeping it a template is the point: No-Intro's own
    /// datomatic requires a captcha, so CI pulls from a mirror, and which mirror must stay a setting
    /// rather than a constant compiled into the tool.
    /// </summary>
    public string ResolveUrl(string baseUrlTemplate) =>
        Url ?? baseUrlTemplate.Replace("{name}", Uri.EscapeDataString(Name), StringComparison.Ordinal);
}

/// <summary>
/// The DAT-name to <see cref="ConsoleType"/> mapping, and the default set of DATs to ingest.
/// <para>
/// Several No-Intro sets fold into one <see cref="ConsoleType"/>. That is deliberate, and matches how
/// the 2020 crawler mapped the DSi (Digital) set onto NintendoDSi: a DS ROM's
/// SHA-1 may well be listed in the DSi set and vice versa, so merging them keeps a lookup from missing
/// purely because the dump was filed under the sibling set.
/// </para>
/// </summary>
public sealed class DatCatalog
{
    /// <summary>
    /// libretro-database's No-Intro mirror. Chosen as the default because it is a plain raw.githubusercontent
    /// GET with no captcha, no cookies and no rate limit, so CI needs no manual step.
    /// </summary>
    public const string DefaultBaseUrlTemplate =
        "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/no-intro/{name}.dat";

    private readonly Dictionary<string, ConsoleType> _byName;

    private DatCatalog(IReadOnlyList<DatSource> sources)
    {
        Sources = sources;

        // Ordinal-ignore-case, never culture-sensitive: a Turkish-locale CI runner comparing
        // "Nintendo - Nintendo DSi" case-insensitively under the current culture does not match itself.
        _byName = new Dictionary<string, ConsoleType>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            _byName[source.Name] = source.Console;
        }
    }

    public IReadOnlyList<DatSource> Sources { get; }

    /// <summary>Maps a DAT name (from a file stem or the DAT's own header) onto a console.</summary>
    public bool TryResolve(string? datName, out ConsoleType console)
    {
        console = ConsoleType.Unknown;
        return !string.IsNullOrWhiteSpace(datName) && _byName.TryGetValue(datName.Trim(), out console);
    }

    /// <summary>
    /// The built-in source list. Every <see cref="ConsoleType"/> the client can identify, plus the
    /// sibling No-Intro sets that fold into one of them.
    /// </summary>
    public static DatCatalog Default { get; } = new(
    [
        Primary(ConsoleType.GameBoy),
        Primary(ConsoleType.GameBoyColor),
        Primary(ConsoleType.GameBoyAdvance),
        Primary(ConsoleType.NintendoDs),

        // Folded into NintendoDs: Download Play dumps are DS ROMs, and a card in the wild is just a DS card.
        new DatSource
        {
            Name = "Nintendo - Nintendo DS (Download Play)",
            Console = ConsoleType.NintendoDs,
            Optional = true,
        },

        // Spelled out rather than left to Primary() only because the "(Digital)" variant below has to
        // sit next to it. Both libretro repositories - the No-Intro mirror and the thumbnails org - call
        // this set plain "Nintendo - Nintendo DSi", which is what ConsoleType.NintendoDsi.Description()
        // now returns. Verified against a live build: this DAT is 1,068 games at 99.3% serial coverage,
        // which is precisely the DSiWare set the coverage baseline was measured over.
        new DatSource
        {
            Name = "Nintendo - Nintendo DSi",
            Console = ConsoleType.NintendoDsi,
        },

        // Kept optional so a mirror that does use the No-Intro "(Digital)" spelling is picked up too.
        new DatSource
        {
            Name = "Nintendo - Nintendo DSi (Digital)",
            Console = ConsoleType.NintendoDsi,
            Optional = true,
        },

        Primary(ConsoleType.Nes),
        Primary(ConsoleType.Snes),
        Primary(ConsoleType.Nintendo64),
        Primary(ConsoleType.FamicomDiskSystem),
        Primary(ConsoleType.MegaDrive),
        Primary(ConsoleType.MasterSystem),
        Primary(ConsoleType.GameGear),

        // The systems TWiLightMenu++ emulates beyond the Nintendo/Sega core. Every one of these was
        // checked against both libretro repositories before being added, because a console needs BOTH
        // to be worth anything: the No-Intro DAT to identify the dump and the thumbnails repository to
        // dress it. A system with only one of the two is the DSi bug again.
        Primary(ConsoleType.PcEngine),
        Primary(ConsoleType.WonderSwan),
        Primary(ConsoleType.WonderSwanColor),
        Primary(ConsoleType.NeoGeoPocket),
        Primary(ConsoleType.NeoGeoPocketColor),
        Primary(ConsoleType.Atari2600),
        Primary(ConsoleType.Atari5200),
        Primary(ConsoleType.Atari7800),
        Primary(ConsoleType.ColecoVision),
        Primary(ConsoleType.Intellivision),
        Primary(ConsoleType.PokemonMini),
        Primary(ConsoleType.Msx),
        Primary(ConsoleType.Msx2),

        // Lowest measured art coverage of the batch by a wide margin (~29% of primary releases against
        // 67-96% for everything else, including consoles already shipping). The No-Intro set is heavy
        // with Korean and Taiwanese releases libretro never carried art for. Kept because the menu does
        // emulate .sg/.sc and ~60 real covers beats none, but do not read a miss here as a defect.
        Primary(ConsoleType.Sg1000),
    ]);

    /// <summary>
    /// Loads a source list from JSON, replacing the built-in one entirely. Shape:
    /// <c>[{ "name": "Nintendo - Game Boy", "console": "GameBoy", "url": null, "optional": false }]</c>.
    /// </summary>
    public static DatCatalog Load(string path)
    {
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<List<SourceJson>>(json, JsonOptions)
                  ?? throw new InvalidDataException($"'{path}' did not contain a source array.");

        var sources = new List<DatSource>(raw.Count);
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                throw new InvalidDataException($"'{path}' contains a source with no name.");
            }

            if (!Enum.TryParse<ConsoleType>(item.Console, ignoreCase: true, out var console) ||
                console == ConsoleType.Unknown)
            {
                throw new InvalidDataException(
                    $"'{path}': source '{item.Name}' has unknown console '{item.Console}'. " +
                    $"Expected one of: {string.Join(", ", Enum.GetNames<ConsoleType>())}.");
            }

            sources.Add(new DatSource
            {
                Name = item.Name.Trim(),
                Console = console,
                Url = string.IsNullOrWhiteSpace(item.Url) ? null : item.Url.Trim(),
                Optional = item.Optional,
            });
        }

        return new DatCatalog(sources);
    }

    private static DatSource Primary(ConsoleType console) => new()
    {
        Name = console.Description(),
        Console = console,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class SourceJson
    {
        public string? Name { get; set; }

        public string? Console { get; set; }

        public string? Url { get; set; }

        public bool Optional { get; set; }
    }
}
