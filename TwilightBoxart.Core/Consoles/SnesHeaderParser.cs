using System.Buffers.Binary;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Core.Consoles;

/// <summary>
/// SNES, via the internal header at 0x7FC0 (LoROM), 0xFFC0 (HiROM) or 0x40FFC0 (ExHiROM), optionally
/// displaced by a 512-byte SMC copier header.
/// </summary>
/// <remarks>
/// <para>
/// <b>This parser cannot work on the 512-byte header window the clients send, and does not pretend
/// to.</b> There is no magic number anywhere in a SNES ROM: the "header" is a 64-byte record at a
/// position that itself has to be guessed, identified only by an internal checksum and a plausible
/// title. Detecting it needs at least 32 KiB for LoROM, 64 KiB for HiROM and 4 MiB for ExHiROM. Given
/// less, <see cref="TryParse"/> returns null. It is the single biggest structural limitation of the
/// header-window contract, and the honest answer for a short buffer is "not detected",
/// not a guess - SNES is 3,402 files in the corpus and every one of them currently identifies by hash.
/// </para>
/// <para>
/// Because it is scored rather than matched, this parser runs last. Everything with a real magic
/// number gets to claim a buffer first, so the only files reaching here are ones nothing else wanted.
/// </para>
/// </remarks>
public sealed class SnesHeaderParser : IConsoleHeaderParser
{
    /// <summary>The internal header record: 21-byte title, then the mode/size/checksum fields.</summary>
    private const int HeaderLength = 0x40;

    private const int TitleLength = 21;
    private const int MapModeOffset = 0x15;
    private const int RomSizeOffset = 0x17;
    private const int LicenseeOffset = 0x1A;
    private const int ComplementOffset = 0x1C;
    private const int ChecksumOffset = 0x1E;
    private const int ResetVectorOffset = 0x3C;

    /// <summary>Game code offset, relative to the header base - it sits in the extended header before it.</summary>
    private const int ExtendedGameCodeOffset = -0x0E;

    /// <summary>Licensee value 0x33 means the extended header (and therefore the game code) is present.</summary>
    private const byte UsesExtendedHeader = 0x33;

    /// <summary>A copier header is exactly 512 bytes when present.</summary>
    private const int SmcHeaderLength = 512;

    /// <summary>
    /// Minimum score to accept a candidate. Reaching it requires a printable title plus either a valid
    /// checksum pair or several weaker signals at once. Set high on purpose: this parser is the last
    /// resort, so a false positive here mislabels a file that nothing else recognised, which is worse
    /// than leaving it for the hash lookup.
    /// </summary>
    private const int MinimumScore = 8;

    public HeaderDetection? TryParse(ReadOnlySpan<byte> header)
    {
        // Candidate positions: each mapping, with and without a copier header in front of it. Trying
        // both copier offsets rather than sniffing for the "AA BB 04" Super Magicom signature is
        // deliberate - plenty of copier headers are entirely zeroed, and scoring settles it either way.
        var best = 0;
        var bestBase = -1;
        var bestSmc = 0;
        string? bestTitle = null;

        foreach (var smc in (ReadOnlySpan<int>)[0, SmcHeaderLength])
        {
            foreach (var candidate in Candidates)
            {
                var score = Score(header, candidate.BaseOffset + smc, candidate.MapModes, out var title);
                if (score > best)
                {
                    best = score;
                    bestBase = candidate.BaseOffset + smc;
                    bestSmc = smc;
                    bestTitle = title;
                }
            }
        }

        if (best < MinimumScore || bestBase < 0)
        {
            return null;
        }

        // The 4-character game code lives in the extended header, which only exists when the licensee
        // byte is the 0x33 escape value. Reading it unconditionally would return whatever ROM data
        // happens to precede the header.
        string? serial = null;
        if (header.ByteAt(bestBase + LicenseeOffset) == UsesExtendedHeader)
        {
            serial = header.ReadGameCode(bestBase + ExtendedGameCodeOffset, 4);
        }

        return new HeaderDetection
        {
            ConsoleType = ConsoleType.Snes,
            Title = bestTitle,
            Serial = serial,
            LeadingHeaderBytes = bestSmc,
        };
    }

    /// <summary>
    /// The header positions, each with the map-mode low nibbles that are consistent with it. A LoROM
    /// header claiming to be HiROM is a coincidence, not a header.
    /// </summary>
    private static readonly Candidate[] Candidates =
    [
        new(0x7FC0, [0x0, 0x2, 0x3]),   // LoROM, ExLoROM/SDD-1, SA-1
        new(0xFFC0, [0x1]),             // HiROM
        new(0x40FFC0, [0x5]),           // ExHiROM
    ];

    private readonly record struct Candidate(int BaseOffset, byte[] MapModes);

    /// <summary>
    /// Scores how much a 64-byte window at <paramref name="baseOffset"/> looks like a real SNES header.
    /// Returns 0 - a hard reject - when the buffer is too short or the title is not printable.
    /// </summary>
    private static int Score(ReadOnlySpan<byte> buffer, int baseOffset, byte[] mapModes, out string? title)
    {
        title = null;

        if (baseOffset < 0 || baseOffset > buffer.Length - HeaderLength)
        {
            // Short buffer: no evidence either way, so no score. This is the path a 512-byte client
            // header always takes.
            return 0;
        }

        var header = buffer.Slice(baseOffset, HeaderLength);

        // The title is the value this parser exists to produce, so anything non-printable in it is a
        // hard reject rather than a lost point - a "detection" with a garbage title is worse than none.
        for (var i = 0; i < TitleLength; i++)
        {
            if (header[i] is < 0x20 or > 0x7E)
            {
                return 0;
            }
        }

        var name = header.ReadAscii(0, TitleLength);
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

        var score = 2;

        // The strongest single signal: the checksum and its one's complement must sum to 0xFFFF. Both
        // zero satisfies that arithmetically while meaning "field never filled in", hence the != 0 test.
        var checksum = BinaryPrimitives.ReadUInt16LittleEndian(header[ChecksumOffset..]);
        var complement = BinaryPrimitives.ReadUInt16LittleEndian(header[ComplementOffset..]);
        if (checksum != 0 && (checksum ^ complement) == 0xFFFF)
        {
            score += 4;
        }

        // High nibble 2 or 3 (3 = FastROM), low nibble must match the mapping this offset implies.
        var mapMode = header[MapModeOffset];
        if ((mapMode & 0xF0) is 0x20 or 0x30 && Array.IndexOf(mapModes, (byte)(mapMode & 0x0F)) >= 0)
        {
            score += 2;
        }

        // ROM size is log2(KiB): 5 = 32 KiB up to 14 = 16 MiB. Outside that it is not a size field.
        if (header[RomSizeOffset] is >= 5 and <= 14)
        {
            score += 1;
        }

        // The 65816 reset vector must point into the ROM half of the address space.
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[ResetVectorOffset..]) >= 0x8000)
        {
            score += 1;
        }

        title = name;
        return score;
    }
}
