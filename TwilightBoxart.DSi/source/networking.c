// networking.c: the HTTP transport for the backend. Requests ride a kept-alive connection: the DS
// pays dearly for connection setup (a DNS lookup, a TCP handshake and a TLS handshake on a slow
// radio), so one connection serves the whole scan and is rebuilt only when it breaks. TLS is
// delegated to tls.c, which shares this socket. Split out of main.c so the socket plumbing - and
// the timeouts that keep a flaky link from hanging the scan - live in one place. A response body
// lands wherever the caller pointed it - the card for covers, a buffer for the small text
// answers - through this one transport; see body_sink.
//
// The reuse is a shortcut in front of the old one-connection-per-request code, not a replacement
// for it: a request that finds no live connection behaves exactly as before, and a request whose
// REUSED connection fails in any way closes it and repeats once on a fresh one. A fresh
// connection's failure is final, as it always was, so the worst case is the old behaviour and the
// best case skips the whole setup cost.

#include <errno.h>
#include <limits.h>
#include <fcntl.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <time.h>

#include <nds.h>
#include <dswifi9.h>
#include <netdb.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/time.h>

#include "networking.h"
#include "tls.h"

/* dswifi-patched/lib/libdswifi9.a was built against BlocksDS 1.22.1, whose lwIP fcntl ABI uses
   O_NONBLOCK == 1. BlocksDS 1.22.2 changed the public header to picolibc's 0x4000 without changing
   this vendored archive: fcntl(F_SETFL) then rejects the flag, and every connection fails before
   connect() is called. Fail the build instead of ever shipping that mismatched combination again.
   When the archive is deliberately rebuilt for a newer SDK, update this guard with it. */
#if O_NONBLOCK != 1
#error "dswifi-patched ABI mismatch: build with the pinned BlocksDS 1.22.1 image or rebuild the archive"
#endif

/* Sent on every /v2 request; the server answers 401 without it. Not a secret - it is compiled into this
   binary and published in the repository - it only marks the request as coming from a real client
   rather than from a scraper pointed at the art routes. Keep in step with ApiKey.cs. */
#define API_KEY_HEADER "X-Twilight-Key: tb2_9f4c1d7a3e8b5062"

/* How long we will wait to establish a connection, and how long any single recv/send may stall before
   the request is abandoned. Small: a DS on WiFi that has gone quiet for this long is not coming back for
   this transfer, and hanging the whole scan on it is worse than a miss the next pass will retry. The
   modern reference (Kekatsu-DS) uses a 7 s connect timeout via libcurl; this is the hand-rolled
   equivalent. */
#define CONNECT_TIMEOUT_SECS 8
#define IO_TIMEOUT_SECS      10

/* Home routers kill idle TCP flows without a word - cheap NATs after half a minute - and the only way
   to find out is a send into the void and a full recv timeout. During a scan requests are back to back
   and never get near this; a connection that HAS sat idle this long (the user in a menu) is assumed
   dead and rebuilt up front, which costs a handshake instead of a 10 s stall. */
#define IDLE_REUSE_LIMIT_SECS 30

/* Distinct from any HTTP status: the request failed in a way that deserves one retry on a fresh
   connection IF it was riding a reused one. Never escapes http_get(). */
#define RC_RETRY (-2)

/* This many kept connections dying in a row, with not one surviving to serve a second request, means
   something on the path - a middlebox, a hostile proxy - kills every idle connection. Stop offering
   it kept connections: from then on every request buys a fresh one, which is exactly the old
   one-per-request behaviour, and the whole experiment cost three wasted probes. */
#define STALE_STREAK_LIMIT 3

static char s_host[128];
static int s_port;
static bool s_tls;

/* The kept-alive connection, -1 when there is none, plus when it last did useful work. */
static int s_sock = -1;
static time_t s_last_used;

/* Consecutive reused connections that died without serving; see STALE_STREAK_LIMIT. */
static int s_stale_streak;

/* One DNS answer per scan instead of one per cover; dropped after a connect failure so a server that
   moved gets looked up afresh rather than pinning the scan to a dead address. */
static struct in_addr s_cached_addr;
static bool s_have_addr;

static void net_disconnect(void);

void net_configure(const char *host, int port, bool tls)
{
    /* A connection to the OLD host must not survive a reconfigure. Called once in practice, but the
       kept-alive socket makes this the one place that has to care. */
    net_disconnect();
    s_have_addr = false;

    strncpy(s_host, host, sizeof(s_host) - 1);
    s_host[sizeof(s_host) - 1] = '\0';
    s_port = port;
    s_tls = tls;
}

/* The plain or TLS pipe over s_sock, whichever this run is configured for. */
static int xfer_send(const void *buf, size_t len)
{
    if (s_tls)
        return tls_send(buf, len);
    return send(s_sock, buf, len, 0);
}

static int xfer_recv(void *buf, size_t len)
{
    if (s_tls)
        return tls_recv(buf, len);
    return recv(s_sock, buf, len, 0);
}

/* Sentinel from parse_content_length: the header was present but not a clean number. A length that
   cannot be trusted must not frame a body, and a body that cannot be framed must not share a
   connection with the next one. */
#define CL_MALFORMED (-2)

/* Case-insensitive Content-Length lookup in a header block. -1 when absent, CL_MALFORMED when
   present but not strictly digits (atol would happily read "12junk", or overflow into garbage on a
   hostile value, and this number frames the body). */
static long parse_content_length(const char *headers)
{
    for (const char *line = headers; line; line = strchr(line, '\n'), line = line ? line + 1 : NULL) {
        if (strncasecmp(line, "Content-Length:", 15) != 0)
            continue;

        const char *p = line + 15;
        while (*p == ' ' || *p == '\t')
            p++;
        if (*p < '0' || *p > '9')
            return CL_MALFORMED;

        long value = 0;
        while (*p >= '0' && *p <= '9') {
            if (value > (LONG_MAX - 9) / 10)
                return CL_MALFORMED;
            value = value * 10 + (*p++ - '0');
        }
        while (*p == ' ' || *p == '\t')
            p++;
        return (*p == '\r' || *p == '\n') ? value : CL_MALFORMED;
    }
    return -1;
}

/* Whether the named header's value carries the token, as a whole word in a comma list: matches
   "Connection: close" and "Connection: keep-alive, close", not "closed". */
static bool header_has_token(const char *headers, const char *name, size_t name_length, const char *token)
{
    size_t token_length = strlen(token);
    for (const char *line = headers; line; line = strchr(line, '\n'), line = line ? line + 1 : NULL) {
        if (strncasecmp(line, name, name_length) != 0)
            continue;
        for (const char *p = line + name_length; *p && *p != '\r' && *p != '\n'; p++) {
            if (strncasecmp(p, token, token_length) != 0)
                continue;
            char end = p[token_length];
            if (end == '\0' || end == '\r' || end == '\n' || end == ' ' || end == '\t' || end == ',' || end == ';')
                return true;
        }
    }
    return false;
}

/* The body's byte source: first whatever arrived in the header read, then the socket. Exists so the
   Content-Length and chunked paths read bytes the same way, and neither has to remember where the
   header buffer ended. */
struct body_source {
    const char *pending;
    long pending_left;
};

static int body_read(struct body_source *src, char *dst, size_t want)
{
    if (src->pending_left > 0) {
        size_t take = (long)want < src->pending_left ? want : (size_t)src->pending_left;
        memcpy(dst, src->pending, take);
        src->pending += take;
        src->pending_left -= (long)take;
        return (int)take;
    }
    return xfer_recv(dst, want);
}

/* One line of chunked framing (a size line, a trailer line, or the CRLF after a chunk), read byte by
   byte because the next chunk's payload must stay on the wire. Cheap in practice: framing lines are a
   handful of bytes, and under TLS a one-byte read is a memcpy from mbedtls's record buffer. Returns
   the line's length without the CRLF, or -1 on error/overflow. */
static int read_framing_line(struct body_source *src, char *line, size_t size)
{
    size_t length = 0;
    for (;;) {
        char c;
        if (body_read(src, &c, 1) != 1)
            return -1;
        if (c == '\n')
            break;
        if (c != '\r') {
            if (length >= size - 1)
                return -1;
            line[length++] = c;
        }
    }
    line[length] = '\0';
    return (int)length;
}

/* Strict lower/upper hex chunk size, overflow-guarded, extensions (";...") ignored. -1 on garbage. */
static long parse_chunk_size(const char *line)
{
    long value = 0;
    const char *p = line;
    for (;; p++) {
        int digit;
        if (*p >= '0' && *p <= '9')
            digit = *p - '0';
        else if (*p >= 'a' && *p <= 'f')
            digit = *p - 'a' + 10;
        else if (*p >= 'A' && *p <= 'F')
            digit = *p - 'A' + 10;
        else
            break;
        if (value > (LONG_MAX - 15) / 16)
            return -1;
        value = value * 16 + digit;
    }
    if (p == line)
        return -1;
    return (*p == '\0' || *p == ';' || *p == ' ' || *p == '\t') ? value : -1;
}

/* Where a 200 body lands. Covers stream to the card through a temp file that is swapped in whole
   on success, so a dropped transfer can never leave half a PNG behind; small answers (the
   /v2/formats lists) land in a caller's buffer and the card is never involved. The transport is
   identical either way - this struct is the whole difference between the two request types. */
struct body_sink {
    const char *out_path; /* file mode when non-NULL: bytes go to <out_path>.tmp, renamed on finish */
    FILE *file;
    char *buf;            /* memory mode: filled up to cap - 1, NUL-terminated by http_get_to_buffer */
    size_t cap;
    size_t len;
};

/* The temp path of the file currently being written. File-scope rather than in the sink because
   one request runs at a time, exactly like the request buffer. */
static char s_tmp_path[520];

/* Opens the sink once the response is worth keeping (a 200 - misses must not churn the card with
   temp files). Memory mode has nothing to open. */
static bool sink_open(struct body_sink *sink)
{
    if (sink->out_path == NULL)
        return true;

    snprintf(s_tmp_path, sizeof(s_tmp_path), "%s.tmp", sink->out_path);
    sink->file = fopen(s_tmp_path, "wb");
    if (!sink->file)
        return false;

    /* FAT pays real overhead per write call, so batch the 2 KB network chunks into card-sized
       flushes. Static for the same reason the request buffer is. */
    static char file_buffer[16384];
    setvbuf(sink->file, file_buffer, _IOFBF, sizeof(file_buffer));
    return true;
}

static bool sink_write(struct body_sink *sink, const char *data, size_t length)
{
    if (sink->file)
        return fwrite(data, 1, length, sink->file) == length;

    /* The terminating NUL's byte stays reserved. A body that fills the buffer cannot be told from
       a truncated one, so it is refused rather than clipped - a caller parses all or nothing. */
    if (length >= sink->cap - sink->len)
        return false;
    memcpy(sink->buf + sink->len, data, length);
    sink->len += length;
    return true;
}

/* fclose flushes, so a failure here is a truncated file, same as a short write. Renaming last
   makes the whole file appear at once; FatFs rename does not replace, so clear the target first. */
static bool sink_finish(struct body_sink *sink)
{
    if (sink->file == NULL)
        return true;

    FILE *file = sink->file;
    sink->file = NULL;
    if (fclose(file) != 0) {
        remove(s_tmp_path);
        return false;
    }

    remove(sink->out_path);
    if (rename(s_tmp_path, sink->out_path) != 0) {
        remove(s_tmp_path);
        return false;
    }
    return true;
}

/* Failure path: nothing of the body may survive, whichever mode. Safe after sink_finish. */
static void sink_abort(struct body_sink *sink)
{
    if (sink->file) {
        fclose(sink->file);
        sink->file = NULL;
        remove(s_tmp_path);
    }
    sink->len = 0;
}

/* connect() with a ceiling: a blocking connect on a dead host would otherwise sit through the whole TCP
   SYN-retry sequence. Non-blocking connect + select is the portable way to bound it (dswifi's sockets
   are lwIP, which supports both). Returns 0 on success, -1 on failure/timeout; the socket is left
   blocking again so the caller's SO_RCVTIMEO governs the data phase. */
static int connect_with_timeout(int sock, const struct sockaddr *addr, socklen_t addrlen, int seconds)
{
    int flags = fcntl(sock, F_GETFL, 0);
    if (flags < 0 || fcntl(sock, F_SETFL, flags | O_NONBLOCK) < 0)
        return -1;

    int rc = connect(sock, addr, addrlen);
    if (rc != 0) {
        if (errno != EINPROGRESS)
            return -1;

        fd_set writable;
        FD_ZERO(&writable);
        FD_SET(sock, &writable);
        struct timeval tv = { seconds, 0 };
        if (select(sock + 1, NULL, &writable, NULL, &tv) <= 0)
            return -1; /* 0 = timed out, <0 = error */

        int so_error = 0;
        socklen_t len = sizeof(so_error);
        if (getsockopt(sock, SOL_SOCKET, SO_ERROR, &so_error, &len) != 0 || so_error != 0)
            return -1;
    }

    return fcntl(sock, F_SETFL, flags) < 0 ? -1 : 0;
}

static void net_disconnect(void)
{
    if (s_sock < 0)
        return;
    if (s_tls)
        tls_close();
    closesocket(s_sock);
    s_sock = -1;
}

/* Establishes the connection the next request will ride: resolve (cached), connect, arm the
   timeouts, and shake TLS hands when configured. On success s_sock is live and blocking. */
static bool net_connect(void)
{
    struct sockaddr_in address = { 0 };
    address.sin_family = AF_INET;
    address.sin_port = htons(s_port);

    if (!s_have_addr) {
        struct addrinfo hints = { 0 };
        hints.ai_family = AF_INET;
        hints.ai_socktype = SOCK_STREAM;
        struct addrinfo *resolved;
        if (getaddrinfo(s_host, NULL, &hints, &resolved) != 0 || resolved == NULL)
            return false;
        s_cached_addr = ((struct sockaddr_in *)resolved->ai_addr)->sin_addr;
        freeaddrinfo(resolved);
        s_have_addr = true;
    }
    address.sin_addr = s_cached_addr;

    int sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock < 0)
        return false;

    if (connect_with_timeout(sock, (struct sockaddr *)&address, sizeof(address), CONNECT_TIMEOUT_SECS) != 0) {
        closesocket(sock);
        s_have_addr = false;
        return false;
    }

    /* A ceiling on every recv/send from here on, so a link that goes quiet mid-transfer fails the
       request rather than hanging the scan. This also covers the TLS handshake and its records: tls.c's
       BIO uses this same blocking socket, and turns a timed-out recv into a fatal error. */
    struct timeval tv = { IO_TIMEOUT_SECS, 0 };
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
    setsockopt(sock, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));

    if (s_tls && !tls_connect(sock)) {
        tls_close();
        closesocket(sock);
        return false;
    }

    s_sock = sock;
    return true;
}

/* One GET over the already-open s_sock, its body into the caller's sink. Returns the HTTP status
   (definitive - the server answered), -1 for a local failure a new connection cannot cure (SD card
   full, buffer too small, malformed request), or RC_RETRY for a transport failure. Decides for
   itself whether the connection is still clean enough to keep: only a provably complete HTTP/1.1
   response (Content-Length present and fully consumed, no "Connection: close") leaves it open.
   Anything else - unknown length, truncation, a mid-body error - closes it, which is exactly the
   old per-request behaviour. */
static int do_request(const char *path, struct body_sink *sink)
{
    /* Sized for the longest query fetch_art can build (a url-encoded 512-byte header sample), plus the
       request line around it. Static: the DS stack is small. A truncated request would be sent as
       garbage, so it is refused outright - snprintf reports the untruncated length. */
    static char request[4096];
    int request_length = snprintf(request, sizeof(request),
        "GET %s HTTP/1.1\r\nHost: %s\r\nUser-Agent: TwilightBoxart-DSi/" APP_VERSION "%s\r\n"
        API_KEY_HEADER "\r\n\r\n",
        path, s_host, isDSiMode() ? "" : " (DS mode)");
    if (request_length <= 0 || request_length >= (int)sizeof(request)) {
        net_disconnect();
        return -1;
    }

    if (xfer_send(request, request_length) != request_length)
        return RC_RETRY;

    /* Buffer until the blank line so a header split across packets still parses. */
    char header[2048];
    int header_length = 0;
    char *body = NULL;
    while (header_length < (int)sizeof(header) - 1) {
        int received = xfer_recv(header + header_length, sizeof(header) - 1 - header_length);
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
    if (body == NULL)
        return RC_RETRY;

    /* Terminate the header block where it ends, so the field lookups below cannot wander into a body
       that happens to contain header-shaped text. body[-4..-1] is the \r\n\r\n delimiter; cutting at
       body[-2] leaves the last header line its \r\n. The body's own bytes are untouched. */
    body[-2] = '\0';

    /* Strict status line, no sscanf: "HTTP/1.<0|1> <3 digits>". Anything else is not an HTTP server
       we can frame responses from. */
    if (strncmp(header, "HTTP/1.", 7) != 0 || (header[7] != '0' && header[7] != '1') || header[8] != ' ')
        return RC_RETRY;
    int minor = header[7] - '0';
    if (header[9] < '1' || header[9] > '5' ||
        header[10] < '0' || header[10] > '9' ||
        header[11] < '0' || header[11] > '9' ||
        (header[12] != ' ' && header[12] != '\r'))
        return RC_RETRY;
    int status = (header[9] - '0') * 100 + (header[10] - '0') * 10 + (header[11] - '0');

    /* Framing, in RFC order: chunked wins over any Content-Length (a middlebox that re-frames a
       response keeps the origin's length header often enough that trusting it would desync the
       connection), and a malformed length is not a framing at all. */
    bool chunked = header_has_token(header, "Transfer-Encoding:", 18, "chunked");
    long content_length = chunked ? -1 : parse_content_length(header);
    if (content_length == CL_MALFORMED)
        return RC_RETRY;

    long body_written = header_length - (long)(body - header);

    /* The reuse test: only a response whose end is provably known (a length, or chunked framing that
       decodes to its terminal chunk) may leave the connection open. Everything here fails safe: when
       in doubt the connection is closed and the next request pays for a new one, which is merely the
       old cost, not an error. */
    bool keep = minor == 1 && (content_length >= 0 || chunked) &&
                !header_has_token(header, "Connection:", 11, "close");

    if (status != 200) {
        /* Reusable only when the whole body already arrived with the headers. It has in every case
           that matters: a miss is an empty 404 (Content-Length: 0), and misses are why this matters
           at all - a quick scan is mostly misses, and each one used to pay a full reconnect. A
           chunked error page is not worth draining; just reconnect. */
        if (!keep || chunked || body_written != content_length)
            net_disconnect();
        return status;
    }

    if (!sink_open(sink)) {
        /* The unread body would poison a kept connection, and an unwritable card is not the network's
           fault: drop the connection, report the local failure. */
        net_disconnect();
        return -1;
    }

    bool ok = true;
    bool transport_failed = false;
    bool sink_failed = false;
    struct body_source source = { body, body_written };
    body_written = 0;

    char chunk[2048];
    if (chunked) {
        /* Chunked framing: size line, that many payload bytes, CRLF, repeat; a zero size ends the
           body, followed by (ignored) trailer lines up to a blank one. Decoded rather than dumped:
           the framing bytes are protocol, not PNG, and the terminal chunk is what makes the
           connection reusable without waiting for a close that a kept-alive server never sends. */
        char line[200];
        for (;;) {
            long remaining = read_framing_line(&source, line, sizeof(line)) < 0
                ? -1
                : parse_chunk_size(line);
            if (remaining < 0) {
                transport_failed = true;
                ok = false;
                break;
            }
            if (remaining == 0)
                break;
            while (ok && remaining > 0) {
                size_t want = (long)sizeof(chunk) < remaining ? sizeof(chunk) : (size_t)remaining;
                int received = body_read(&source, chunk, want);
                if (received <= 0) {
                    transport_failed = true;
                    ok = false;
                    break;
                }
                if (!sink_write(sink, chunk, (size_t)received)) {
                    ok = false;
                    sink_failed = true;
                    break;
                }
                body_written += received;
                remaining -= received;
            }
            if (!ok)
                break;
            /* The CRLF that closes every chunk. */
            if (read_framing_line(&source, line, sizeof(line)) != 0) {
                transport_failed = true;
                ok = false;
                break;
            }
        }
        while (ok) {
            int length = read_framing_line(&source, line, sizeof(line));
            if (length < 0) {
                transport_failed = true;
                ok = false;
            }
            if (length <= 0)
                break;
        }
    } else {
        while (ok) {
            /* With a declared length, stop at exactly that byte: the bytes after it belong to the
               NEXT response. Without one, read to close, as always. */
            size_t want = sizeof(chunk);
            if (content_length >= 0) {
                if (body_written >= content_length)
                    break;
                if ((long)want > content_length - body_written)
                    want = (size_t)(content_length - body_written);
            }
            int received = body_read(&source, chunk, want);
            if (received < 0) {
                transport_failed = true;
                ok = false;
            }
            if (received <= 0)
                break;
            if (!sink_write(sink, chunk, (size_t)received)) {
                ok = false;
                sink_failed = true;
            }
            body_written += received;
        }
    }

    /* A close before the declared end is a dropped connection, not a PNG - unless the sink failed
       first, because a retry cannot cure a full SD card or a too-small buffer and would only pull
       the same bytes again. Local failures stay final, exactly as they always were. An empty body
       is a failure too, with one exception: a buffer sink whose framing PROVES the body ended (a
       declared zero length, or chunked reaching its terminal chunk) gets its empty answer - what
       that means is the caller's judgement, where an empty cover is garbage on any reading and an
       unframed zero-byte close is a drop either way. */
    bool empty_is_complete = sink->out_path == NULL && (content_length == 0 || chunked);
    if ((body_written <= 0 && !empty_is_complete) || (content_length >= 0 && body_written != content_length)) {
        ok = false;
        transport_failed = !sink_failed;
    }

    /* Runs before the keep decision on purpose: a finish that fails is card trouble, and dropping
       the connection there is merely the old per-request cost, never an error. */
    if (ok && !sink_finish(sink))
        ok = false;

    if (!ok) {
        sink_abort(sink);
        if (transport_failed)
            return RC_RETRY;
        net_disconnect();
        return -1;
    }

    if (!keep)
        net_disconnect();
    return 200;
}

/* The shared engine every request type rides: connection reuse, the one retry a reused connection
   has earned, and the stale-streak bookkeeping. Only the sink differs between callers. */
static int http_get(const char *path, struct body_sink *sink)
{
    /* Two attempts at most, and only ever two when the first rode a reused connection. A reused
       connection failing proves nothing - the router may have silently dropped it - so it earns one
       fresh connection; the fresh connection's verdict is final, exactly as it was when every request
       built its own. */
    for (int attempt = 0; attempt < 2; attempt++) {
        if (s_sock >= 0 && time(NULL) - s_last_used > IDLE_REUSE_LIMIT_SECS)
            net_disconnect();

        bool reused = s_sock >= 0;
        if (!reused && !net_connect())
            return -1;

        int rc = do_request(path, sink);
        if (rc != RC_RETRY) {
            if (rc >= 100) {
                s_last_used = time(NULL);
                if (reused)
                    s_stale_streak = 0;
                if (s_stale_streak >= STALE_STREAK_LIMIT)
                    net_disconnect();
            }
            return rc;
        }

        net_disconnect();
        if (!reused)
            return -1;
        s_stale_streak++;
    }
    return -1; /* unreachable: the second pass never runs on a reused connection */
}

int http_get_to_file(const char *path, const char *out_path)
{
    struct body_sink sink = { .out_path = out_path };
    return http_get(path, &sink);
}

int http_get_to_buffer(const char *path, char *buf, size_t size)
{
    struct body_sink sink = { .buf = buf, .cap = size };
    int status = http_get(path, &sink);
    if (status == 200)
        buf[sink.len] = '\0';
    return status;
}
