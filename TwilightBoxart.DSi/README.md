# TwilightBoxart for DS and DSi

Homebrew that fills your launcher's boxart folder from the console itself:
`_nds/TWiLightMenu/boxart/` with PNGs for TWiLightMenu++, or `_pico/covers/user/` with the
fixed-format 8-bit BMPs Pico Launcher (DS Pico) reads. Runs on a DSi in DSi mode (WPA2 WiFi)
and on an original DS or DS Lite flashcart in DS mode (open or WEP WiFi). It is the thinnest client
of them all: for each ROM it sends the file name and the file's first 512 bytes, and writes the PNG
it gets back over HTTPS by default or plain HTTP (`backend_tls` in the ini; the compiled-in
default is `DEFAULT_BACKEND_TLS` in `source/main.c`). No JSON, no image decoding, and no ROM
parsing on the DS. Which console a file is, where its serial lives, what title it carries: all
of that is worked out server-side, so this
binary never goes stale as the backend improves. The backend guarantees every PNG fits
TWiLightMenu++'s 45,056-byte box art slot.

## Using it

Copy `twilightboxart.nds` anywhere on your card, launch it from TWiLightMenu++, and that is it:
it connects, scans the whole card (skipping `_nds`), fetches what is missing and reports found,
written, already there and no art. Hold B to stop a scan. Art already on the card is left alone.

At launch a small options screen picks the launcher (TWLMenu++ or DS Pico), the cover size,
border style, thicker border and overwrite, all with the D-pad; A starts. Choices persist in the
same settings file as the WiFi network. With DS Pico selected the size and border rows go dark:
Pico's cover format is fixed, so there is nothing there to choose, and the client just asks the
backend for the ready-made BMP. The touch screen is only used for typing on the keyboard.

That settings file (`/_nds/TwilightBoxart.ini`, written on first run) also carries the backend:
`backend_host`, `backend_port` and `backend_tls` default to the hosted service at
boxart.kirovair.com, and a self-hoster can point a card at their own server by editing them:
`backend_tls = 0` for a plain-HTTP instance on the LAN.

There is deliberately nothing else to set up on the console itself. If no known network works, the app scans the air and
lists what it finds, strongest first; pick yours with the D-pad or a tap and type only the password
(open networks skip even that). Saved credentials come
back pre-filled, so a typo means fixing one character rather than retyping everything. A first
join takes ten seconds or so (the WPA2 key math alone costs about five on this CPU) and failed
attempts retry once by themselves before asking again. Hidden networks cannot be joined in DSi
mode at all; that is a library limitation, so unhide the SSID for the setup. What worked
is remembered in `/_nds/TwilightBoxart.ini`, in plain text, the same way the console itself stores
its WiFi settings.

The background tune is "Pixel Cart Drift" by Jesse Sander, embedded as a 14 kHz PCM loop
(`data/music.bin`) playing on a hardware sound channel.

DS, DSi and GBA ROMs are matched by the game code in their header, which needs no index and works
for practically everything. Anything else (GB, GBC, NES, SNES, N64, Mega Drive, SMS, Game Gear,
FDS) is matched by its No-Intro file name, so keep those files named the way they came.

## WiFi reality check

- In **DSi mode** (on a DSi: in TWiLightMenu, press Y on the app and set Run in: DSi mode)
  dswifi connects to normal WPA2 networks.
  The console's stored connections are tried first (Advanced Setup slots 4 to 6 are the
  WPA2-capable ones), and when none work the app simply asks on screen and saves the answer. If
  even that fails, the router is usually the problem: WPA3-only or a 5 GHz-only network; a
  2.4 GHz WPA2 (AES) guest network always works.
- In **DS mode** (flashcarts, original DS/DS Lite) the 2005 radio only does open or WEP
  networks, and the app says so at launch. A passwordless guest SSID or phone hotspot is the
  easy path. A WEP key can be typed as text (5/13/16 characters) or as the hex form routers
  usually print (10/26/32 digits). WPA2 networks are left out of the scan list there, since
  that radio can never join them. The console's stored WFC connections (slots 1 to 3) are
  still tried first.
- Nintendo's WFC servers being dead does not matter here: the app talks HTTPS to the hosted
  backend (or plain HTTP to your own on the LAN) and never touches a Nintendo server.

## Testing in melonDS

melonDS boots a bare `.nds` with no SD card attached, so the app stops at "No SD card / FAT
found" until you give it one:

- **DS mode (easiest):** Config, Emu settings, DLDI tab: enable DLDI and point it at an SD image.
  "Sync SD card to folder" is ideal for testing, since you can drop ROMs into a host folder and
  read the written PNGs straight back out of it.
- **DSi mode:** needs DSi firmware/NAND dumps plus a DSi SD image; more setup, closer to real
  hardware.

A note for the record: an earlier revision here blamed melonDS for a WiFi init hang. The real
cause was this Makefile linking BlocksDS's default ARM7 core, which contains no WiFi driver at
all, so the ARM9 waited forever for a handler that did not exist. With `arm7_dswifi.elf` linked,
WiFi init, the association timeout and the on-screen setup all run in melonDS too. Since the app
runs in DS mode, the whole loop runs in the emulator: enable internet connectivity in melonDS's
WiFi settings and the scan finds melonAP, an open network the app can join like any other. Real
WPA2 still needs a console.

## Building

With [BlocksDS](https://blocksds.skylyrac.net/) installed: `make`. Without it, use the container:

```bash
docker run --rm -v "$PWD:/work" -w /work skylyrac/blocksds:slim-latest make
```

CI builds the `.nds` on every change under `.github/workflows/build-dsi.yml`.
