// networking.c: the HTTP transport for the backend. One request rides one connection (Connection:
// close); TLS is delegated to tls.c, which shares this socket. Split out of main.c so the socket
// plumbing - and the timeouts that keep a flaky link from hanging the scan - live in one place.

#include <errno.h>
#include <fcntl.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include <nds.h>
#include <dswifi9.h>
#include <netdb.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/time.h>

#include "networking.h"
#include "tls.h"

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

static char s_host[128];
static int s_port;
static bool s_tls;

void net_configure(const char *host, int port, bool tls)
{
    strncpy(s_host, host, sizeof(s_host) - 1);
    s_host[sizeof(s_host) - 1] = '\0';
    s_port = port;
    s_tls = tls;
}

/* One request rides one connection; these pick the plain or TLS pipe for it. */
static int xfer_send(int sock, const void *buf, size_t len)
{
    if (s_tls)
        return tls_send(buf, len);
    return send(sock, buf, len, 0);
}

static int xfer_recv(int sock, void *buf, size_t len)
{
    if (s_tls)
        return tls_recv(buf, len);
    return recv(sock, buf, len, 0);
}

/* Case-insensitive Content-Length lookup in a header block, -1 when absent. */
static long parse_content_length(const char *headers)
{
    for (const char *line = headers; line; line = strchr(line, '\n'), line = line ? line + 1 : NULL) {
        if (strncasecmp(line, "Content-Length:", 15) == 0)
            return atol(line + 15);
    }
    return -1;
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

int http_get_to_file(const char *path, const char *out_path)
{
    struct sockaddr_in address = { 0 };
    address.sin_family = AF_INET;
    address.sin_port = htons(s_port);

    struct addrinfo hints = { 0 };
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    struct addrinfo *resolved;
    if (getaddrinfo(s_host, NULL, &hints, &resolved) != 0 || resolved == NULL)
        return -1;
    address.sin_addr = ((struct sockaddr_in *)resolved->ai_addr)->sin_addr;
    freeaddrinfo(resolved);

    int sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock < 0)
        return -1;

    if (connect_with_timeout(sock, (struct sockaddr *)&address, sizeof(address), CONNECT_TIMEOUT_SECS) != 0) {
        closesocket(sock);
        return -1;
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
        return -1;
    }

    /* Sized for the longest query fetch_art can build (a url-encoded 512-byte header sample), plus the
       request line around it. Static: the DS stack is small. A truncated request would be sent as
       garbage, so it is refused outright - snprintf reports the untruncated length. */
    static char request[4096];
    int request_length = snprintf(request, sizeof(request),
        "GET %s HTTP/1.1\r\nHost: %s\r\nUser-Agent: TwilightBoxart-DSi/" APP_VERSION "%s\r\n"
        API_KEY_HEADER "\r\nConnection: close\r\n\r\n",
        path, s_host, isDSiMode() ? "" : " (DS mode)");
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

    /* FatFs rename does not replace, so clear the target first. The new art is already complete on disk
       at this point. */
    remove(out_path);
    if (rename(tmp_path, out_path) != 0) {
        remove(tmp_path);
        return -1;
    }
    return 200;
}
