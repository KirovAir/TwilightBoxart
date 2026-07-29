# TwilightBoxart browser client, for maintainers

The user-facing story (which browsers work, the read-only fallback, what the page does to a card)
lives in `../Pages/Support.cshtml`, linked from the app's footer. This file is the developer side only.

Plain ES modules. No build step, no bundler, no dependencies. Edit a file, reload the page.

The three pages live in `../Pages` and are Razor for one reason: `asp-append-version` stamps every link to a file here
with a hash of its bytes, so a deploy cannot be served from a stale cache. The JS files import each other by relative
path, which that tag helper never sees, so `Index.cshtml`
also emits an import map covering them. **Add a module and it needs a line in that map**, or it will be the one file a
browser keeps serving from four-hour-old cache.

| File                      | Job                                                              |
|---------------------------|------------------------------------------------------------------|
| `../Pages/Index.cshtml`   | The entire UI                                                    |
| `../Pages/Support.cshtml` | Browser support notes, served to users                           |
| `../Pages/Admin.cshtml`   | The operator panel behind `/admin.html`                          |
| `app.js`                  | The pipeline: walk, probe, identify, fetch, write                |
| `scan.js`                 | Which files are ROMs, walking the card, writing PNGs             |
| `romprobe.js`             | ZIP and 7z parsing                                               |
| `api.js`                  | The only file that knows the backend's wire format               |
| `store.js`                | IndexedDB: the folder handle and the content-keyed caches        |
| `zipwriter.js`            | Store-only ZIP writer for the read-only fallback                 |
| `sw.js`                   | Registration only, so the install prompt appears. Caches nothing |

Nothing here is kept in step with the backend by hand. `Index.cshtml` renders the scan set and the console labels from
`SupportedFiles` into a `<script type="application/json" id="formats">`, which
`scan.js` reads for `ROM_EXTENSIONS` / `ARCHIVE_EXTENSIONS` and `api.js` for `PLATFORM_LABELS`. Both were hand-copied
until 2.2 and both had drifted, the extension list by nineteen entries. **A module that needs backend facts should take
them from that element**, not restate them.

Art URLs are never built here: identify returns each match's `artPath` and `api.js` follows it verbatim, so the server
owns its own URL scheme.

There is no shipped test harness for `romprobe.js` and `scan.js`; they were validated against a large corpus of real
archives during the port. After touching either, exercise them by scanning a folder of real `.zip`/`.7z`/loose ROMs and
checking the identify results. The archive parsing is the risky part, not the DOM wiring.

The icons (`icon.png`, `icon-192.png`, `icon-512.png`, `apple-touch-icon.png`) are resized from the master logo at
`docs/logo.png` with ImageMagick: Lanczos resize to fit, a light unsharp, then
`-extent` onto the square canvas (opaque `#EDF3FA` for the apple-touch icon only).
