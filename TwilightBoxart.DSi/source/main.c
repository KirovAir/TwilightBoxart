// TwilightBoxart DSi client. A thin pipe over the backend:
// read a ROM's header, ask the backend for art by serial or name, write the PNG it
// returns into the TWiLightMenu++ boxart folder. No TLS, no JSON, no image decoding.
// The server guarantees every PNG fits TWiLightMenu's 45,056-byte box art slot.

#include <ctype.h>
#include <dirent.h>
#include <errno.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

#include <nds.h>
#include <fat.h>
#include <dswifi9.h>

#include <netdb.h>
#include <netinet/in.h>
#include <sys/socket.h>

#include "logo.h"
#include "bottom.h"
#include "music_bin.h"
#include "tls.h"
#include "credit_font.h"

/* Where the backend lives when the ini does not say otherwise. Nobody should ever have to
   configure anything - but the ini written on first run carries backend_host / backend_port /
   backend_tls, so a self-hoster can point a card at their own server by editing a text file. */
#define DEFAULT_BACKEND_HOST "boxart.kirovair.com"
#define DEFAULT_BACKEND_TLS  1

/* APP_VERSION comes from the Makefile, which reads it out of Directory.Build.props. Deliberately
   no fallback here: a build that cannot work out its own version should fail, not ship a guess. */
#ifndef APP_VERSION
#error "APP_VERSION not defined; build through the Makefile"
#endif

/* Sent on every /v2 request; the server answers 401 without it. Not a secret - it is compiled into
   this binary and published in the repository - it only marks the request as coming from a real
   client rather than from a scraper pointed at the art routes. Keep in step with ApiKey.cs. */
#define API_KEY_HEADER "X-Twilight-Key: tb2_9f4c1d7a3e8b5062"

#define BOXART_DIR  "/_nds/TWiLightMenu/boxart"
#define MAX_DEPTH   8

/* The sample rate music.bin was converted at (Pixel Cart Drift, by Jesse Sander). */
#define MUSIC_RATE  14000
#define MUSIC_VOLUME 80

/* One settings file straight in the homebrew support folder, the nds-bootstrap way. */
#define CONFIG_PATH "/_nds/TwilightBoxart.ini"

typedef struct {
    char ssid[33];
    char key[65];
    int size;       /* 0 classic 128x115, 1 large 168x130, 2 xl 208x143 */
    int border;     /* 0 none, 1 dsi, 2 3ds, 3 black, 4 white */
    bool thick;
    bool overwrite;
    bool quick_scan;
    bool mute;
    char backend_host[128];
    int backend_port;
    int backend_tls; /* -1 while loading = "not in the ini"; resolved to 0/1 before use */
} AppConfig;

static AppConfig g_config;

/* The bottom screen's artwork layer. The keyboard is created lazily on first use, because an
   initialized-but-hidden keyboard layer leaks a tile row onto the artwork; its palette entries
   are swapped with the artwork's while it is up. */
static int sub_bitmap_bg;
static int top_bg;

/* The looping music, so the options screen can silence it. Volume rather than soundKill: the
   sample is a loop with no start cue, so killing it means it can never come back. */
static int music_channel = -1;

static void apply_mute(void)
{
    if (music_channel >= 0)
        soundSetVolume(music_channel, g_config.mute ? 0 : MUSIC_VOLUME);
}
static u16 kb_palette[16];
static bool kb_ready;

static const int SIZE_W[] = { 128, 168, 208 };
static const int SIZE_H[] = { 115, 130, 143 };
static const char *SIZE_NAMES[] = { "Classic", "Large", "XL" };
static const char *BORDER_NAMES[] = { "None", "DSi Theme", "3DS Theme", "Black", "White" };
static const char *BORDER_WIRE[] = { "None", "NintendoDsi", "Nintendo3Ds", "Line", "Line" };

static struct {
    int found, written, skipped, missed, failed;
} counters;

static bool aborted = false;

/* small helpers */

/* VRAM and palette RAM ignore byte writes, and dmaCopy bypasses the data cache, so every copy
   into them goes through this: CPU-driven, halfword-sized, cache-coherent. */
static void vram_copy(void *dst, const void *src, size_t bytes)
{
    u16 *d = dst;
    const u16 *s = src;
    for (size_t i = 0; i < bytes / 2; i++)
        d[i] = s[i];
}

static void wait_for_start(void)
{
    printf("\nPress START to exit.\n");
    while (1) {
        cothread_yield_irq(IRQ_VBLANK);
        scanKeys();
        if (keysDown() & KEY_START)
            break;
    }
}

static bool user_aborted(void)
{
    scanKeys();
    if (keysHeld() & KEY_B)
        aborted = true;
    return aborted;
}

/* Whole-file CRC32 plus the byte count that went into it: the server needs both, because
   stripping a container header (iNES) out of the hash arithmetically only works when it
   knows where the hashed bytes ended. Holding B cancels. */
static bool file_crc32(const char *path, u32 *result, u32 *size, bool *cancelled)
{
    static u32 table[256];
    static bool table_ready;
    static unsigned char buffer[128 * 1024];

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

    u32 crc = 0xFFFFFFFF;
    u32 total = 0;
    size_t got;
    while ((got = fread(buffer, 1, sizeof(buffer), f)) > 0) {
        for (size_t i = 0; i < got; i++)
            crc = table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        total += got;

        cothread_yield_irq(IRQ_VBLANK);
        scanKeys();
        if (keysHeld() & KEY_B) {
            *cancelled = true;
            fclose(f);
            return false;
        }
    }

    bool ok = !ferror(f);
    fclose(f);
    if (ok) {
        *result = crc ^ 0xFFFFFFFF;
        *size = total;
    }
    return ok;
}

static const char *file_ext(const char *name)
{
    const char *dot = strrchr(name, '.');
    return dot ? dot : "";
}

static bool is_ds_ext(const char *ext)
{
    static const char *exts[] = { ".nds", ".ds", ".dsi", ".srl", ".ids", ".app" };
    for (unsigned i = 0; i < sizeof(exts) / sizeof(exts[0]); i++) {
        if (strcasecmp(ext, exts[i]) == 0)
            return true;
    }
    return false;
}

/* The extension list the backend hands out at /v2/formats, normalised to ",.nds,.gba," - lowercase,
   with a comma on BOTH ends so a substring search cannot match half an extension (".gb" would
   otherwise hit inside ".gbc"). Empty until fetch_rom_extensions() succeeds, and that is the whole
   design: a backend that is unreachable, older than this binary, or answering 401 costs nothing,
   because is_rom_ext just keeps using the built-in list. */
static char g_server_exts[1024];

/* Extensions worth scanning. Client-side knowledge deliberately ENDS here, at "is this a ROM":
   which console a file belongs to, where its serial lives, what its title is - all of that is the
   server's job, worked out from the file name and the header sample this client sends along.

   The built-in list is a FALLBACK, not the truth. A card is flashed once and kept for years, so
   every extension frozen into this binary is a fact that expires the day a console is added to the
   backend - and it expires silently, as a game TWiLightMenu++ happily launches while this walks
   straight past it. Asking the server first is what stops that; the list below is what keeps the
   client working when nobody answers. It mirrors SupportedFiles in TwilightBoxart.Core. */
static bool is_rom_ext(const char *ext)
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

    char needle[16];
    size_t length = strlen(ext);

    /* The server's answer wins when we have one and the extension can be framed in commas. An
       extension too long for the buffer is not one the server sent, so the built-in list answers it. */
    if (g_server_exts[0] != '\0' && length + 3 <= sizeof(needle)) {
        size_t o = 0;
        needle[o++] = ',';
        for (size_t i = 0; i < length; i++)
            needle[o++] = (char)tolower((unsigned char)ext[i]);
        needle[o++] = ',';
        needle[o] = '\0';
        return strstr(g_server_exts, needle) != NULL;
    }

    for (unsigned i = 0; i < sizeof(exts) / sizeof(exts[0]); i++) {
        if (strcasecmp(ext, exts[i]) == 0)
            return true;
    }
    return false;
}

/* Percent-encodes into out. Conservative: everything but unreserved chars is escaped. */
static void url_encode(const char *in, char *out, size_t out_size)
{
    size_t o = 0;
    for (const unsigned char *p = (const unsigned char *)in; *p && o + 4 < out_size; p++) {
        if (isalnum(*p) || *p == '-' || *p == '_' || *p == '.' || *p == '~') {
            out[o++] = (char)*p;
        } else {
            o += (size_t)sprintf(out + o, "%%%02X", *p);
        }
    }
    out[o] = '\0';
}

/* Standard base64. The result goes through url_encode afterwards, which escapes '+', '/' and '='
   so none of them can be misread as query syntax. */
static void base64_encode(const unsigned char *in, size_t len, char *out)
{
    static const char alphabet[] =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    size_t o = 0;
    for (size_t i = 0; i < len; i += 3) {
        unsigned v = (unsigned)in[i] << 16;
        if (i + 1 < len) v |= (unsigned)in[i + 1] << 8;
        if (i + 2 < len) v |= in[i + 2];
        out[o++] = alphabet[(v >> 18) & 0x3F];
        out[o++] = alphabet[(v >> 12) & 0x3F];
        out[o++] = i + 1 < len ? alphabet[(v >> 6) & 0x3F] : '=';
        out[o++] = i + 2 < len ? alphabet[v & 0x3F] : '=';
    }
    out[o] = '\0';
}

/* http */

/* Case-insensitive Content-Length lookup in a header block, -1 when absent. */
static long parse_content_length(const char *headers)
{
    for (const char *line = headers; line; line = strchr(line, '\n'), line = line ? line + 1 : NULL) {
        if (strncasecmp(line, "Content-Length:", 15) == 0)
            return atol(line + 15);
    }
    return -1;
}

/* One request rides one connection; these pick the plain or TLS pipe for it. Decided at
   runtime by the ini, since a self-hosted backend on the LAN is usually plain HTTP while the
   hosted default is HTTPS. mbedTLS is always linked either way. */
static int xfer_send(int sock, const void *buf, size_t len)
{
    if (g_config.backend_tls)
        return tls_send(buf, len);
    return send(sock, buf, len, 0);
}

static int xfer_recv(int sock, void *buf, size_t len)
{
    if (g_config.backend_tls)
        return tls_recv(buf, len);
    return recv(sock, buf, len, 0);
}

/* GET the path from the backend into out_path, via a temporary file so a failed or
   truncated transfer never harms art already on the card. Returns the HTTP
   status, or -1 on a transport error. Anything but 200 leaves no file behind. */
static int http_get_to_file(const char *path, const char *out_path)
{
    struct sockaddr_in address = { 0 };
    address.sin_family = AF_INET;
    address.sin_port = htons(g_config.backend_port);

    struct addrinfo hints = { 0 };
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    struct addrinfo *resolved;
    if (getaddrinfo(g_config.backend_host, NULL, &hints, &resolved) != 0 || resolved == NULL)
        return -1;
    address.sin_addr = ((struct sockaddr_in *)resolved->ai_addr)->sin_addr;
    freeaddrinfo(resolved);

    int sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock < 0)
        return -1;

    if (connect(sock, (struct sockaddr *)&address, sizeof(address)) != 0) {
        closesocket(sock);
        return -1;
    }

    if (g_config.backend_tls && !tls_connect(sock)) {
        closesocket(sock);
        return -1;
    }

    /* Sized for the longest query fetch_art can build (a url-encoded 512-byte header sample),
       plus the request line around it. Static: the DS stack is small. A truncated request would
       be sent as garbage, so it is refused outright - snprintf reports the untruncated length. */
    static char request[4096];
    int request_length = snprintf(request, sizeof(request),
        "GET %s HTTP/1.1\r\nHost: %s\r\nUser-Agent: TwilightBoxart-DSi/" APP_VERSION "\r\n"
        API_KEY_HEADER "\r\nConnection: close\r\n\r\n",
        path, g_config.backend_host);
    if (request_length <= 0 || request_length >= (int)sizeof(request)) {
        tls_close();
        closesocket(sock);
        return -1;
    }

    if (xfer_send(sock, request, request_length) != request_length) {
        tls_close();
        closesocket(sock);
        return -1;
    }

    /* Buffer until the blank line so a header split across packets still parses. */
    char header[2048];
    int header_length = 0;
    char *body = NULL;
    while (header_length < (int)sizeof(header) - 1) {
        int received = xfer_recv(sock, header + header_length, sizeof(header) - 1 - header_length);
        if (received <= 0)
            break;
        header_length += received;
        header[header_length] = '\0';
        body = strstr(header, "\r\n\r\n");
        if (body) {
            body += 4;
            break;
        }
    }

    int status = -1;
    if (body && sscanf(header, "HTTP/1.%*c %d", &status) != 1)
        status = -1;
    if (status != 200) {
        tls_close();
        closesocket(sock);
        return status;
    }

    long content_length = parse_content_length(header);

    char tmp_path[520];
    snprintf(tmp_path, sizeof(tmp_path), "%s.tmp", out_path);
    FILE *out = fopen(tmp_path, "wb");
    if (!out) {
        tls_close();
        closesocket(sock);
        return -1;
    }

    bool ok = true;
    long body_written = header_length - (long)(body - header);
    if (body_written > 0 && fwrite(body, 1, (size_t)body_written, out) != (size_t)body_written)
        ok = false;

    char chunk[2048];
    while (ok) {
        int received = xfer_recv(sock, chunk, sizeof(chunk));
        if (received < 0)
            ok = false;
        if (received <= 0)
            break;
        if (fwrite(chunk, 1, (size_t)received, out) != (size_t)received)
            ok = false;
        body_written += received;
    }

    /* A close before Content-Length bytes is a dropped connection, not a PNG. */
    if (body_written <= 0 || (content_length >= 0 && body_written != content_length))
        ok = false;

    /* fclose flushes; a failure here is a truncated file, same as a short write. */
    if (fclose(out) != 0)
        ok = false;
    tls_close();
    closesocket(sock);
    if (!ok) {
        remove(tmp_path);
        return -1;
    }

    /* FatFs rename does not replace, so clear the target first. The new art is
       already complete on disk at this point. */
    remove(out_path);
    if (rename(tmp_path, out_path) != 0) {
        remove(tmp_path);
        return -1;
    }
    return 200;
}

/* Asks the backend which extensions are worth scanning and fills g_server_exts. Best effort by
   construction: every failure path leaves the buffer empty, which hands is_rom_ext back to its
   built-in list. Nothing here reports an error, because there is no error to report - an older or
   offline backend simply means this binary scans what it already knew about. */
static void fetch_rom_extensions(void)
{
    const char *tmp = "/_nds/TwilightBoxart.fmt";
    if (http_get_to_file("/v2/formats", tmp) != 200)
        return;

    FILE *file = fopen(tmp, "rb");
    if (!file)
        return;

    char body[1024];
    size_t read = fread(body, 1, sizeof(body) - 1, file);
    fclose(file);
    remove(tmp);
    body[read] = '\0';

    /* One key=csv pair per line. Only "rom=" matters here; any other line is skipped rather than
       rejected, so the backend can add keys later without this binary having to understand them. */
    for (const char *line = body; line && *line; ) {
        if (strncmp(line, "rom=", 4) == 0) {
            const char *value = line + 4;
            size_t length = strcspn(value, "\r\n");

            /* Leave the buffer empty rather than half-filled: a truncated list would silently skip
               whatever fell off the end, which is the exact failure this endpoint exists to remove. */
            if (length == 0 || length + 3 > sizeof(g_server_exts))
                return;

            size_t o = 0;
            g_server_exts[o++] = ',';
            for (size_t i = 0; i < length; i++)
                g_server_exts[o++] = (char)tolower((unsigned char)value[i]);
            g_server_exts[o++] = ',';
            g_server_exts[o] = '\0';
            return;
        }

        line = strchr(line, '\n');
        if (line)
            line++;
    }
}

/* the scan */

/* Bytes of the file's head we hand the server. 512 covers every header its parsers read. */
#define HEADER_SAMPLE 512

#define VISIBLE_RESULTS 8
#define MAX_SCAN_RESULTS 4096

typedef struct {
    char name[27];
    char result;
    char *path;
    u32 crc32;
    bool crc32_known;
} ScanResult;

static ScanResult *scan_results;
static size_t scan_result_count;
static size_t scan_result_capacity;
static bool scan_history_complete = true;

static void reset_scan_results(void)
{
    for (size_t i = 0; i < scan_result_count; i++)
        free(scan_results[i].path);
    free(scan_results);
    scan_results = NULL;
    scan_result_count = 0;
    scan_result_capacity = 0;
    scan_history_complete = true;
}

static void remember_result(
    const char *path, const char *name, char result, bool crc32_known, u32 crc32)
{
    if (scan_result_count == MAX_SCAN_RESULTS) {
        scan_history_complete = false;
        return;
    }

    if (scan_result_count == scan_result_capacity) {
        size_t capacity = scan_result_capacity == 0 ? 64 : scan_result_capacity * 2;
        if (capacity > MAX_SCAN_RESULTS)
            capacity = MAX_SCAN_RESULTS;
        ScanResult *grown = realloc(scan_results, capacity * sizeof(*scan_results));
        if (!grown) {
            scan_history_complete = false;
            return;
        }
        scan_results = grown;
        scan_result_capacity = capacity;
    }

    char *saved_path = malloc(strlen(path) + 1);
    if (!saved_path) {
        scan_history_complete = false;
        return;
    }
    strcpy(saved_path, path);

    ScanResult *entry = &scan_results[scan_result_count++];
    snprintf(entry->name, sizeof(entry->name), "%.26s", name);
    entry->result = result;
    entry->path = saved_path;
    entry->crc32 = crc32;
    entry->crc32_known = crc32_known;
}

static const char *result_label(char result)
{
    return result == '+' ? "got "
           : result == '=' ? "have"
           : result == '-' ? "miss"
           : "fail";
}

static const char *result_colour(char result)
{
    return result == '+' ? "\x1b[32;1m"
           : result == '=' ? "\x1b[36;1m"
           : result == '-' ? "\x1b[33;1m"
           : "\x1b[31;1m";
}

static void print_recent_results(void)
{
    printf("Recent:\n");
    size_t first = scan_result_count > VISIBLE_RESULTS ? scan_result_count - VISIBLE_RESULTS : 0;
    for (size_t i = first; i < scan_result_count; i++) {
        const ScanResult *entry = &scan_results[i];
        printf("%s%-4s \x1b[37;1m%s\n",
               result_colour(entry->result), result_label(entry->result), entry->name);
    }
    for (size_t i = scan_result_count - first; i < VISIBLE_RESULTS; i++)
        printf("\n");
}

static void print_scan_counts(void)
{
    printf("%d games scanned\n"
           "\x1b[32;1m%d new\x1b[37;1m / \x1b[36;1m%d existing\x1b[37;1m\n"
           "\x1b[33;1m%d missing\x1b[37;1m / \x1b[31;1m%d failed\x1b[37;1m\n",
           counters.found, counters.written, counters.skipped, counters.missed, counters.failed);
}

static void scan_dashboard(const char *name, const char *status)
{
    consoleClear();
    printf("\x1b[37;1mScanning...\n\n");
    print_scan_counts();
    printf("\n");

    if (name && *name) {
        printf("%.30s\n", name);
        if (strlen(name) > 30)
            printf("%.27s%s\n", name + 30, strlen(name) > 57 ? "..." : "");
        else
            printf("\n");
    } else {
        printf("Reading the card...\n\n");
    }

    printf(status ? "\x1b[36;1m%s\x1b[37;1m\n" : "\n", status);
    print_recent_results();
    printf("\nHold B to stop.");
}

static void scan_summary(bool stopped, size_t selected, size_t first, const char *debug_line)
{
    consoleClear();
    printf(stopped ? "\x1b[33;1mScan stopped.\x1b[37;1m\n\n"
                   : "\x1b[32;1mScan complete!\x1b[37;1m\n\n");
    print_scan_counts();
    printf("\n");

    if (scan_result_count == 0) {
        printf("No results.\n");
        for (int i = 1; i < VISIBLE_RESULTS; i++)
            printf("\n");
    } else {
        printf("Result %lu of %lu%s\n",
               (unsigned long)(selected + 1), (unsigned long)scan_result_count,
               scan_history_complete ? "" : " (partial)");
        size_t end = first + VISIBLE_RESULTS;
        if (end > scan_result_count)
            end = scan_result_count;
        for (size_t i = first; i < end; i++) {
            const ScanResult *entry = &scan_results[i];
            printf("%c%s%-4s \x1b[37;1m%.25s\n", i == selected ? '>' : ' ',
                   result_colour(entry->result), result_label(entry->result), entry->name);
        }
        for (size_t i = end - first; i < VISIBLE_RESULTS; i++)
            printf("\n");
    }

    printf("\n%s\n\n"
           " \x1b[32;1mA:\x1b[37;1m Run again\n"
           " \x1b[32;1mSTART:\x1b[37;1m Exit\n"
           " \x1b[32;1mSELECT:\x1b[37;1m CRC32\n", debug_line);
}

static void fetch_art(const char *path, const char *name)
{
    char out_path[512];
    snprintf(out_path, sizeof(out_path), BOXART_DIR "/%s.png", name);

    struct stat existing;
    if (!g_config.overwrite && stat(out_path, &existing) == 0) {
        counters.skipped++;
        remember_result(path, name, '=', false, 0);
        scan_dashboard(name, NULL);
        return;
    }

    char render[96];
    snprintf(render, sizeof(render), "&w=%d&h=%d&b=%s&bt=%d&bc=%s",
             SIZE_W[g_config.size], SIZE_H[g_config.size], BORDER_WIRE[g_config.border],
             g_config.thick ? 2 : 1, g_config.border == 4 ? "FFFFFFFF" : "FF000000");

    /* The file name plus the file's first bytes: everything the server needs to work out the
       console, the serial and the title entirely on its side. This client parses nothing - a
       shipped binary must not carry knowledge (header offsets, platform names) that can go
       stale. Static buffers: fetch_art is single-threaded and the DS stack is small. */
    static char encoded_name[512];
    url_encode(name, encoded_name, sizeof(encoded_name));

    static unsigned char header[HEADER_SAMPLE];
    static char header_b64[((HEADER_SAMPLE + 2) / 3) * 4 + 1];
    static char encoded_header[sizeof(header_b64) * 3];
    encoded_header[0] = '\0';

    FILE *f = fopen(path, "rb");
    if (f) {
        size_t got = fread(header, 1, sizeof(header), f);
        fclose(f);
        if (got > 0) {
            base64_encode(header, got, header_b64);
            url_encode(header_b64, encoded_header, sizeof(encoded_header));
        }
    }

    static char query[3072];
    if (encoded_header[0] != '\0') {
        snprintf(query, sizeof(query), "/v2/art.png?name=%s&header=%s%s",
                 encoded_name, encoded_header, render);
    } else {
        /* Unreadable file: the name alone still identifies most ROMs. */
        snprintf(query, sizeof(query), "/v2/art.png?name=%s%s", encoded_name, render);
    }

    scan_dashboard(name, NULL);
    int status = http_get_to_file(query, out_path);
    bool crc32_known = false;
    u32 crc32 = 0;
    if (status == 404 && !g_config.quick_scan && !is_ds_ext(file_ext(name))) {
        scan_dashboard(name, "Calculating CRC32...");
        bool cancelled;
        u32 size;
        if (file_crc32(path, &crc32, &size, &cancelled)) {
            crc32_known = true;
            size_t length = strlen(query);
            snprintf(query + length, sizeof(query) - length, "&crc32=%08lX&size=%lu",
                     (unsigned long)crc32, (unsigned long)size);
            scan_dashboard(name, "Retrying with CRC32...");
            status = http_get_to_file(query, out_path);
        } else if (cancelled) {
            aborted = true;
            return;
        } else {
            status = -1;
        }
    }

    if (status == 200) {
        counters.written++;
        remember_result(path, name, '+', crc32_known, crc32);
        scan_dashboard(name, NULL);
    } else if (status == 404) {
        counters.missed++;
        remember_result(path, name, '-', crc32_known, crc32);
        scan_dashboard(name, NULL);
    } else {
        counters.failed++;
        remember_result(path, name, '!', crc32_known, crc32);
        scan_dashboard(name, NULL);
    }
}

/* One path buffer shared by the whole walk: each level appends its segment before recursing and
   truncates it back after, so a recursion frame costs only its locals. A per-frame path buffer
   at MAX_DEPTH was ~4.5 KB of the small DS stack. */
static char scan_path[512];

static void scan_directory(int depth)
{
    if (depth > MAX_DEPTH || user_aborted())
        return;

    DIR *dir = opendir(depth == 0 ? "/" : scan_path);
    if (!dir)
        return;

    size_t base_len = strlen(scan_path);

    struct dirent *entry;
    while (!user_aborted() && (entry = readdir(dir)) != NULL) {
        if (entry->d_name[0] == '.')
            continue;

        if (base_len + 1 + strlen(entry->d_name) >= sizeof(scan_path))
            continue;

        scan_path[base_len] = '/';
        strcpy(scan_path + base_len + 1, entry->d_name);

        if (entry->d_type == DT_DIR) {
            /* Never descend into _nds: the boxart output lives there. */
            if (!(depth == 0 && strcasecmp(entry->d_name, "_nds") == 0))
                scan_directory(depth + 1);
        } else if (is_rom_ext(file_ext(entry->d_name))) {
            counters.found++;
            fetch_art(scan_path, entry->d_name);
        }

        scan_path[base_len] = '\0';
    }

    closedir(dir);
}

/* main */

/* Where the last join attempt got to, so failures can say what actually went wrong. */
static int g_last_assoc_status = -1;

/* Polls the association state, giving up after the deadline instead of hanging forever. Once
   the network lets us in, the wait stretches to a minute: DHCP retries back off slowly and a
   short window would abandon a join that was going to succeed.

   The wait MUST be cothread_yield_irq, never swiWaitForVBlank. dswifi runs lwIP on its own
   cothread, and swiWaitForVBlank halts the CPU without ever handing the scheduler control, so
   the stack never gets to answer the DHCP offer. Association still completes (the ARM7 does
   that on its own) and the status then sits on ACQUIRINGDHCP until the deadline, on every
   console and every emulator. Same rule anywhere else the app waits while the network is up. */
static bool wait_for_association(int seconds)
{
    int deadline = seconds * 60;
    bool in_dhcp = false;
    for (int frame = 0; frame < deadline; frame++) {
        cothread_yield_irq(IRQ_VBLANK);
        int status = Wifi_AssocStatus();
        g_last_assoc_status = status;
        if (status == ASSOCSTATUS_ACQUIRINGDHCP && !in_dhcp) {
            in_dhcp = true;
            printf("\n\x1b[36;1mgot in, asking for an IP\x1b[37;1m");
            if (deadline < 60 * 60)
                deadline = 60 * 60;
        }
        if (frame % 60 == 0)
            printf(".");
        if (status == ASSOCSTATUS_ASSOCIATED)
            return true;
        if (status == ASSOCSTATUS_CANNOTCONNECT)
            return false;
    }
    return false;
}

/* Edits the buffer in place on the on-screen keyboard. The keyboard covers the lower half of
   the screen, so the prompt and the echoed text always start from a cleared top. Whatever is
   already in the buffer is shown and editable, so a saved password only needs its typo fixed
   instead of a full retype. Enter accepts, B cancels and leaves the buffer untouched. */
static bool edit_line(const char *prompt, char *buf, size_t buf_size)
{
    consoleClear();
    printf("\x1b[37;1m%s\n> ", prompt);

    char work[128];
    if (buf_size > sizeof(work))
        buf_size = sizeof(work);
    size_t len = strlen(buf);
    if (len >= buf_size)
        len = buf_size - 1;
    memcpy(work, buf, len);
    printf("%.*s", (int)len, work);

    bgHide(sub_bitmap_bg);
    if (!kb_ready) {
        keyboardInit(NULL, 0, BgType_Text4bpp, BgSize_T_256x512, 26, 5, false, true);
        vram_copy(kb_palette, BG_PALETTE_SUB, sizeof(kb_palette));
        kb_ready = true;
    } else {
        vram_copy(BG_PALETTE_SUB, kb_palette, sizeof(kb_palette));
    }
    keyboardShow();
    bool accepted = false;
    while (1) {
        cothread_yield_irq(IRQ_VBLANK);
        scanKeys();
        if (keysDown() & KEY_B)
            break;
        int c = keyboardUpdate();
        if (c <= 0)
            continue;
        if (c == '\n') {
            accepted = true;
            break;
        }
        if (c == 8) {
            if (len > 0) {
                len--;
                printf("\b \b");
            }
        } else if (c >= 32 && c < 127 && len + 1 < buf_size) {
            work[len++] = (char)c;
            printf("%c", c);
        }
    }
    keyboardHide();
    vram_copy(BG_PALETTE_SUB, (const void *)bottomPal, sizeof(kb_palette));
    bgShow(sub_bitmap_bg);
    printf("\n");

    if (accepted) {
        memcpy(buf, work, len);
        buf[len] = '\0';
    }
    return accepted;
}


static bool load_config(AppConfig *config)
{
    memset(config, 0, sizeof(*config));
    config->backend_tls = -1; /* "not in the ini", resolved below */

    FILE *f = fopen(CONFIG_PATH, "r");
    if (f) {
        char line[192];
        while (fgets(line, sizeof(line), f)) {
            char *p = line;
            while (isspace((unsigned char)*p))
                p++;
            char *eq = strchr(p, '=');
            if (*p == ';' || *p == '#' || !eq)
                continue;
            *eq = '\0';

            char *key = p, *value = eq + 1;
            for (char *e = eq - 1; e >= key && isspace((unsigned char)*e); e--)
                *e = '\0';
            while (isspace((unsigned char)*value))
                value++;
            value[strcspn(value, "\r\n")] = '\0';

            if (strcasecmp(key, "ssid") == 0) {
                strncpy(config->ssid, value, sizeof(config->ssid) - 1);
            } else if (strcasecmp(key, "key") == 0) {
                strncpy(config->key, value, sizeof(config->key) - 1);
            } else if (strcasecmp(key, "size") == 0) {
                config->size = atoi(value);
                if (config->size < 0 || config->size > 2)
                    config->size = 0;
            } else if (strcasecmp(key, "border") == 0) {
                config->border = atoi(value);
                if (config->border < 0 || config->border > 4)
                    config->border = 0;
            } else if (strcasecmp(key, "thick") == 0) {
                config->thick = atoi(value) != 0;
            } else if (strcasecmp(key, "overwrite") == 0) {
                config->overwrite = atoi(value) != 0;
            } else if (strcasecmp(key, "quick_scan") == 0) {
                config->quick_scan = atoi(value) != 0;
            } else if (strcasecmp(key, "mute") == 0) {
                config->mute = atoi(value) != 0;
            } else if (strcasecmp(key, "backend_host") == 0) {
                strncpy(config->backend_host, value, sizeof(config->backend_host) - 1);
            } else if (strcasecmp(key, "backend_port") == 0) {
                config->backend_port = atoi(value);
            } else if (strcasecmp(key, "backend_tls") == 0) {
                config->backend_tls = atoi(value) != 0;
            }
        }

        fclose(f);
    }

    /* The ini overrides the backend only where it says something; everything absent falls
       back to the hosted service. Runs on the no-file path too, so a first run - and any
       older ini without these keys - lands on the defaults. */
    if (config->backend_host[0] == '\0')
        strncpy(config->backend_host, DEFAULT_BACKEND_HOST, sizeof(config->backend_host) - 1);
    if (config->backend_tls < 0)
        config->backend_tls = DEFAULT_BACKEND_TLS;
    if (config->backend_port <= 0 || config->backend_port > 65535)
        config->backend_port = config->backend_tls ? 443 : 8186;

    return config->ssid[0] != '\0';
}

static void save_config(const AppConfig *config)
{
    mkdir("/_nds", 0777);
    FILE *f = fopen(CONFIG_PATH, "w");
    if (!f)
        return;
    fprintf(f, "; TwilightBoxart\nssid = %s\nkey = %s\nsize = %d\nborder = %d\nthick = %d\noverwrite = %d\nquick_scan = %d\nmute = %d\n",
            config->ssid, config->key, config->size, config->border, config->thick ? 1 : 0,
            config->overwrite ? 1 : 0, config->quick_scan ? 1 : 0, config->mute ? 1 : 0);
    /* Written out even when untouched, so the keys are on the card to edit. Self-hosters:
       point backend_host at your own server; backend_tls 0 means plain HTTP. */
    fprintf(f, "; backend override - edit to use your own server\nbackend_host = %s\nbackend_port = %d\nbackend_tls = %d\n",
            config->backend_host, config->backend_port, config->backend_tls ? 1 : 0);
    fclose(f);
}

/* Forgets everything saved on the card and goes back to the built-in defaults.

   The ini is deleted rather than rewritten: load_config only falls back to a compiled default for
   keys the file does not mention, so a stale backend_host would otherwise outlive the reset. That
   is the case this exists for, a card left pointing at a server that is no longer there. */
static void reset_config(void)
{
    remove(CONFIG_PATH);
    load_config(&g_config);
    apply_mute();
}

/* Joins with the scanned AP record when there is one (its bssid makes the match exact), or by
   name alone otherwise; ConnectSecureAP runs its own scan and disconnect either way. The wait
   has to cover that scan plus ~5s of WPA2 key derivation, which happens on every attempt. */
static bool join_network(const Wifi_AccessPoint *scanned)
{
    Wifi_AccessPoint ap = { 0 };
    if (scanned) {
        ap = *scanned;
    } else {
        size_t ssid_len = strlen(g_config.ssid);
        memcpy(ap.ssid, g_config.ssid, ssid_len);
        ap.ssid_len = ssid_len;
    }
    size_t key_len = strlen(g_config.key);
    if (Wifi_ConnectSecureAP(&ap, key_len > 0 ? g_config.key : NULL, key_len) != 0)
        return false;
    return wait_for_association(30);
}

/* Some hotspots never finish DHCP (their replies cannot reach a client that has no address
   yet). Last resort while still associated: claim a static address on the well-known hotspot
   subnets and check whether the gateway's DNS answers. */
static bool static_ip_rescue(void)
{
    static const struct {
        u32 ip, gateway, mask;
        const char *label;
    } candidates[] = {
        { 0xAC140A0D, 0xAC140A01, 0xFFFFFFF0, "iPhone hotspot" },
        { 0xC0A82BD5, 0xC0A82B01, 0xFFFFFF00, "Android hotspot" },
    };

    for (unsigned i = 0; i < sizeof(candidates) / sizeof(candidates[0]); i++) {
        printf("\x1b[36;1mtrying %s..\x1b[37;1m ", candidates[i].label);
        Wifi_SetIP(candidates[i].ip, candidates[i].gateway, candidates[i].mask,
                   candidates[i].gateway, candidates[i].gateway);
        for (int frame = 0; frame < 120; frame++)
            cothread_yield_irq(IRQ_VBLANK);

        struct addrinfo hints = { 0 };
        hints.ai_family = AF_INET;
        hints.ai_socktype = SOCK_STREAM;
        struct addrinfo *resolved;
        if (getaddrinfo(g_config.backend_host, NULL, &hints, &resolved) == 0 && resolved != NULL) {
            freeaddrinfo(resolved);
            printf("\x1b[32;1mworks!\x1b[37;1m\n");
            return true;
        }
        printf("no\n");
    }

    Wifi_SetIP(0, 0, 0, 0, 0); /* back to DHCP for whatever gets tried next */
    return false;
}

/* After a failed attempt the radio firmware can emit a disconnect event for up to ~3 seconds,
   and one landing mid-retry kills that retry instantly. Disconnect, then sit the whole window
   out before the next try. */
static void settle_radio(void)
{
    Wifi_DisconnectAP();
    for (int i = 0; i < 180; i++)
        cothread_yield_irq(IRQ_VBLANK);
}

#define SCAN_SLOTS 10

/* Wifi_AccessPoint.ssid is a 33-byte field with a separate ssid_len and no promise of a NUL, so
   every string use of it goes through this bounded copy into a char[33]. */
static void copy_ssid(char *dst, const Wifi_AccessPoint *ap)
{
    size_t len = ap->ssid_len;
    if (len > 32)
        len = 32;
    memcpy(dst, ap->ssid, len);
    dst[len] = '\0';
}

/* rssi is dBm (negative) on DSi and 0-255 on DS; in both, bigger means stronger. */
static const char *signal_bars(int rssi)
{
    if (rssi >= (rssi < 0 ? -55 : 170))
        return "***";
    if (rssi >= (rssi < 0 ? -70 : 100))
        return "** ";
    return "*  ";
}

/* A few seconds in scan mode, keeping the strongest unique networks. Hidden names and WPA1
   networks are dropped; dswifi cannot join WPA1 so listing them would only disappoint. */
static int scan_networks(Wifi_AccessPoint *list)
{
    Wifi_ScanMode();
    printf("\x1b[36;1mLooking for networks");
    for (int frame = 0; frame < 4 * 60; frame++) {
        cothread_yield_irq(IRQ_VBLANK);
        if (frame % 60 == 59)
            printf(".");
    }
    printf("\x1b[37;1m\n");

    int count = 0;
    int total = Wifi_GetNumAP();
    for (int i = 0; i < total; i++) {
        Wifi_AccessPoint ap;
        if (Wifi_GetAPData(i, &ap) != WIFI_RETURN_OK)
            continue;
        if (ap.ssid_len == 0 || ap.security_type == AP_SECURITY_WPA)
            continue;

        char name[33];
        copy_ssid(name, &ap);

        bool seen = false;
        for (int j = 0; j < count; j++) {
            char other[33];
            copy_ssid(other, &list[j]);
            if (strcmp(other, name) == 0) {
                if (ap.rssi > list[j].rssi)
                    list[j] = ap;
                seen = true;
                break;
            }
        }
        if (seen)
            continue;

        if (count < SCAN_SLOTS) {
            list[count++] = ap;
        } else {
            int weakest = 0;
            for (int j = 1; j < count; j++) {
                if (list[j].rssi < list[weakest].rssi)
                    weakest = j;
            }
            if (ap.rssi > list[weakest].rssi)
                list[weakest] = ap;
        }
    }

    /* strongest first */
    for (int i = 1; i < count; i++) {
        Wifi_AccessPoint ap = list[i];
        int j = i;
        while (j > 0 && list[j - 1].rssi < ap.rssi) {
            list[j] = list[j - 1];
            j--;
        }
        list[j] = ap;
    }
    return count;
}

/* Returns 0 with *chosen filled in, 1 for typing a name by hand, -1 to give up. */
static int pick_network(Wifi_AccessPoint *chosen)
{
    static Wifi_AccessPoint list[SCAN_SLOTS];

    while (1) {
        consoleClear();
        printf("\x1b[37;1mLet's get you online.\n\n");
        int count = scan_networks(list);

        if (count == 0) {
            printf("\nNo networks found. WiFi off,\nor 5 GHz only? The DSi needs\na 2.4 GHz network.\n\n");
            printf("\x1b[32;1mY:\x1b[37;1m scan again  \x1b[32;1mX:\x1b[37;1m type a name\n\x1b[31;1mSTART:\x1b[37;1m give up\n");
            while (1) {
                cothread_yield_irq(IRQ_VBLANK);
                scanKeys();
                u32 down = keysDown();
                if (down & KEY_Y)
                    break;
                if (down & KEY_X)
                    return 1;
                if (down & KEY_START)
                    return -1;
            }
            continue;
        }

        int row = 0;
        for (int i = 0; i < count; i++) {
            char name[33];
            copy_ssid(name, &list[i]);
            if (strcmp(name, g_config.ssid) == 0)
                row = i;
        }

        while (1) {
            consoleClear();
            printf("\x1b[37;1mPick your WiFi network:\n\n");
            for (int i = 0; i < count; i++) {
                char name[33];
                copy_ssid(name, &list[i]);
                const char *tag = list[i].security_type == AP_SECURITY_OPEN ? "open" : signal_bars(list[i].rssi);
                printf("%s %c %-22.22s %s\n", i == row ? "\x1b[33;1m" : "\x1b[37;1m",
                       i == row ? '>' : ' ', name, tag);
            }
            printf("\x1b[30;1m\nUP/DOWN + A, or just tap it.\n\n");
            printf("\x1b[32;1mY:\x1b[30;1m rescan  \x1b[32;1mX:\x1b[30;1m type a name\n\x1b[31;1mSTART:\x1b[30;1m give up\x1b[37;1m\n");

            bool rescan = false;
            while (1) {
                cothread_yield_irq(IRQ_VBLANK);
                scanKeys();
                u32 down = keysDown();
                if (down & KEY_UP) {
                    row = (row + count - 1) % count;
                    break;
                }
                if (down & KEY_DOWN) {
                    row = (row + 1) % count;
                    break;
                }
                if (down & KEY_A) {
                    *chosen = list[row];
                    return 0;
                }
                if (down & KEY_TOUCH) {
                    touchPosition touch;
                    touchRead(&touch);
                    int hit = touch.py / 8 - 2;
                    if (hit >= 0 && hit < count) {
                        *chosen = list[hit];
                        return 0;
                    }
                }
                if (down & KEY_Y) {
                    rescan = true;
                    break;
                }
                if (down & KEY_X)
                    return 1;
                if (down & KEY_START)
                    return -1;
            }
            if (rescan)
                break;
        }
    }
}

/* The saved network first, then the console's stored connections, then a scan-and-pick setup
   that only asks for what it really needs: usually one tap and, at most, one password. */
static bool connect_wifi(void)
{
    /* WIFI_ATTEMPT_DSI_MODE is what actually powers the DSi's own radio (and WPA2 with it);
       the library's default is DS mode wifi even on DSi consoles, which stalls in a TWL boot. */
    if (!Wifi_InitDefault(INIT_ONLY | WIFI_ATTEMPT_DSI_MODE))
        return false;
    printf(" connecting");

    /* Two shots at the saved network; the radio needs settling between them or the retry
       fails instantly off the previous attempt's latched state. */
    if (g_config.ssid[0] != '\0') {
        printf(" to %s", g_config.ssid);
        for (int attempt = 0; attempt < 2; attempt++) {
            if (join_network(NULL))
                return true;
            if (g_last_assoc_status == ASSOCSTATUS_ACQUIRINGDHCP) {
                printf("\n\x1b[33;1mNo IP address handed out.\x1b[37;1m\n");
                if (static_ip_rescue())
                    return true;
            }
            settle_radio();
        }
    }

    Wifi_AutoConnect();
    if (wait_for_association(12))
        return true;
    settle_radio();

    while (1) {
        Wifi_AccessPoint picked;
        int choice = pick_network(&picked);
        if (choice == -1)
            return false;

        /* A connect issued while the picker's scan is still in flight gets eaten by the radio
           firmware (an unfixed library race), so drop to idle and let the scan drain first. */
        Wifi_IdleMode();
        for (int i = 0; i < 30; i++)
            cothread_yield_irq(IRQ_VBLANK);

        bool needs_key = true;
        if (choice == 1) {
            /* hand-typed name, pre-filled with the saved one; a changed name drops the old key */
            char previous[sizeof(g_config.ssid)];
            strcpy(previous, g_config.ssid);
            if (!edit_line("Network name:", g_config.ssid, sizeof(g_config.ssid)) || g_config.ssid[0] == '\0')
                continue;
            if (strcmp(previous, g_config.ssid) != 0)
                g_config.key[0] = '\0';
        } else {
            char picked_ssid[33];
            copy_ssid(picked_ssid, &picked);
            if (strcmp(g_config.ssid, picked_ssid) != 0) {
                strcpy(g_config.ssid, picked_ssid);
                g_config.key[0] = '\0';
            }
            if (picked.security_type == AP_SECURITY_OPEN) {
                needs_key = false;
                g_config.key[0] = '\0';
            }
        }

        bool retry = false;
        while (1) {
            if (needs_key) {
                char prompt[96];
                snprintf(prompt, sizeof(prompt), "%sPassword for %s\n(blank if open):",
                         retry ? "That didn't work.\n\n" : "", g_config.ssid);
                if (!edit_line(prompt, g_config.key, sizeof(g_config.key)))
                    break; /* B goes back to the network list */
            }

            /* two attempts per password entry: transient firmware disconnects kill single
               attempts often enough that retrying silently first beats asking again */
            bool joined = false;
            for (int attempt = 0; attempt < 2 && !joined; attempt++) {
                if (attempt > 0)
                    settle_radio();
                printf("\nJoining %s", g_config.ssid);
                joined = join_network(choice == 0 ? &picked : NULL);
            }
            if (joined) {
                save_config(&g_config);
                printf("\n\x1b[32;1mConnected and saved: next\ntime this is automatic.\x1b[37;1m\n");
                return true;
            }

            if (g_last_assoc_status == ASSOCSTATUS_ACQUIRINGDHCP) {
                /* the password was fine; the router never handed out an address */
                printf("\n\x1b[33;1mNo IP address handed out.\x1b[37;1m\n");
                if (static_ip_rescue()) {
                    save_config(&g_config);
                    printf("\n\x1b[32;1mConnected and saved: next\ntime this is automatic.\x1b[37;1m\n");
                    return true;
                }
                settle_radio();
                printf("\nThe network let us in but\nnever gave an IP address,\nand the usual hotspot\naddresses came up empty.\nTry another network.\n\n"
                       "\x1b[32;1mA:\x1b[37;1m back to the list\n");
                while (1) {
                    cothread_yield_irq(IRQ_VBLANK);
                    scanKeys();
                    if (keysDown() & KEY_A)
                        break;
                }
                break;
            }
            settle_radio();

            if (!needs_key) {
                printf("\nCouldn't join that one.\n");
                break;
            }
            retry = true;
        }
    }
}

/* options */

/* A little settings screen: pick with the D-pad, A starts the scan. */
static bool options_menu(void)
{
    int row = 0;
    while (1) {
        consoleClear();
        printf("\x1b[37;1mWelcome to TwilightBoxart!\n\n");
        printf("How do you want your covers?\n\n");

        const char *values[5] = {
            SIZE_NAMES[g_config.size],
            BORDER_NAMES[g_config.border],
            g_config.thick ? "On" : "Off",
            g_config.overwrite ? "Yes" : "No",
            g_config.quick_scan ? "Quick" : "Complete",
        };
        const char *labels[5] = { "Size", "Border", "Thicker border", "Overwrite existing", "Scan mode" };

        for (int i = 0; i < 5; i++) {
            bool dim = i == 2 && g_config.border == 0;
            if (i == row)
                printf(" \x1b[33;1m> %-18s %s\n", labels[i], values[i]);
            else if (dim)
                printf(" \x1b[30;1m  %-18s %s\n", labels[i], values[i]);
            else
                printf(" \x1b[37;1m  %-18s %s\n", labels[i], values[i]);
        }

        printf(g_config.quick_scan
               ? "\x1b[30;1mQuick may miss some covers.\x1b[37;1m\n"
               : "\n");
        printf("\x1b[37;1m\n \x1b[32;1mA:\x1b[37;1m scan and download\n"
               " \x1b[32;1mX:\x1b[37;1m music %s\n"
               " \x1b[32;1mY:\x1b[37;1m reset settings\n"
               " \x1b[32;1mSTART:\x1b[37;1m quit\n", g_config.mute ? "off" : "on");

        while (1) {
            cothread_yield_irq(IRQ_VBLANK);
            scanKeys();
            u32 down = keysDownRepeat();
            if (down & KEY_UP) {
                row = (row + 4) % 5;
                break;
            }
            if (down & KEY_DOWN) {
                row = (row + 1) % 5;
                break;
            }
            if (down & (KEY_LEFT | KEY_RIGHT)) {
                int step = (down & KEY_RIGHT) ? 1 : -1;
                if (row == 0)
                    g_config.size = (g_config.size + step + 3) % 3;
                else if (row == 1)
                    g_config.border = (g_config.border + step + 5) % 5;
                else if (row == 2)
                    g_config.thick = !g_config.thick;
                else if (row == 3)
                    g_config.overwrite = !g_config.overwrite;
                else
                    g_config.quick_scan = !g_config.quick_scan;
                break;
            }
            if (down & KEY_A) {
                consoleClear();
                return true;
            }
            if (down & KEY_X) {
                g_config.mute = !g_config.mute;
                apply_mute();
                break;
            }
            if (down & KEY_Y) {
                /* Worth a confirmation: the WiFi password goes with it, and it is one button
                   away from the one that starts a scan. */
                consoleClear();
                printf("\x1b[37;1mReset settings?\n\n"
                       "Forgets the WiFi network and\n"
                       "password, the box art choices,\n"
                       "and any backend the card was\n"
                       "pointed at.\n\n"
                       " \x1b[32;1mA:\x1b[37;1m reset\n"
                       " \x1b[32;1mB:\x1b[37;1m keep them\n");
                bool reset = false;
                while (1) {
                    cothread_yield_irq(IRQ_VBLANK);
                    scanKeys();
                    u32 answer = keysDown();
                    if (answer & KEY_A) {
                        reset = true;
                        break;
                    }
                    if (answer & KEY_B)
                        break;
                }

                if (reset) {
                    reset_config();
                    row = 0;
                    printf("\n\x1b[32;1mSettings reset.\x1b[37;1m\n");
                }

                /* Wait for the button to come back up. The menu below polls with key repeat, so
                   an A still held on the way out of here reads as "scan and download" and a run
                   starts the instant the reset finishes. */
                while (keysHeld() & (KEY_A | KEY_B)) {
                    cothread_yield_irq(IRQ_VBLANK);
                    scanKeys();
                }

                /* Long enough to read the confirmation before the menu paints over it. */
                if (reset) {
                    for (int frame = 0; frame < 60; frame++)
                        cothread_yield_irq(IRQ_VBLANK);
                }
                break;
            }
            if (down & KEY_START)
                return false;
            if (down & KEY_TOUCH) {
                touchPosition touch;
                touchRead(&touch);
                /* The five option rows sit on console lines 5 to 9, the scan line on 12. */
                int line = touch.py / 8;
                if (line >= 5 && line <= 9) {
                    int tapped = line - 5;
                    if (tapped == row) {
                        if (row == 0)
                            g_config.size = (g_config.size + 1) % 3;
                        else if (row == 1)
                            g_config.border = (g_config.border + 1) % 5;
                        else if (row == 2)
                            g_config.thick = !g_config.thick;
                        else if (row == 3)
                            g_config.overwrite = !g_config.overwrite;
                        else
                            g_config.quick_scan = !g_config.quick_scan;
                    }
                    row = tapped;
                    break;
                }
                if (line >= 11 && line <= 13) {
                    consoleClear();
                    return true;
                }
            }
        }
    }
}

/* eye candy */

/* Two 8x8 4bpp frames: a bright four-point star and a faint dot. */
static const u8 STAR_BRIGHT[32] = {
    0x00, 0x10, 0x00, 0x00,
    0x00, 0x10, 0x00, 0x00,
    0x10, 0x11, 0x01, 0x00,
    0x11, 0x11, 0x11, 0x00,
    0x10, 0x11, 0x01, 0x00,
    0x00, 0x10, 0x00, 0x00,
    0x00, 0x10, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
};
static const u8 STAR_DIM[32] = {
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x10, 0x00, 0x00,
    0x00, 0x11, 0x01, 0x00,
    0x00, 0x10, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00,
};

static const struct { int x, y, phase; } STARS[] = {
    { 86, 56, 0 }, { 121, 50, 23 }, { 158, 58, 41 }, { 176, 82, 11 }, { 76, 84, 33 }, { 141, 60, 50 },
};
#define STAR_COUNT (sizeof(STARS) / sizeof(STARS[0]))

static u16 *star_bright_gfx, *star_dim_gfx;

static void twinkle_vblank(void)
{
    static int frame;
    frame++;
    for (unsigned i = 0; i < STAR_COUNT; i++) {
        bool bright = ((frame + STARS[i].phase) / 40) & 1;
        oamSet(&oamMain, i, STARS[i].x, STARS[i].y, 0, 0,
               SpriteSize_8x8, SpriteColorFormat_16Color,
               bright ? star_bright_gfx : star_dim_gfx, -1, false, false, false, false, false);
    }
    oamUpdate(&oamMain);
}

static void init_twinkle(void)
{
    vramSetBankB(VRAM_B_MAIN_SPRITE);
    oamInit(&oamMain, SpriteMapping_1D_32, false);
    star_bright_gfx = oamAllocateGfx(&oamMain, SpriteSize_8x8, SpriteColorFormat_16Color);
    star_dim_gfx = oamAllocateGfx(&oamMain, SpriteSize_8x8, SpriteColorFormat_16Color);
    vram_copy(star_bright_gfx, STAR_BRIGHT, sizeof(STAR_BRIGHT));
    vram_copy(star_dim_gfx, STAR_DIM, sizeof(STAR_DIM));
    SPRITE_PALETTE[1] = RGB15(31, 31, 28);
    irqSet(IRQ_VBLANK, twinkle_vblank);
}

/* Draws a string into the top screen's 16-bit bitmap, x,y being the pen with y on the baseline.
   The version has to sit opposite the credit that is part of graphics/logo.png, so it is drawn
   from credit_font.h rather than the console's own face: that one is 8x8 and 1bpp, and next to
   the credit it looks like a different program wrote it.

   Coverage is blended rather than thresholded. At a seven pixel cap height forcing every pixel
   fully on or off destroys the narrow lowercase ('x' and 'a' stop being readable at all), and
   the credit this sits next to is antialiased anyway, being part of the image.

   The pen runs in sixteenths of a pixel and only rounds when a glyph is placed. Helvetica's
   advances here are fractional ('i' is 2.25px, 'T' is 6.125px), and rounding each one on its own
   leaves the error in the gap after that letter, which is what makes spacing look uneven. */
/* Advance width of a string in sixteenths of a pixel, so a label can be right aligned. */
static int credit_text_width(const char *text)
{
    int width = 0;

    for (; *text != '\0'; text++) {
        int index = (unsigned char)*text - CREDIT_FONT_FIRST;
        if (index >= 0 && index < CREDIT_FONT_COUNT)
            width += credit_font[index].advance;
    }

    return width;
}

static void draw_credit_text(int x, int y, int r, int g, int b, const char *text)
{
    u16 *canvas = bgGetGfxPtr(top_bg);
    int pen = x * CREDIT_FONT_SUB;

    for (; *text != '\0'; text++) {
        int index = (unsigned char)*text - CREDIT_FONT_FIRST;
        if (index < 0 || index >= CREDIT_FONT_COUNT)
            continue;

        const CreditGlyph *glyph = &credit_font[index];
        const u8 *coverage = credit_font_alpha + glyph->offset;
        int origin = (pen + CREDIT_FONT_SUB / 2) / CREDIT_FONT_SUB;

        for (int row = 0; row < glyph->h; row++) {
            int py = y - CREDIT_FONT_BASELINE + glyph->yoff + row;
            if (py < 0 || py >= SCREEN_HEIGHT)
                continue;

            for (int col = 0; col < glyph->w; col++) {
                int px = origin + glyph->xoff + col;
                int alpha = coverage[row * glyph->w + col];
                if (px < 0 || px >= SCREEN_WIDTH || alpha == 0)
                    continue;

                /* 255 nudged to 256, so a fully covered pixel lands on the colour itself
                   instead of a step short of it, which reads as a washed out label. */
                int weight = alpha + (alpha >> 7);

                u16 *pixel = &canvas[py * SCREEN_WIDTH + px];
                int dr = *pixel & 31, dg = (*pixel >> 5) & 31, db = (*pixel >> 10) & 31;
                dr += ((r - dr) * weight) >> 8;
                dg += ((g - dg) * weight) >> 8;
                db += ((b - db) * weight) >> 8;
                *pixel = ARGB16(1, dr, dg, db);
            }
        }

        pen += glyph->advance;
    }
}

/* The logo fills the top screen; the console gets the bottom one. */
static void init_screens(void)
{
    lcdMainOnTop();
    videoSetMode(MODE_5_2D);
    vramSetBankA(VRAM_A_MAIN_BG);
    top_bg = bgInit(3, BgType_Bmp16, BgSize_B16_256x256, 0, 0);
    vram_copy(bgGetGfxPtr(top_bg), logoBitmap, logoBitmapLen);

    /* Both bottom corners are drawn here rather than painted into logo.png: the version so a bump
       never means re-exporting artwork, and the credit so it is the same face as the version
       opposite it. \xA9 is written as a byte because the font is indexed by codepoint and this
       source is UTF-8, where the copyright sign would otherwise arrive as two of them. */
    static const char credit[] = "\xA9 KirovAir 2026";
    /* Rounded, not rounded up: the width lands on a fraction of a pixel and taking the ceiling
       pushes the whole label a pixel further from the edge than the version is from its own. */
    int credit_x = SCREEN_WIDTH - 4
                   - (credit_text_width(credit) + CREDIT_FONT_SUB / 2) / CREDIT_FONT_SUB;

    draw_credit_text(4, 189, 17, 17, 24, "TwilightBoxart " APP_VERSION);
    draw_credit_text(credit_x, 189, 17, 17, 24, credit);

    /* Bottom screen: the twilight artwork as an 8-bit bitmap, with the console's text layer
       drawn transparently over it. VRAM C budget: bitmap 0..64K, console tiles at 64K with its
       map at 72K, keyboard map at 80K and tiles at 96K. The bitmap palette is capped at 240
       colours so line 15 stays the console font's. */
    videoSetModeSub(MODE_5_2D);
    vramSetBankC(VRAM_C_SUB_BG);
    // Map bases are 5-bit (0-31, 2KB units). The bitmap below spans 0-64KB but only rows
    // 0-191 are shown, so the console map (48KB) and keyboard map sit in its hidden tail.
    consoleInit(NULL, 1, BgType_Text4bpp, BgSize_T_256x256, 24, 4, false, true);

    /* The bitmap sits one 16K slot up: something low in VRAM C picks up stray writes, and up
       here they land outside the visible artwork. */
    sub_bitmap_bg = bgInitSub(3, BgType_Bmp8, BgSize_B8_256x256, 0, 0);
    vram_copy(bgGetGfxPtr(sub_bitmap_bg), bottomBitmap, bottomBitmapLen);
    vram_copy(BG_PALETTE_SUB, bottomPal, bottomPalLen);

    init_twinkle();
}

int main(void)
{
    init_screens();
    soundEnable();
    music_channel = soundPlaySample(music_bin, SoundFormat_8Bit, music_bin_size, MUSIC_RATE,
                                    MUSIC_VOLUME, 64, true, 0);

    printf("TwilightBoxart " APP_VERSION "\n");
    printf("Box art for TWiLightMenu++\n\n");

    /* DS mode means the 2005 radio: no WPA2, no modern network. Refuse early and say how to
       fix it rather than letting WiFi fail in confusing ways later. */
    if (!isDSiMode()) {
        printf("This app needs DSi mode.\n\n"
               "In TWiLightMenu, press Y on\n"
               "TwilightBoxart and set\n"
               "Run in: DSi mode.\n");
        wait_for_start();
        return 1;
    }

    /* The DSi radio sits behind SCFG-gated hardware. Launched with the gates locked it can
       never power up and WiFi init would wait on it forever, so check the ARM9's own SCFG
       window first: it reads as zero when the launcher kept them shut. */
    if (REG_SCFG_EXT == 0) {
        printf("DSi mode, but without full\nhardware access, so WiFi\ncannot start.\n\n"
               "Try launching this app from\nthe Unlaunch menu itself, or\n"
               "look for an SCFG option in\nTWiLightMenu's per-game\nsettings (press Y).\n");
        wait_for_start();
        return 1;
    }

    if (!fatInitDefault()) {
        printf("No SD card / FAT found.\n\n"
               "The card is both the input and\n"
               "the output here: games are read\n"
               "from it, art is written to it.\n");
        wait_for_start();
        return 1;
    }

    load_config(&g_config);
    apply_mute();
    if (!options_menu())
        return 0;
    save_config(&g_config);

    printf("Getting your box art ready...\n\n");
    printf("Getting WiFi ready..");
    if (!connect_wifi()) {
        printf("\n\nNo WiFi connection.\n\n"
               "Check the console's WiFi\nsettings and try again.\n");
        wait_for_start();
        return 1;
    }
    printf(" connected.\n");

    if (g_config.backend_tls) {
        printf(" securing the line..");
        if (!tls_global_init(g_config.backend_host)) {
            printf("\n\nCould not set up HTTPS.\n");
            wait_for_start();
            return 1;
        }
        printf(" ok.\n");
    }
    printf("\n");

    /* EEXIST is fine: the folders are usually already there. */
    (void)mkdir("/_nds", 0777);
    (void)mkdir("/_nds/TWiLightMenu", 0777);
    (void)mkdir(BOXART_DIR, 0777);

    /* Before walking the card, ask what counts as a ROM. Silent and optional - see is_rom_ext. */
    fetch_rom_extensions();

    /* Stopping a scan is usually "wrong size" or "wrong border", not "I am done", so the end of a
       run goes back to the settings rather than straight out of the program. */
    for (;;) {
        memset(&counters, 0, sizeof(counters));
        reset_scan_results();
        aborted = false;

        scan_dashboard(NULL, NULL);
        scan_directory(0);

        size_t selected = scan_result_count > 0 ? scan_result_count - 1 : 0;
        size_t first = scan_result_count > VISIBLE_RESULTS ? scan_result_count - VISIBLE_RESULTS : 0;
        char debug_line[32] = "";
        bool again = false;
        scan_summary(aborted, selected, first, debug_line);
        while (1) {
            cothread_yield_irq(IRQ_VBLANK);
            scanKeys();
            /* Only the browse keys repeat. A short scan can end while the A that started it is
               still held, and repeat would read that held A as another "run again". */
            u32 down = keysDownRepeat();
            u32 pressed = keysDown();
            if (pressed & KEY_A) {
                again = true;
                break;
            }
            if (pressed & KEY_START)
                break;
            if (scan_result_count == 0)
                continue;

            if (pressed & KEY_SELECT) {
                ScanResult *entry = &scan_results[selected];
                if (!entry->crc32_known) {
                    strcpy(debug_line, "Calculating CRC32... B: Cancel");
                    scan_summary(aborted, selected, first, debug_line);
                    bool cancelled;
                    u32 size;
                    if (file_crc32(entry->path, &entry->crc32, &size, &cancelled)) {
                        entry->crc32_known = true;
                    } else {
                        strcpy(debug_line, cancelled ? "CRC32 cancelled." : "CRC32 unavailable.");
                    }
                }
                if (entry->crc32_known)
                    snprintf(debug_line, sizeof(debug_line), "CRC32: %08lX", (unsigned long)entry->crc32);
                scan_summary(aborted, selected, first, debug_line);
                continue;
            }

            bool moved = false;
            if ((down & KEY_UP) && selected > 0) {
                selected--;
                if (selected < first)
                    first = selected;
                moved = true;
            }
            if ((down & KEY_DOWN) && selected + 1 < scan_result_count) {
                selected++;
                if (selected >= first + VISIBLE_RESULTS)
                    first = selected - VISIBLE_RESULTS + 1;
                moved = true;
            }
            if (down & KEY_LEFT) {
                size_t step = selected < VISIBLE_RESULTS ? selected : VISIBLE_RESULTS;
                selected -= step;
                first = selected < VISIBLE_RESULTS ? 0 : selected - VISIBLE_RESULTS + 1;
                moved = step > 0;
            }
            if (down & KEY_RIGHT) {
                size_t remaining = scan_result_count - selected - 1;
                size_t step = remaining < VISIBLE_RESULTS ? remaining : VISIBLE_RESULTS;
                selected += step;
                first = selected < VISIBLE_RESULTS ? 0 : selected - VISIBLE_RESULTS + 1;
                moved = step > 0;
            }
            if (moved) {
                debug_line[0] = '\0';
                scan_summary(aborted, selected, first, debug_line);
            }
        }
        if (!again)
            break;

        while (keysHeld() & KEY_A) {
            cothread_yield_irq(IRQ_VBLANK);
            scanKeys();
        }

        if (!options_menu())
            break;
        save_config(&g_config);
    }

    return 0;
}
