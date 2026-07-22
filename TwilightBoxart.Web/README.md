# TwilightBoxart.Web

The backend. It identifies ROMs, fetches box art, resizes it, draws the border and caches the result,
so that a browser tab, a DS on your wifi and a desktop app can all be thin clients over the same API.

Minimal APIs on .NET 10, Serilog to the console, one SQLite file that the build produces rather than
the app migrates, and two cache layers on disk. No database server, no Redis, no queue.

## Why a backend at all

A native client could do almost all of this locally. Two things it cannot do:

- **A browser cannot fetch GameTDB art.** `art.gametdb.com` sends no CORS headers at all, and an
  `<img>` tag taints the canvas so the pixels can never be read back to resize or composite. GameTDB
  is the only source of DS/DSi art, and TWiLightMenu++ *is* a DS application, so the flagship path
  needs a relay permanently.
- **A shared cache.** GameTDB is volunteer-run. One cache in front of 18,000-ROM library scans is the
  difference between being a good neighbour and being a problem.

## Quick start

```bash
docker compose up -d          # from the repo root
```

Then `http://localhost:8186`. Configuration is documented inline in `docker-compose.yml`.

Locally:

```bash
dotnet run --project TwilightBoxart.Web
```

Development uses `./data` for its data directory, a short negative-cache window and small cache
budgets, so a local run actually exercises eviction.

## API

Version lives in the **path**, not a header: the DS client cannot easily set headers and the URL has
to be pasteable into a config file.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/v2/identify` | Batch fingerprints to identities, each carrying its ready-made art URL |
| `GET` | `/v2/index/nointro.db` | The generated No-Intro index, for the desktop client's Local mode |
| `GET` | `/v2/art/{platform}/{key}.png` | Rendered box art. **The canonical cacheable URL** |
| `GET` | `/v2/art.png` | Identify and deliver in one call, for a client that cannot batch. PNG bytes or empty 404 |
| `GET` | `/v2/formats` | File extensions worth scanning, so clients need not hard-code them |
| `GET` | `/v2/health` | Whether this instance is up, and whether it has an index |
| `POST` | `/api` | The v0.7 protocol, served for real: classic form fields in, PNG bytes out |
| | `/v2/admin/*` | Owner-only, cookie-authed: login, stats, and the index-rebuild button behind `/admin.html` |

The `/api` shim exists because the v0.7 fleet never updated and never will. It works better than
the backend it was written for: v0.7 always sent the inner ROM's first 512 bytes and SHA-1, which
are the identification ladder's strongest evidence. Only the file name (often the archive's) was
weak, and the shim barely needs it.

**Clients never build the canonical URL.** Identify hands it out as `artPath`, and `/v2/art.png`
advertises it in `Content-Location`. Its structure is the server's business; clients follow it
verbatim and append render parameters. This is deliberate: clients in the wild do not update (v0.7
installs were still calling the retired endpoint six years on), so any URL grammar or platform
vocabulary a client had to carry would be frozen at whatever version it shipped with.

For the humans: `{platform}` accepts the slug (`gb`, `gbc`, `gba`, `nds`, `dsi`, `nes`, `snes`,
`n64`, `fds`, `md`, `sms`, `gg`, `min`, `sg`, `pce`, `ws`, `wsc`, `ngp`, `ngpc`, `a26`, `a52`,
`a78`, `col`, `int`, `msx`, `msx2`) or the `consoleType` name identify returns (`NintendoDs`),
case-insensitive. `{key}` is the **title**, never the fingerprint: the 4-character title id for
platforms whose header carries one (`ASME`), otherwise a 16-hex digest of the canonical No-Intro
name. Only `[A-Za-z0-9_-]{1,64}` is accepted; anything else is a 404.

### `POST /v2/identify`

At most **500 items** and **1 MB** per request. Both are hard limits, not settings.

```jsonc
{
  "items": [
    {
      "fileName": "Super Mario 64 DS (USA).nds",  // the ROM's own name, NOT the archive's
      "crc32": 3229619840,                        // free from the archive header, see below
      "sha1": null,
      "header": null,                             // base64, first 512 bytes, when cheap
      "size": 67108864,
      "tag": "row-17"                             // echoed back so you can correlate
    }
  ]
}
```

```jsonc
{
  "items": [
    {
      "consoleType": "NintendoDs",
      "key": "ASME",
      "serial": "ASME",
      "title": "SM64DS",
      "canonicalName": "Super Mario 64 DS (USA)",
      "regionId": "E",
      "matchMethod": "HeaderSerial",
      "tag": "row-17",
      "artPath": "/v2/art/nds/ASME.png"   // follow verbatim; append render params
    }
  ],
  "matched": 1
}
```

Every matched identity is remembered server-side, which is what lets a later art request resolve a
key that carries no information of its own.

**Clients should send a CRC32 before anything else.** ZIP central directories and 7z headers already
store the CRC32 of the *uncompressed* entry, so identifying a ROM costs a ~4 KiB tail read and no
decompression: 116-508 bytes are actually needed for a zip, and a 512-byte tail read covers 100% of a
real 14,076-archive corpus. That is ~54x faster than decompress-and-SHA-1.

One trap: **7z reports a CRC of `0` for both "absent" and "genuinely zero"**. Treat `0` as unknown and
fall through to the next rung of the ladder, or you will silently mis-identify ROMs.

### `GET /v2/art/{platform}/{key}.png`

| Param | Meaning | Default | Clamp |
|---|---|---|---|
| `w` | Width | 128 | 1-256 |
| `h` | Height | 115 | 1-192 |
| `ar` | Keep aspect ratio (`1`/`0`/`true`/`false`) | `true` | |
| `b` | Border style: `None`, `Line`, `NintendoDsi`, `Nintendo3Ds` (or its number) | `None` | |
| `bt` | Border thickness | 1 | 0-5 |
| `bc` | Border colour, `RRGGBB` or `AARRGGBB`, `#` optional | `FF000000` | |

```
GET /v2/art/nds/ASME.png?w=128&h=115&b=NintendoDsi&bt=2&bc=1a1a1a
```

Every returned PNG is **at most 45,056 bytes** (`0xB000`), quantized if it has to be. TWiLightMenu++
allocates its box art cache as 40 slots of exactly that size and *silently* ignores anything larger,
which produces bug reports nobody can explain. The server guarantees the ceiling so no client ever
has to rewrite the user's `settings.ini`.

Responses carry an `ETag` (the source art's hash) and `Cache-Control: public, max-age=86400`, and
honour `If-None-Match` with a `304`. The ETag changes when the art changes and is stable across
every size and border variant of the same source.

A miss is a **404**, never a 500.

### `GET /v2/art.png`

Identify and deliver in one `GET`, for a client that cannot afford the two-phase protocol: a DS
walking its SD card, or a quick `curl`. Query in, PNG bytes out, an empty 404 on a miss. The caller
sends raw facts about the file it holds (its name, its first bytes) and *nothing it had to
understand*: no console, no serial, no header offsets. The same identification ladder as
`POST /v2/identify` works the rest out server-side.

```
GET /v2/art.png?name=Super%20Mario%2064%20DS%20(USA).nds&header=<base64>&w=128&h=115&b=NintendoDsi
```

| Param | Meaning |
|---|---|
| `name` | The ROM's own file name, extension included |
| `header` | Base64 of the ROM's leading bytes (512 is plenty; over 1 KB is rejected, not truncated). Beats the name when they disagree: magic bytes do not lie about the console, and the serial inside skips the index entirely |
| `w` `h` `ar` `b` `bt` `bc` | Render parameters, exactly as the canonical route above |

At least one of `name` / `header` is required. A caller that already knows exactly what it wants,
a human with a console and a title id, uses the canonical route directly instead.

The bytes are served directly rather than redirected to the canonical URL, because a second round trip
hurts precisely the slow clients this exists for. The canonical, cacheable URL is advertised in the
`Content-Location` header so a capable client can use it next time.

A constrained client writes whatever it receives straight to the SD card, so an error *body* would
become a corrupt cover with no explanation. There is never a body on a non-200.

### `GET /v2/formats`

Which file extensions are worth opening. Plain text, one `key=csv` line per role:

```
rom=.a26,.a52,.a78,.agb,.app,.col,.ds,.dsi,.fds,.gb,.gba,.gbc,.gen,.gg,.ids,.int,.md,.mb,.min,.msx,.n64,.nds,.nes,.ngc,.ngp,.pce,.sc,.sfc,.sg,.sgb,.smc,.sms,.snes,.srl,.v64,.ws,.wsc,.z64
archive=.7z,.zip
```

The two roles are not cosmetic. They are different code paths, and different clients support
different subsets:

| Key | Meaning | Who uses it |
|---|---|---|
| `rom` | The file **is** a game. Probed directly; its extension also hints at the console | Everyone |
| `archive` | The file **contains** a game. Opened and read from its own header, never decompressed | Clients that can read containers |

The DSi homebrew has no archive support, so it reads `rom=` and ignores the rest; handing it one
merged list would send it into `.zip` files it cannot open. The desktop client reads both.

Clients **must** treat this as advisory and fall back to their built-in list if the call fails.
Being a release behind costs a cover; scanning nothing is a broken app. Unrecognised keys are
skipped rather than rejected, so new roles can be added without breaking older clients.

This endpoint exists because clients are the hardest thing here to update: a DSi binary is flashed
to a card once and kept for years, and every extension frozen into one goes stale *silently* the day
a console is added, as a game TWiLightMenu++ launches happily while the scanner walks past it.

## How a request is served

```
GET /v2/art/nds/ASME.png?w=128&h=115
      |
      v
  art record for (nds, ASME)?         ArtRecord row - identity, source hash, negative cache
      |  no
      v
  IArtSource ladder                   GameTDB (title id, exact) then libretro (name-based)
      |                               capped at 4 concurrent requests per upstream
      v
  originals cache                     content-addressed by SHA-256, large budget
      |
      v
  renders cache                       {platform}/{key}/{sourceSha}/{w}x{h}_ar_None_1_FF000000_45056.png
      |
      v
  PNG, <= 45,056 bytes
```

Identical concurrent requests are coalesced, so a hundred clients asking for the same popular title in
the same second produce **one** upstream fetch.

## Caching

Two layers, both on the data volume and deliberately **not** under `wwwroot`:

| Layer | Contents | Budget | Policy |
|---|---|---|---|
| `originals` | Upstream art exactly as downloaded | 4 GiB | Content-addressed, shared, stable |
| `renders` | Resized and bordered output | 256 MiB | LRU - about 5 ms to regenerate |

Every write is atomic: a unique temp file, then `File.Move(overwrite: true)`. A request that dies
mid-write can never leave a truncated PNG that later requests treat as valid forever.

Cache-hit accounting is buffered in a `ConcurrentDictionary` and merged in bulk every 30 seconds by a
background service. Eviction is a separate background sweep. Neither ever runs on the request path -
at 18,000 art requests per library scan, bookkeeping *is* the workload.

## Security notes

- Render parameters are clamped through `RenderOptions.Normalized()` before they can size a buffer.
- The cache key is the title, not the fingerprint. The key space is bounded by the number of games.
- Art keys are validated against an allowlist, not sanitised, because they become path segments.
- Rate limiting is per remote IP with the built-in `RateLimiter`: 60/min identify, 1800/min art,
  1200/min DS, with a 2000/min global backstop over everything (it must stay above every
  per-endpoint policy, or it silently becomes the real limit).
- CORS: `*` on `/v2` GETs (public data, no credentials), an origin allowlist on POSTs, preflights
  cached for 24 hours.
- `ForwardedHeaders` is **off** unless `Twilight__TrustedProxy` names a specific proxy IP. Rate
  limiting partitions on the client address, so an unrestricted forwarded header would let any caller
  pick its own bucket.
- `/v2` requires a static `X-Twilight-Key` header. A speed bump against scrapers, not authentication
  See "The API key" below for what it does and does not claim.
- No secrets anywhere: the service holds no credentials at all.

## Configuration

Almost nothing. The index file name, the render defaults, the upstream politeness limits and every
cache timing are **constants in the code**, next to the reasoning that produced them. In six years
nobody had a reason to set them to anything else, and offering the choice only invited getting it
wrong. `appsettings.json` holds log levels and nothing else.

What is left is a deployment fact rather than a preference, so it is an environment variable.
`TwilightSettings.Load` is the only place this server reads configuration, and the cache budgets are
read in `Program.cs`; between them that is the complete list:

| Setting | Default | Notes |
|---|---|---|
| `Twilight__Security__AdminPassword` | *(none)* | The admin panel fails closed (503) without it. Never put this in a file |
| `Twilight__Cache__OriginalsBudgetBytes` | 4 GiB | Disk space, clamped to 16 MiB to 1 TiB |
| `Twilight__Cache__RendersBudgetBytes` | 256 MiB | Disk space, clamped to 16 MiB to 1 TiB |
| `Twilight__TrustedProxy` | *(none)* | Enables `X-Forwarded-For`/`X-Forwarded-Proto` for exactly this address; anything unparseable is ignored and forwarded headers stay off |
| `Twilight__DataPath` | `/data` | Index, caches, art records. `./data` in Development |
| `Seq__ServerUrl` | *(none)* | Optional structured logging sink |

Two things that used to be settings now follow the environment instead, because they were only ever
set one way per environment anyway: the Vite dev server's origins are allowed automatically in
Development, and the first-boot index build is suppressed only under the `Testing` environment.

## The API key

Every `/v2` request must carry `X-Twilight-Key`, or it gets a bodiless 401. **This is not a secret**
The value is a constant in `ApiKey.cs`, compiled into the DSi homebrew and served to any browser
inside `api.js`. It buys one thing: a request without it was not written against this API, which
turns away drive-by scrapers and hotlinkers pointed at the art routes, and that traffic costs us
bandwidth from volunteer-run upstreams. Anything that genuinely must be unguessable (the admin
panel) uses a password from the environment.

Exempt on purpose: `POST /api` (v0.7 clients were compiled in 2020 and will never send a new header
(that route requires the fixed nine-field form shape those clients send instead), `/v2/health` (the
desktop client probes it to decide between backend and local mode, before it has done anything else),
and the admin routes (password-protected already).

## The index

`nointro.db` is a **generated file**, not a database the app migrates. The server builds its own:
on first boot when the file is absent, and again whenever the admin panel's "update No-Intro
index" button asks. The build downloads the public No-Intro / libretro DAT files (cached under
`/data/dat-cache`, so a rebuild re-fetches only what changed), parses and dedupes them, and writes
the index complete with an FTS5 trigram table for fuzzy name matching. The finished file is
swapped in atomically under a running server - no restart, no deployment step, no release asset.
The builder lives in `TwilightBoxart.Core.Index`, right next to the reader that shares its schema.

The finished file is self-describing: its `meta` table records attribution, the source URL
template, the version of every DAT that went in, and the build stamp. The DAT URL template is a
setting, so anyone wanting a byte-reproducible corpus can point it at a commit-pinned
`raw.githubusercontent.com/libretro/libretro-database/<commit>/...` URL instead of `master`.

While the index is missing or building, the service still starts, still serves `/v2/health`, and
still serves art for anything whose key is a title id. It reports `"status": "degraded"`.

## Notes for whoever picks this up

- `Extensions/CoreRegistration.cs` binds Core's implementations **by contract, not by type name**, so
  Core can add an art source or swap the index implementation without touching the web tier. A
  contract with no implementation is logged loudly at startup rather than crashing the host.
- Server-side mutable state lives in the EF SQLite database: art records and cache bookkeeping,
  reached through `ArtRecordStore` over `IDbContextFactory<AppDbContext>`. The only durable state
  kept outside EF is the cache blobs themselves (the PNGs on disk); their metadata is a table like
  everything else.
- Everything here assumes **one instance**. Single-flight coalescing, the negative cache and the cache
  entry tables are per-process. A second instance would need cross-instance locking - which is also
  the stated trigger for moving off SQLite.
