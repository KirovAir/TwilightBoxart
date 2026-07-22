// Applied on top of mbedTLS's default config. The DS has no POSIX sockets, no gettimeofday
// timers and no OS entropy pool, so those modules go; TLS 1.3 and server support go because
// this is a TLS 1.2 client and every byte of ROM counts.
#ifndef TB_MBEDTLS_CONFIG_H
#define TB_MBEDTLS_CONFIG_H

#undef MBEDTLS_NET_C
#undef MBEDTLS_TIMING_C
#undef MBEDTLS_SSL_SRV_C
#undef MBEDTLS_SSL_PROTO_TLS1_3
#undef MBEDTLS_SSL_TLS1_3_COMPATIBILITY_MODE
#undef MBEDTLS_SSL_EARLY_DATA
#undef MBEDTLS_SSL_SESSION_TICKETS
#undef MBEDTLS_FS_IO
#undef MBEDTLS_PSA_ITS_FILE_C
#undef MBEDTLS_PSA_CRYPTO_STORAGE_C

#define MBEDTLS_NO_PLATFORM_ENTROPY
#define MBEDTLS_ENTROPY_HARDWARE_ALT
#define MBEDTLS_PLATFORM_MS_TIME_ALT

#endif
