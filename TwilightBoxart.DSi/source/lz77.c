// Nintendo LZ77 (LZSS, type 0x10) support for the ".lz77.<ext>" convention TWiLightMenu++ uses to fit
// SNES / Mega Drive / Master System / Game Gear / PC Engine ROMs onto a card. nds-bootstrap decompresses
// exactly these on-device (its hb/arm9/source/main.cpp + LZ77_Decompress in lzss.c), so a cover the
// scanner produces for one is a cover the menu can use.
//
// This is deliberately NOT DEFLATE: DEFLATE is LZ77 + Huffman, bit-packed; this is plain byte-aligned
// LZSS with a 4-byte header (0x10, then a 24-bit little-endian length), which is the format the GBA/DS
// BIOS decompresses in hardware. The pre-2.0 desktop client's commented-out DeflateStream stub could
// never have matched these files. The decode here mirrors Lz77RomProbe.TryDecompress in
// TwilightBoxart.Core (which is unit-tested), with the bounds checks a file scanner needs.

#include "lz77.h"

#include <stdio.h>
#include <string.h>
#include <strings.h>

/* True when NAME is "<stem>.lz77.<ext>" and <ext> is a console nds-bootstrap decompresses on-device:
   SNES, Mega Drive/Genesis, Master System, Game Gear or PC Engine. This is the exact set its
   hb/arm9/source/main.cpp checks before launching their emulator cores. */
bool is_lz77_name(const char *name)
{
    static const char *exts[] = { ".sfc", ".smc", ".gen", ".md", ".sms", ".gg", ".pce" };

    const char *ext = strrchr(name, '.');
    if (!ext)
        return false;

    bool console = false;
    for (unsigned i = 0; i < sizeof(exts) / sizeof(exts[0]); i++) {
        if (strcasecmp(ext, exts[i]) == 0) {
            console = true;
            break;
        }
    }
    if (!console)
        return false;

    /* The five characters immediately before the extension must be ".lz77". */
    if (ext - name < 5)
        return false;
    return strncasecmp(ext - 5, ".lz77", 5) == 0;
}

/* A refillable byte source over a file. next() returns -1 at end-of-file or on a read error. */
typedef struct {
    FILE *file;
    unsigned char *buffer;
    size_t capacity;
    size_t length;
    size_t position;
    bool error;
} Lz77Reader;

static int lz77_next(Lz77Reader *r)
{
    if (r->position >= r->length) {
        r->length = fread(r->buffer, 1, r->capacity, r->file);
        r->position = 0;
        if (r->length == 0) {
            if (ferror(r->file))
                r->error = true;
            return -1;
        }
    }
    return r->buffer[r->position++];
}

/* Streaming decompressor: the CRC32 and byte count of the DECOMPRESSED ROM, without ever holding it in
   RAM. A back-reference reaches at most 4096 bytes, so a 4 KB ring buffer is the entire window we need;
   every output byte is folded straight into the CRC. That is what keeps a 6-12 MB SNES / Mega Drive ROM
   off a 4 MB DS's heap - only ~37 KB of static buffers, no allocation that scales with the ROM. Shaped
   as a drop-in for file_crc32 so the CRC-retry path can treat an ".lz77.sfc" exactly as it treats a
   plain ".sfc". Returns false on truncated or non-LZ77 input, so a bad file is a miss, never a crash.
   Holding B cancels. */
bool lz77_decode(const char *path, u32 *result, u32 *size, bool *cancelled)
{
    static u32 table[256];
    static bool table_ready;
    static unsigned char input[32 * 1024];
    static unsigned char window[4096]; /* == the maximum back-reference distance */

    *cancelled = false;

    if (!table_ready) {
        for (u32 i = 0; i < 256; i++) {
            u32 value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value >> 1) ^ ((value & 1) ? 0xEDB88320 : 0);
            table[i] = value;
        }
        table_ready = true;
    }

    FILE *f = fopen(path, "rb");
    if (!f)
        return false;

    Lz77Reader reader = { f, input, sizeof(input), 0, 0, false };

    /* 4-byte header: type 0x10, then a 24-bit little-endian decompressed length. */
    int type = lz77_next(&reader);
    int len0 = lz77_next(&reader);
    int len1 = lz77_next(&reader);
    int len2 = lz77_next(&reader);
    if (type != 0x10 || len2 < 0) {
        fclose(f);
        return false;
    }

    u32 declared = (u32)len0 | ((u32)len1 << 8) | ((u32)len2 << 16);
    if (declared == 0) {
        fclose(f);
        return false;
    }

    u32 crc = 0xFFFFFFFF;
    u32 produced = 0;
    u32 next_yield = 128 * 1024;
    bool ok = true;

    while (produced < declared) {
        int flags = lz77_next(&reader);
        if (flags < 0) {
            ok = false;
            break;
        }

        for (int bit = 0; bit < 8 && produced < declared; bit++, flags <<= 1) {
            if ((flags & 0x80) == 0) {
                int literal = lz77_next(&reader);
                if (literal < 0) {
                    ok = false;
                    break;
                }
                unsigned char value = (unsigned char)literal;
                window[produced & 4095] = value;
                crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
                produced++;
            } else {
                int high = lz77_next(&reader);
                int low = lz77_next(&reader);
                if (high < 0 || low < 0) {
                    ok = false;
                    break;
                }
                u32 distance = (u32)(((high & 0x0F) << 8) | low) + 1;
                u32 run = (u32)(high >> 4) + 3;
                /* A distance past the start, or a run past the declared end, is a corrupt stream. */
                if (distance > produced || run > declared - produced) {
                    ok = false;
                    break;
                }
                for (u32 j = 0; j < run; j++) {
                    unsigned char value = window[(produced - distance) & 4095];
                    window[produced & 4095] = value;
                    crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
                    produced++;
                }
            }
        }

        if (!ok)
            break;

        /* Same cadence as file_crc32: yield to the vblank cothread and poll cancel, but not so often
           that the decode crawls. */
        if (produced >= next_yield) {
            next_yield += 128 * 1024;
            cothread_yield_irq(IRQ_VBLANK);
            scanKeys();
            if (keysHeld() & KEY_B) {
                *cancelled = true;
                fclose(f);
                return false;
            }
        }
    }

    if (reader.error)
        ok = false;

    fclose(f);
    if (!ok)
        return false;

    *result = crc ^ 0xFFFFFFFF;
    *size = produced; /* == declared on success */
    return true;
}
