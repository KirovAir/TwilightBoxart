// TLS 1.2 client transport over an already-connected dswifi socket, mbedTLS underneath.
// One handshake result is cached, so later connections resume the session cheaply.
#ifndef TLS_H
#define TLS_H

#include <stdbool.h>
#include <stddef.h>

bool tls_global_init(const char *host);
bool tls_connect(int sock);
int tls_send(const void *buf, size_t len);
int tls_recv(void *buf, size_t len);
void tls_close(void);

#endif
