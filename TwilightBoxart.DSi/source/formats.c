/* What counts as a ROM, and what never does. Client-side knowledge deliberately ENDS at these
   lists: which console a file belongs to, where its serial lives, what its title is - all of that
   is the server's job, worked out from the file name and the header sample the client sends.

   The built-in lists are a FALLBACK, not the truth. A card is flashed once and kept for years, so
   every name frozen into this binary is a fact that expires the day the backend learns better -
   and it expires silently, as a game the scanner walks past or a junk folder it drowns in. Asking
   the server first is what stops that; the lists below are what keep the client working when
   nobody answers. They mirror SupportedFiles in TwilightBoxart.Core. */

#include <ctype.h>
#include <string.h>
#include <strings.h>

#include "formats.h"
#include "networking.h"

/* The lists the backend hands out at /v2/formats, normalised to ",.nds,.gba," - lowercase, with a
   comma on BOTH ends so a substring search cannot match half an entry (".gb" would otherwise hit
   inside ".gbc"). Empty until fetch_formats() succeeds, and that is the whole design: a backend
   that is unreachable, older than this binary, or answering 401 costs nothing, because every
   check below just keeps using its built-in list. */
static char g_server_exts[1024];
static char g_server_skip_dirs[512];
static char g_server_skip_root_dirs[512];
static char g_server_skip_files[512];

/* Lowercases a key's csv payload into the comma-framed shape above. Leaves out empty rather than
   half-filled on anything oversized: a truncated list would silently skip whatever fell off the
   end, which is the exact failure this endpoint exists to remove. */
static void store_csv(const char *value, size_t length, char *out, size_t out_size)
{
    if (length == 0 || length + 3 > out_size) {
        out[0] = '\0';
        return;
    }

    size_t o = 0;
    out[o++] = ',';
    for (size_t i = 0; i < length; i++)
        out[o++] = (char)tolower((unsigned char)value[i]);
    out[o++] = ',';
    out[o] = '\0';
}

/* True when entry, lowercased and comma-framed, appears in a stored list. An empty list never
   matches, and an entry too long for the needle cannot be one the server sent. */
static bool in_server_list(const char *list, const char *entry)
{
    if (list[0] == '\0')
        return false;

    char needle[64];
    size_t length = strlen(entry);
    if (length + 3 > sizeof(needle))
        return false;

    size_t o = 0;
    needle[o++] = ',';
    for (size_t i = 0; i < length; i++)
        needle[o++] = (char)tolower((unsigned char)entry[i]);
    needle[o++] = ',';
    needle[o] = '\0';
    return strstr(list, needle) != NULL;
}

const char *file_ext(const char *name)
{
    const char *dot = strrchr(name, '.');
    return dot ? dot : "";
}

bool is_ds_ext(const char *ext)
{
    static const char *exts[] = { ".nds", ".ds", ".dsi", ".srl", ".ids", ".app" };
    for (unsigned i = 0; i < sizeof(exts) / sizeof(exts[0]); i++) {
        if (strcasecmp(ext, exts[i]) == 0)
            return true;
    }
    return false;
}

bool is_rom_ext(const char *ext)
{
    static const char *exts[] = {
        ".nds", ".ds", ".dsi", ".srl", ".ids", ".app",
        ".gba", ".agb", ".mb", ".gb", ".sgb", ".gbc",
        ".nes", ".fds", ".sfc", ".smc", ".snes",
        ".n64", ".z64", ".v64", ".gen", ".md", ".sms", ".gg",
        ".min", ".sg", ".sc", ".pce", ".ws", ".wsc", ".ngp", ".ngc",
        ".a26", ".a52", ".a78", ".col", ".int", ".msx",
    };

    if (*ext == '\0')
        return false;

    if (g_server_exts[0] != '\0')
        return in_server_list(g_server_exts, ext);

    for (unsigned i = 0; i < sizeof(exts) / sizeof(exts[0]); i++) {
        if (strcasecmp(ext, exts[i]) == 0)
            return true;
    }
    return false;
}

/* Root directories that are never ROM storage: the DSi NAND layout that hiyaCFW and Unlaunch
   mirror onto the SD card - title/ alone holds hundreds of content .app files that can never
   identify, re-missed on every scan - plus the 3DS equivalent for cards that also boot
   TWiLightMenu++ on a 3DS, and the OS folders Windows and macOS plant on removable media.
   Root-only on purpose: these names only mean anything at the top of a card, and a ROM folder
   someone called "sys" deeper down must still be scanned. (tmp is here and not in the served
   list: on a card it is NAND junk, anywhere else it is somebody's folder.) */
static bool is_system_root_dir(const char *name)
{
    static const char *dirs[] = {
        "hiya", "title", "ticket", "sys", "shared1", "shared2",
        "import", "progress", "tmp", "private", "Nintendo 3DS",
        "System Volume Information", "$RECYCLE.BIN", "found.000",
    };
    for (unsigned i = 0; i < sizeof(dirs) / sizeof(dirs[0]); i++) {
        if (strcasecmp(name, dirs[i]) == 0)
            return true;
    }
    return false;
}

bool is_skip_dir(const char *name, int depth)
{
    /* This app's own output is never input, whatever any list says. */
    if (depth == 0 && (strcasecmp(name, "_nds") == 0 || strcasecmp(name, "_pico") == 0))
        return true;

    /* At the root the served list wins when there is one, so the backend can retract a compiled
       name without a reflash; the compiled list answers offline. */
    if (depth == 0 && (g_server_skip_root_dirs[0] != '\0'
                           ? in_server_list(g_server_skip_root_dirs, name)
                           : is_system_root_dir(name)))
        return true;

    /* Names that are junk wherever they sit ("hiya", "Nintendo 3DS", the launchers' data). */
    return in_server_list(g_server_skip_dirs, name);
}

/* Documentation stems that are never ROMs whatever their extension: .md is Markdown as well as
   Mega Drive, so scene pack READMEs scanned as ROMs and missed twice (by name, then on the CRC
   retry). Compared on the name minus its last extension. */
bool is_junk_file(const char *name)
{
    static const char *stems[] = {
        "readme", "license", "licence", "copying", "changelog", "authors",
        "contributing", "notice", "notices", "third_party_notices", "pdf_source_release",
    };

    const char *dot = strrchr(name, '.');
    size_t length = dot ? (size_t)(dot - name) : strlen(name);

    char stem[64];
    if (length == 0 || length >= sizeof(stem))
        return false;
    for (size_t i = 0; i < length; i++)
        stem[i] = (char)tolower((unsigned char)name[i]);
    stem[length] = '\0';

    if (g_server_skip_files[0] != '\0')
        return in_server_list(g_server_skip_files, stem);

    for (unsigned i = 0; i < sizeof(stems) / sizeof(stems[0]); i++) {
        if (strcmp(stem, stems[i]) == 0)
            return true;
    }
    return false;
}

/* Fills the g_server_* buffers from the backend. Best effort by construction: every failure path
   leaves a buffer empty, which hands its check back to the built-in list. Nothing here reports an
   error, because there is no error to report - an older or offline backend simply means this
   binary scans what it already knew about. */
void fetch_formats(void)
{
    /* Straight into memory: the answer is a few hundred bytes of configuration the card never
       needs to see. An answer too big for the buffer fails the request outright (see
       http_get_to_buffer), which is the right reading - a half-parsed list would silently skip
       whatever fell off the end, so oversized means "no answer" and the built-in lists keep
       working. Static: the DS stack is small. */
    static char body[2048];
    if (http_get_to_buffer("/v2/formats", body, sizeof(body)) != 200)
        return;

    /* One key=csv pair per line. A key this binary does not know is skipped rather than rejected,
       so the backend can add keys later without this build having to understand them. */
    for (const char *line = body; line && *line; ) {
        if (strncmp(line, "rom=", 4) == 0)
            store_csv(line + 4, strcspn(line + 4, "\r\n"), g_server_exts, sizeof(g_server_exts));
        else if (strncmp(line, "skipdirs=", 9) == 0)
            store_csv(line + 9, strcspn(line + 9, "\r\n"), g_server_skip_dirs, sizeof(g_server_skip_dirs));
        else if (strncmp(line, "skiprootdirs=", 13) == 0)
            store_csv(line + 13, strcspn(line + 13, "\r\n"),
                      g_server_skip_root_dirs, sizeof(g_server_skip_root_dirs));
        else if (strncmp(line, "skipfiles=", 10) == 0)
            store_csv(line + 10, strcspn(line + 10, "\r\n"), g_server_skip_files, sizeof(g_server_skip_files));

        line = strchr(line, '\n');
        if (line)
            line++;
    }
}
