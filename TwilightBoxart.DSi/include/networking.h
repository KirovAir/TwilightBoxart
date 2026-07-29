#ifndef NETWORKING_H
#define NETWORKING_H

#include <stdbool.h>

/* The HTTP transport for the backend, split out of main.c so the request/socket plumbing lives on its
   own (mirrors how Kekatsu-DS and other modern DS(i) net apps keep a networking module). TLS is handled
   by tls.c; this owns the plain socket, the timeouts, and the request/response framing. */

/* Point the client at the backend. Call once after the config is loaded, so http_get_to_file() need not
   carry the endpoint on every call. The host is copied, so the caller's buffer need not outlive it. */
void net_configure(const char *host, int port, bool tls);

/* GET `path` from the backend into `out_path`, via a temp file so a failed or truncated transfer never
   harms art already on the card. Returns the HTTP status, or -1 on a transport error or timeout.
   Anything but 200 leaves no file behind. Bounded by a connect timeout and a per-recv/-send timeout, so
   a stalled server or a dropped link fails the request instead of hanging the scan. */
int http_get_to_file(const char *path, const char *out_path);

#endif
