<p align="center">
  <img src="docs/logo.png" width="120" alt="TwilightBoxart">
</p>

<h1 align="center">TwilightBoxart</h1>

<p align="center">Box art for <a href="https://github.com/DS-Homebrew/TWiLightMenu" target="_blank" rel="noopener"><b>TWiLightMenu++</b></a>, <a href="https://github.com/LNH-team/pico-launcher" target="_blank" rel="noopener"><b>Pico Launcher</b></a> and <a href="https://github.com/coderkei/akmenu-next" target="_blank" rel="noopener"><b>AKMenu-Next</b></a>, straight onto your SD card.</p>

<p align="center">
  <a href="https://github.com/KirovAir/TwilightBoxart/releases"><img src="https://img.shields.io/github/v/release/KirovAir/TwilightBoxart?color=7566DD&label=release" alt="Latest release"></a>
  <a href="https://github.com/KirovAir/TwilightBoxart/releases"><img src="https://img.shields.io/github/downloads/KirovAir/TwilightBoxart/total?color=C75BB4&label=downloads" alt="Downloads"></a>
  <a href="https://github.com/KirovAir/TwilightBoxart/stargazers"><img src="https://img.shields.io/github/stars/KirovAir/TwilightBoxart?color=F5A05C&label=stars" alt="Stars"></a>
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="GPL-3.0"></a>
</p>

<h3 align="center"><a href="https://twilightboxart.com/" target="_blank" rel="noopener">→ Open the web app ←</a></h3>
<p align="center">Plug the card in, press scan.</p>

Automatically downloads box art / covers for your favorite launcher. DS, DSi and GBA, plus everything
else a Nintendo DS can play that is supported: Gameboy, SNES, NES, Mega Drive, PC Engine, WonderSwan, Neo Geo
Pocket, the Atari consoles [and more](#supported-systems). Runs in your browser, as a desktop app,
or as homebrew on the DS itself. 😊
Missing a cover? Easily [create your own custom covers](#custom-covers)! (Web only)

## 👾 DS / DSi version

Hate connecting your SD card to a computer? No problem. Copy the `.nds` to your card, launch it, and
the console joins your WiFi and fills its own boxart folder. Works on a DSi, and on a DS or DS Lite
with a flashcart.

Reads each game's header, so badly named roms still match. Out of the box it picks the launcher from
what is on the card and skips games that already have a cover, but that is just the default: launcher,
shape, size, border, overwrite and scan mode are all in the options menu.

[Releases](https://github.com/KirovAir/TwilightBoxart/releases)

<p align="center">
  <img src="docs/dsi.png" width="240" alt="The DS/DSi homebrew client, running in melonDS">
</p>

## 🌐 Web version

[twilightboxart.com](https://twilightboxart.com/) scans the card and writes the covers straight onto
it, no upload and no install.

<p align="center">
  <img src="docs/webapp.png" width="620" alt="The TwilightBoxart web app">
</p>

<a id="custom-covers"></a>
Nothing found for a game? Add your own: drop any image on it (a scan, a photo of the box, a
screenshot) and it comes back exactly as needed for your launcher. It is sent once to be converted
and never stored. :)

<p align="center">
  <img src="docs/customcover.png" width="520" alt="Adding a custom cover in the web app">
</p>

## 💻 Desktop version

Windows, macOS and Linux. Works offline: it builds its own game database when it cannot reach a
backend. [Releases](https://github.com/KirovAir/TwilightBoxart/releases)

<p align="center">
  <img src="docs/desktop.png" width="440" alt="The desktop app on macOS">
</p>

## 🚀 Supported launchers

Every app asks which launcher up front.

| Launcher | Format | Where it goes |
| --- | --- | --- |
| <a href="https://github.com/DS-Homebrew/TWiLightMenu" target="_blank" rel="noopener">TWiLightMenu++</a> | PNG, sized to the menu's limits | `_nds/TWiLightMenu/boxart/<rom name>.png` |
| <a href="https://github.com/LNH-team/pico-launcher" target="_blank" rel="noopener">Pico Launcher</a> (DS Pico) | 8-bit BMP, 128 × 96 | `_pico/covers/user/<rom name>.bmp` |
| <a href="https://github.com/coderkei/akmenu-next" target="_blank" rel="noopener">AKMenu-Next</a> | 8-bit BMP, 128 × 96 | `_nds/covers_name/<rom name without extension>.bmp` |

Pico covers go in the filename-keyed `user` folder: it works for every system below, where the
game-code folders only do NDS and GBA.
AKMenu-Next reads the same BMP (its loader trims the black padding columns) and looks covers up by
the ROM filename without its extension.

Only cover images are written, into the boxart folder. Nothing else on the card is touched.

<a id="supported-systems"></a>
## 🕹️ Supported systems

Games are identified by what they **contain**, not what they are called. Rename a rom to
`aaaa.gba` and it still gets the right cover.

<details>
<summary><b>All 26 systems, and how each one is matched</b></summary>

| System | Matching (in order) |
| --- | --- |
| Nintendo - Nintendo DS | title id / crc32 / sha1 / filename |
| Nintendo - Nintendo DSi | title id / crc32 / sha1 / filename |
| Nintendo - Game Boy Advance | title id / crc32 / sha1 / filename |
| Nintendo - Game Boy | game code / crc32 / sha1 / filename |
| Nintendo - Game Boy Color | game code / crc32 / sha1 / filename |
| Nintendo - Nintendo 64 *(new in 2.0)* | game code / crc32 / sha1 / filename |
| Nintendo - Nintendo Entertainment System | crc32 / sha1 / filename |
| Nintendo - Super Nintendo Entertainment System | crc32 / sha1 / filename |
| Nintendo - Family Computer Disk System | game code / crc32 / sha1 / filename |
| Sega - Mega Drive - Genesis | serial / crc32 / sha1 / filename |
| Sega - Master System - Mark III | crc32 / sha1 / filename |
| Sega - Game Gear | crc32 / sha1 / filename |
| Sega - SG-1000 *(new in 2.0)* | crc32 / sha1 / filename |
| NEC - PC Engine - TurboGrafx 16 *(new in 2.0)* | crc32 / sha1 / filename |
| Bandai - WonderSwan *(new in 2.0)* | crc32 / sha1 / filename |
| Bandai - WonderSwan Color *(new in 2.0)* | crc32 / sha1 / filename |
| SNK - Neo Geo Pocket *(new in 2.0)* | crc32 / sha1 / filename |
| SNK - Neo Geo Pocket Color *(new in 2.0)* | crc32 / sha1 / filename |
| Atari - 2600 *(new in 2.0)* | crc32 / sha1 / filename |
| Atari - 5200 *(new in 2.0)* | crc32 / sha1 / filename |
| Atari - 7800 *(new in 2.0)* | crc32 / sha1 / filename |
| Coleco - ColecoVision *(new in 2.0)* | crc32 / sha1 / filename |
| Mattel - Intellivision *(new in 2.0)* | crc32 / sha1 / filename |
| Microsoft - MSX *(new in 2.0)* | crc32 / sha1 / filename |
| Microsoft - MSX2 *(new in 2.0)* | crc32 / sha1 / filename |
| Nintendo - Pokemon Mini *(new in 2.0)* | crc32 / sha1 / filename |

That is every console TWiLightMenu++ emulates that both [No-Intro](https://no-intro.org) and
[libretro-thumbnails](https://github.com/libretro-thumbnails) have data for. It takes both: one
names the dump, the other has the cover. The menu also lists `.xex`/`.atr` (Atari 8-bit), `.m5`
(Sord M5) and `.dsk` (Amstrad CPC), which are left out on purpose. Nothing publishes box art for
them, so every lookup would miss.

File extensions follow TWiLightMenu++'s own list, so anything the menu will launch is something
this will scan. That includes `.agb`/`.mb` for GBA and `.srl`/`.ids`/`.app`/`.ndz` for DS(i).

</details>

A lot of work went into the matching. It rarely misses.

## 🖼️ Boxart sources

* [GameTDB](https://www.gametdb.com) by title id matching.
* [libretro-thumbnails](https://github.com/libretro-thumbnails) by
  [No-Intro](https://no-intro.org) name matching.
* Every TWiLightMenu++ cover is delivered under the menu's box art size limit, so nothing silently
  refuses to show up on the console. Pico and AKMenu-Next covers come pre-converted to their
  compatible 8-bit BMP format, no separate converter needed.

For Pico Launcher and AKMenu-Next, the apps show the fixed cover size and mark border settings as
unavailable. AKMenu-Next currently shows covers for DS and GBA games only; covers for the other
systems it launches through plugins are written all the same, ready for when it does. Switching
back to TWiLightMenu++ restores its saved size and border choices.

## 🔲 Borders

You can select between four (or no) border options: `DSi Theme`, `3DS Theme`, `Black` and `White` (which both have a `Thicker border` option).

| DSi Theme | 3DS Theme |
|     :---:    |     :---:      |
| <img width="216" height="192" alt="dsi-theme" src="https://github.com/user-attachments/assets/c80ccd9b-9984-4fa3-aefc-d797192ab624" />   | <img width="216" height="192" alt="3ds-theme" src="https://github.com/user-attachments/assets/5c2c7d51-3669-433f-9c8f-045cbe85445b" />     |
| **Black** | **White** |
| <img width="217" height="192" alt="black" src="https://github.com/user-attachments/assets/f308080c-3e99-4ef7-8204-7d444798e815" />     | <img width="217" height="192" alt="white" src="https://github.com/user-attachments/assets/7a3b911f-978d-4650-b908-b922e8c38a5a" />       |
| **Black (Thicker border)** | **White (Thicker border)** |
| <img width="217" height="192" alt="black-thicker" src="https://github.com/user-attachments/assets/09ff9cda-9ac6-4003-be28-226038e5a4e1" /> | <img width="217" height="192" alt="white-thicker" src="https://github.com/user-attachments/assets/3085fbf8-aac6-4b4e-8e9b-9c343431aa44" /> |


## 🐳 Self-hosting

One container, one volume, no database server, no preparation:

```bash
docker compose up -d      # then open http://localhost:8186
```

On first boot the server downloads the public No-Intro data and builds its own game index in the
background, serving all the while. Set an admin password and `/admin.html` gives you instance
stats and an "update No-Intro index" button for whenever the database should catch up.
Configuration is documented inline in [docker-compose.yml](docker-compose.yml); the backend's own
[README](TwilightBoxart.Web/README.md) covers the API.

## License

GPL-3.0. See [LICENSE.md](LICENSE.md). The DS/DSi client ships with
[Mbed TLS](https://github.com/Mbed-TLS/mbedtls) (Apache-2.0/GPL-2.0) and a patched
[dswifi](https://codeberg.org/blocksds/dswifi) (MIT), built with
[BlocksDS](https://blocksds.skylyrac.net/); their notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Credits

Covers come from [GameTDB](https://www.gametdb.com) and
[libretro-thumbnails](https://github.com/libretro-thumbnails); identification data from
[No-Intro](https://no-intro.org) via the [libretro-database](https://github.com/libretro/libretro-database)
mirror. Built for [TWiLightMenu++](https://github.com/DS-Homebrew/TWiLightMenu),
[Pico Launcher](https://github.com/LNH-team/pico-launcher) and
[AKMenu-Next](https://github.com/coderkei/akmenu-next) by
[LNH-team](https://github.com/LNH-team).
Music on the DS/DSi client: "Pixel Cart Drift" by Jesse Sander.

## Legal

TwilightBoxart is a fan-made, open-source tool and is not affiliated with, endorsed by or
sponsored by Nintendo. It distributes no games and contains none: it reads files already on your
card to identify them, and writes cover images, nothing else. Covers are fetched from the
community databases credited above; identification data is factual metadata (names, serials,
checksums). If you hold rights to something served by this project and want it removed, open a
GitHub issue and it will be handled promptly.

## Development

```
TwilightBoxart.Web         The API server and the web app it serves
TwilightBoxart.Pipeline    Art fetching, caching and eviction
TwilightBoxart.Data        EF Core + SQLite records
TwilightBoxart.Core        Identification, header parsers, index building, rendering, art sources
TwilightBoxart.Desktop     Desktop app (Avalonia)
TwilightBoxart.DSi         DS/DSi homebrew client (BlocksDS)
TwilightBoxart.Tests       MSTest suite
```

`dotnet run --project TwilightBoxart.Web` starts everything on port 8186.
