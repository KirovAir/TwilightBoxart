#ifndef FORMATS_H
#define FORMATS_H

#include <stdbool.h>

/* What is worth scanning and what must be walked past, split out of main.c because it is the part
   that keeps growing: the backend extends every list at /v2/formats, and this module owns the
   fetch, the parsing and the compiled-in fallbacks it degrades to. */

/* The extension including its dot, or "" when the name has none. */
const char *file_ext(const char *name);

/* DS-family extensions, whose header title id identifies them without a CRC. */
bool is_ds_ext(const char *ext);

/* Extensions worth scanning. The server's rom= list wins once fetched; the built-in list otherwise. */
bool is_rom_ext(const char *ext);

/* Directories that never hold ROMs. The generic NAND names (compiled-in, or the server's
   skiprootdirs= once fetched) apply at the card root only (depth 0); the server's skipdirs=
   list applies at any depth; _nds and _pico are always skipped at the root. */
bool is_skip_dir(const char *name, int depth);

/* Documentation wearing a ROM extension - README.md is Markdown, not Mega Drive. */
bool is_junk_file(const char *name);

/* Ask the backend for all of the above in one request. Silent and best effort: on any failure the
   compiled-in lists answer, so an older or offline backend costs nothing. */
void fetch_formats(void);

#endif
