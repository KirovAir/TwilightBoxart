# TwilightBoxart browser client, for maintainers

The user-facing story (which browsers work, the read-only fallback, what the page does to a card)
lives in `support.html`, linked from the app's footer. This file is the developer side only.

Plain ES modules. No build step, no bundler, no dependencies. Edit a file, reload the page.

| File | Job |
|---|---|
| `index.html` | The entire UI |
| `support.html` | Browser support notes, served to users |
| `app.js` | The pipeline: walk, probe, identify, fetch, write |
| `scan.js` | Which files are ROMs, walking the card, writing PNGs |
| `romprobe.js` | ZIP and 7z parsing |
| `api.js` | The only file that knows the backend's wire format |
| `store.js` | IndexedDB: the folder handle and the content-keyed caches |
| `zipwriter.js` | Store-only ZIP writer for the read-only fallback |
| `sw.js` | Shell caching. Bump `CACHE` after editing any shell file |

One list must be kept in step with the backend by hand:

- `ROM_EXTENSIONS` / `ARCHIVE_EXTENSIONS` in `scan.js` mirror `SupportedFiles` in
  `TwilightBoxart.Core/Probe/ProbeContracts.cs`.

Art URLs are never built here: identify returns each match's `artPath` and `api.js` follows it
verbatim, so the server owns its own URL scheme.

There is no shipped test harness for `romprobe.js` and `scan.js`; they were validated against a
large corpus of real archives during the port. After touching either, exercise them by scanning a
folder of real `.zip`/`.7z`/loose ROMs and checking the identify results. The archive parsing is
the risky part, not the DOM wiring.

The icons (`icon.png`, `icon-192.png`, `icon-512.png`, `apple-touch-icon.png`) are resized from
the master logo at `docs/logo.png` with ImageMagick: Lanczos resize to fit, a light unsharp, then
`-extent` onto the square canvas (opaque `#EDF3FA` for the apple-touch icon only).
