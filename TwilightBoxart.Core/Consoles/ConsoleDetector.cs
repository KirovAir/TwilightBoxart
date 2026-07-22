namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// Identifies a ROM's console from its leading bytes by running every <see cref="IConsoleHeaderParser"/>
/// in a false-positive-safe order and taking the first match.
/// </summary>
/// <remarks>
/// <para>
/// This is the entry point for the identification ladder's rung 1 (header serial - free, 512 bytes,
/// and the highest-coverage method for DSi/DS/Mega Drive/GBA/FDS). It is a pure function of the bytes:
/// no I/O, no index, no filename. Everything the header cannot settle on its own is surfaced on
/// <see cref="HeaderDetection"/> for the ladder to resolve with a lookup.
/// </para>
///
/// <para><b>Why the order is what it is.</b> First match wins, so ordering is a correctness property.
/// The rule is: a parser may only run after every parser whose evidence would beat it. Concretely,
/// in <see cref="DefaultParsers"/> order:</para>
///
/// <list type="number">
/// <item>
/// <b>GBA (magic at 0x04) before NDS (the same magic at 0xC0).</b> This direction is structurally safe
/// and the reverse is not. On an NDS ROM, offset 0x04 falls inside the 12-byte ASCII title field, which
/// cannot contain the 0xFF and 0xAE bytes the magic requires - so a DS ROM can never be claimed by the
/// GBA parser. Going the other way is only probabilistically safe: 0xC0 on a GBA cart is the joybus
/// entry point and ordinary ARM code, so nothing but a 1-in-2^32 argument stops it matching.
/// </item>
/// <item>
/// <b>The offset-0 magics (NES, FDS, N64) before Game Boy.</b> Game Boy's fallback probe is the
/// entrypoint heuristic <c>00 C3 50 01</c> at 0x100 - four bytes of arbitrary code, kept only because
/// it is the one thing that catches logo-less unlicensed carts. Offset 0 in a NES/FDS/N64 image is a
/// fixed magic and offset 0x100 is arbitrary program data, so a NES ROM could in principle satisfy the
/// Game Boy heuristic. Letting the definitive magics claim the buffer first removes the question. The
/// three offset-0 parsers do not conflict with each other - their magics are mutually exclusive - so
/// their relative order is free.
/// </item>
/// <item>
/// <b>Game Boy before Mega Drive.</b> Both probe 0x100. The values ("SEGA" vs <c>00 C3 50 01</c>) are
/// mutually exclusive so neither can steal the other, but Game Boy leads with a logo match at 0x104,
/// which is the stronger of the two claims on the same region of the file.
/// </item>
/// <item>
/// <b>Mega Drive before Master System / Game Gear.</b> "TMR SEGA" at 0x7FF0 is 32 KiB into the file,
/// well inside a Mega Drive ROM's data, where an 8-byte ASCII string is entirely possible. The Mega
/// Drive header at 0x100 is definitive, so it must get to claim the file first.
/// </item>
/// <item>
/// <b>SNES last, always.</b> It is the only parser with no magic number at all - it scores candidate
/// windows on a checksum and a printable title. Anything carrying real evidence has to be given the
/// chance to claim the buffer before a heuristic is allowed to.
/// </item>
/// </list>
///
/// <para>
/// Parsers never throw. A short, empty or hostile buffer produces <see cref="HeaderDetection.None"/>.
/// </para>
/// </remarks>
public static class ConsoleDetector
{
    /// <summary>The parsers, already in the order described on this type.</summary>
    public static IReadOnlyList<IConsoleHeaderParser> DefaultParsers { get; } =
    [
        new GameBoyAdvanceHeaderParser(),        // magic at 0x04
        new NintendoDsHeaderParser(),            // magic at 0xC0
        new NesHeaderParser(),                   // magic at 0x00
        new FamicomDiskSystemHeaderParser(),     // magic at 0x00
        new Nintendo64HeaderParser(),            // magic at 0x00
        new GameBoyHeaderParser(),               // logo at 0x104, weak heuristic at 0x100
        new MegaDriveHeaderParser(),             // text at 0x100
        new SegaEightBitHeaderParser(),          // text at 0x7FF0/0x3FF0/0x1FF0
        new SnesHeaderParser(),                  // no magic; scored. Must be last.
    ];

    /// <summary>
    /// Detects the console from a ROM's leading bytes.
    /// </summary>
    /// <param name="header">
    /// The ROM's first bytes. 512 is enough for GB, GBC, GBA, NDS, DSi, NES, FDS, N64 and Mega Drive.
    /// Master System, Game Gear and SNES need substantially more and will report no detection below it -
    /// see <see cref="SegaEightBitHeaderParser"/> and <see cref="SnesHeaderParser"/>. May be empty.
    /// </param>
    /// <returns>
    /// The first match, or <see cref="HeaderDetection.None"/>. Never null, and never throws.
    /// </returns>
    public static HeaderDetection Detect(ReadOnlySpan<byte> header)
    {
        foreach (var parser in DefaultParsers)
        {
            var result = parser.TryParse(header);
            if (result is { IsDetected: true })
            {
                return result;
            }
        }

        return HeaderDetection.None;
    }
}
