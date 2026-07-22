// The DS has no random number generator, so entropy is scraped together from the free-running
// hardware timers, the LCD line counter and the RTC, folded through a xorshift mix. Weaker than
// a real TRNG, but the session only fetches public box art over a certificate-checked link.
#include <nds.h>
#include <string.h>
#include <time.h>

int mbedtls_hardware_poll(void *data, unsigned char *output, size_t len, size_t *olen);

int mbedtls_hardware_poll(void *data, unsigned char *output, size_t len, size_t *olen)
{
    (void)data;
    static u32 state;
    if (state == 0) {
        /* free-running timers at different rates; nothing else in the app uses them */
        TIMER0_CR = TIMER_ENABLE | TIMER_DIV_1;
        TIMER1_CR = TIMER_ENABLE | TIMER_DIV_64;
        state = (u32)time(NULL) ^ 0x9E3779B9;
    }

    for (size_t i = 0; i < len; i++) {
        state ^= TIMER0_DATA | ((u32)TIMER1_DATA << 16);
        state ^= REG_VCOUNT * 0x01000193;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        output[i] = (unsigned char)(state ^ (state >> 24));
        swiDelay(state & 0x3F);
    }

    *olen = len;
    return 0;
}

/* Millisecond clock for mbedTLS; second precision from the RTC is plenty for session ages. */
#include <mbedtls/platform_time.h>
mbedtls_ms_time_t mbedtls_ms_time(void)
{
    return (mbedtls_ms_time_t)time(NULL) * 1000;
}
