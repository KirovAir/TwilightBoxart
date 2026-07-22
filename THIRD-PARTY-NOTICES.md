# Third-party notices

TwilightBoxart is licensed under GPL-3.0 (see [LICENSE.md](LICENSE.md)). The DSi homebrew
client vendors and redistributes the following third-party code; their notices travel with
every release artifact that contains them.

## Mbed TLS

`TwilightBoxart.DSi/mbedtls/` is a source copy of [Mbed TLS](https://github.com/Mbed-TLS/mbedtls),
dual-licensed **Apache-2.0 OR GPL-2.0-or-later**. The full license text is preserved at
[TwilightBoxart.DSi/mbedtls/LICENSE](TwilightBoxart.DSi/mbedtls/LICENSE). It is compiled into
`TwilightBoxart-DSi.nds` to provide HTTPS on the console.

## dswifi (patched)

`TwilightBoxart.DSi/dswifi-patched/` carries a prebuilt `libdswifi9.a` from
[BlocksDS dswifi](https://codeberg.org/blocksds/dswifi), **MIT-licensed**. See
[TwilightBoxart.DSi/dswifi-patched/COPYING](TwilightBoxart.DSi/dswifi-patched/COPYING).

The binary is upstream tag `v1.22.1-blocks` plus one local patch
([dhcp-broadcast-flag.patch](TwilightBoxart.DSi/dswifi-patched/dhcp-broadcast-flag.patch)).
[dswifi-patched/README.md](TwilightBoxart.DSi/dswifi-patched/README.md) documents the exact
rebuild recipe, so the blob is reproducible from source.

## Everything fetched at runtime

Box art comes from [GameTDB](https://www.gametdb.com) and
[libretro-thumbnails](https://github.com/libretro-thumbnails); identification data from
[No-Intro](https://no-intro.org) via the
[libretro-database](https://github.com/libretro/libretro-database) mirror. None of it is
redistributed in this repository.

At runtime the server builds and serves a **derived index** of the No-Intro data
(`nointro.db`, via `GET /v2/index/nointro.db`): game names, serials and checksums. This is
factual metadata, deduplicated and normalised, not a copy of the DAT files. The file
carries its own provenance in its `meta` table (attribution, source, and the version of
every DAT that went in). See the Credits and Legal sections of the [README](README.md).
