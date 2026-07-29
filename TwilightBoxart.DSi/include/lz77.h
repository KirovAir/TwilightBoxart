#ifndef LZ77_H
#define LZ77_H

#include <nds.h>
#include <stdbool.h>

/* Nintendo-LZ77-wrapped ROMs: the ".lz77.<ext>" convention TWiLightMenu++ / nds-bootstrap loads for
   SNES, Mega Drive/Genesis, Master System, Game Gear and PC Engine (it decompresses them on-device
   before launching their emulator cores). See lz77.c for the format and why it is NOT DEFLATE. */

/* True when NAME is "<stem>.lz77.<ext>" for a console that appears LZ77-compressed on a card. */
bool is_lz77_name(const char *name);

/* CRC32 and byte count of the DECOMPRESSED ROM at PATH, streamed so the ROM is never held whole.
   Returns false on a read error or a stream that is not well-formed LZ77; sets *cancelled and returns
   false when B is held. Shaped as a drop-in for file_crc32. */
bool lz77_decode(const char *path, u32 *result, u32 *size, bool *cancelled);

#endif
