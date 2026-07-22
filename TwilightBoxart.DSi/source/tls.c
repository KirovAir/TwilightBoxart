#include <stdio.h>
#include <string.h>

#include <mbedtls/ctr_drbg.h>
#include <mbedtls/entropy.h>
#include <mbedtls/error.h>
#include <mbedtls/net_sockets.h>
#include <mbedtls/ssl.h>
#include <mbedtls/x509_crt.h>

#include <nds.h>
#include <sys/socket.h>

#include "tls.h"
#include "tls_roots.h"

/* WANT_READ/WANT_WRITE can spin forever on a wedged link; cap the retries, one vblank each, so
   the wait is ~33 seconds of frames instead of a hang with no escape. */
#define TLS_SPIN_LIMIT 2000

static mbedtls_entropy_context s_entropy;
static mbedtls_ctr_drbg_context s_drbg;
static mbedtls_x509_crt s_cas;
static mbedtls_ssl_config s_conf;
static mbedtls_ssl_context s_ssl;

/* The abbreviated handshake spares the DS the multi-second key exchange on every cover. */
static mbedtls_ssl_session s_session;
static bool s_have_session;

static int s_fd = -1;

static int bio_send(void *ctx, const unsigned char *buf, size_t len)
{
    (void)ctx;
    int sent = send(s_fd, buf, len, 0);
    return sent < 0 ? MBEDTLS_ERR_NET_SEND_FAILED : sent;
}

static int bio_recv(void *ctx, unsigned char *buf, size_t len)
{
    (void)ctx;
    int got = recv(s_fd, buf, len, 0);
    /* 0 (peer closed) passes through as-is: ssl_fetch_input maps it to MBEDTLS_ERR_SSL_CONN_EOF. */
    return got < 0 ? MBEDTLS_ERR_NET_RECV_FAILED : got;
}

bool tls_global_init(const char *host)
{
    mbedtls_entropy_init(&s_entropy);
    mbedtls_ctr_drbg_init(&s_drbg);
    mbedtls_x509_crt_init(&s_cas);
    mbedtls_ssl_config_init(&s_conf);
    mbedtls_ssl_init(&s_ssl);
    mbedtls_ssl_session_init(&s_session);

    if (mbedtls_ctr_drbg_seed(&s_drbg, mbedtls_entropy_func, &s_entropy, NULL, 0) != 0)
        goto fail;
    if (mbedtls_x509_crt_parse(&s_cas, (const unsigned char *)TLS_ROOT_CAS, sizeof(TLS_ROOT_CAS)) != 0)
        goto fail;
    if (mbedtls_ssl_config_defaults(&s_conf, MBEDTLS_SSL_IS_CLIENT,
                                    MBEDTLS_SSL_TRANSPORT_STREAM, MBEDTLS_SSL_PRESET_DEFAULT) != 0)
        goto fail;

    mbedtls_ssl_conf_authmode(&s_conf, MBEDTLS_SSL_VERIFY_REQUIRED);
    mbedtls_ssl_conf_ca_chain(&s_conf, &s_cas, NULL);
    mbedtls_ssl_conf_rng(&s_conf, mbedtls_ctr_drbg_random, &s_drbg);

    if (mbedtls_ssl_setup(&s_ssl, &s_conf) != 0)
        goto fail;
    if (mbedtls_ssl_set_hostname(&s_ssl, host) != 0)
        goto fail;

    mbedtls_ssl_set_bio(&s_ssl, NULL, bio_send, bio_recv, NULL);
    return true;

fail:
    mbedtls_ssl_session_free(&s_session);
    mbedtls_ssl_free(&s_ssl);
    mbedtls_ssl_config_free(&s_conf);
    mbedtls_x509_crt_free(&s_cas);
    mbedtls_ctr_drbg_free(&s_drbg);
    mbedtls_entropy_free(&s_entropy);
    return false;
}

bool tls_connect(int sock)
{
    s_fd = sock;
    mbedtls_ssl_session_reset(&s_ssl);
    if (s_have_session)
        mbedtls_ssl_set_session(&s_ssl, &s_session);

    int spins = 0;
    int ret;
    while ((ret = mbedtls_ssl_handshake(&s_ssl)) != 0) {
        if (ret != MBEDTLS_ERR_SSL_WANT_READ && ret != MBEDTLS_ERR_SSL_WANT_WRITE) {
            printf("\x1b[31;1mTLS %04x\x1b[37;1m ", (unsigned)-ret);
            break;
        }
        if (++spins > TLS_SPIN_LIMIT)
            break;
        cothread_yield_irq(IRQ_VBLANK);
    }

    if (ret != 0) {
        /* The TCP connection is dead after a fatal error, so retrying the handshake on it can
           never work. Drop the cached session too: the next connection does a full handshake
           on a fresh socket. */
        s_have_session = false;
        s_fd = -1;
        return false;
    }

    if (!s_have_session && mbedtls_ssl_get_session(&s_ssl, &s_session) == 0)
        s_have_session = true;
    return true;
}

int tls_send(const void *buf, size_t len)
{
    int spins = 0;
    int ret;
    while ((ret = mbedtls_ssl_write(&s_ssl, buf, len)) == MBEDTLS_ERR_SSL_WANT_READ ||
           ret == MBEDTLS_ERR_SSL_WANT_WRITE) {
        if (++spins > TLS_SPIN_LIMIT)
            return -1;
        cothread_yield_irq(IRQ_VBLANK);
    }
    return ret;
}

int tls_recv(void *buf, size_t len)
{
    int spins = 0;
    int ret;
    while ((ret = mbedtls_ssl_read(&s_ssl, buf, len)) == MBEDTLS_ERR_SSL_WANT_READ ||
           ret == MBEDTLS_ERR_SSL_WANT_WRITE) {
        if (++spins > TLS_SPIN_LIMIT)
            return -1;
        cothread_yield_irq(IRQ_VBLANK);
    }
    if (ret == MBEDTLS_ERR_SSL_PEER_CLOSE_NOTIFY)
        return 0;
    return ret;
}

void tls_close(void)
{
    if (s_fd < 0)
        return;
    /* The cached session outlives the connection on purpose - resuming it is its whole point -
       so a normal close leaves it alone; only a failed handshake clears it (see tls_connect). */
    mbedtls_ssl_close_notify(&s_ssl);
    s_fd = -1;
}
