// app.js: orchestration and UI wiring: walk -> probe -> identify -> fetch art -> write. Anything
// already in the IndexedDB cache skips identify, so a second run over the same card is nearly free.

import * as api from './api.js';
import * as scan from './scan.js';
import * as store from './store.js';
import { crc32File } from './romprobe.js';
import { buildZip, downloadBlob } from './zipwriter.js';

const $ = (id) => document.getElementById(id);

/** Concurrent art downloads. Six matches what a browser will open per origin anyway. */
const ART_CONCURRENCY = 6;
/** Concurrent probes. These are local reads, so a little more parallelism pays. */
const PROBE_CONCURRENCY = 8;
/** TWiLightMenu++ silently ignores box art above this size. */
const TWILIGHT_MAX_PNG_BYTES = 0xB000;

const BOXART_PATH = '_nds/TWiLightMenu/boxart';

/** Where art goes, relative to the card root. Optional override, like the classic Set Manually. */
function boxartPath() {
    if (!$('dest-custom').checked) return BOXART_PATH;
    const cleaned = $('dest').value.replaceAll('\\', '/').split('/').map(s => s.trim())
        .filter(s => s && s !== '.' && s !== '..').join('/');
    return cleaned || BOXART_PATH;
}

const state = {
    /** 'write' with the File System Access API, 'readonly' with <input webkitdirectory>. */
    mode: null,
    root: null,
    fileList: null,
    items: [],
    running: false,
    abort: null,
    zipEntries: [],
    installPrompt: null,
};

const counters = { found: 0, probed: 0, identified: 0, written: 0, skipped: 0, missed: 0 };

/**
 * The missed counter moves only through these two, and each item remembers whether it is counted:
 * the retry path subtracts exactly the items that were counted, so the total cannot drift.
 */
function countMiss(item) {
    if (item.countedMissed) return;
    item.countedMissed = true;
    counters.missed++;
}

function uncountMiss(item) {
    if (!item.countedMissed) return;
    item.countedMissed = false;
    counters.missed--;
}

/* settings */

const SETTINGS_KEY = 'twilightboxart.settings';

/** The size presets of the classic app. Custom frees the width/height fields. */
const SIZE_PRESETS = { classic: [128, 115], large: [168, 130], xl: [208, 143] };

function readSettings() {
    const addBorder = $('border').checked;
    const choice = document.querySelector('input[name="borderstyle"]:checked')?.value ?? 'NintendoDsi';
    const line = choice === 'Black' || choice === 'White';
    return {
        width: clamp(+$('w').value || 128, 1, 256),
        height: clamp(+$('h').value || 115, 1, 192),
        keepAspectRatio: $('ar').checked,
        borderStyle: !addBorder ? 'None' : line ? 'Line' : choice,
        borderThickness: $('thick').checked ? 2 : 1,
        // RenderOptions.BorderColor is 0xAARRGGBB, always fully opaque here.
        borderColor: (choice === 'White' ? 0xFFFFFFFF : 0xFF000000) >>> 0,
        overwrite: $('overwrite').checked,
    };
}

/** Mirrors RenderOptions.CacheDiscriminator() so the two caches agree on what "same" means. */
const renderKey = (s) =>
    `${s.width}x${s.height}_${s.keepAspectRatio ? 'ar' : 'fill'}_${s.borderStyle}_${s.borderThickness}_${s.borderColor.toString(16).toUpperCase().padStart(8, '0')}`;

function saveSettings() {
    const s = { ...readSettings(), destCustom: $('dest-custom').checked, dest: $('dest').value };
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(s));
}

function restoreSettings() {
    // The stored shape predates the preset controls, so it maps back onto them: width/height pick
    // the size radio, style plus colour pick the border radio.
    try {
        const s = JSON.parse(localStorage.getItem(SETTINGS_KEY) || '{}');
        if (s.width) $('w').value = s.width;
        if (s.height) $('h').value = s.height;
        if (s.keepAspectRatio !== undefined) $('ar').checked = s.keepAspectRatio;
        if (s.borderStyle) {
            $('border').checked = s.borderStyle !== 'None';
            const white = (s.borderColor & 0xFFFFFF) === 0xFFFFFF;
            const radio = s.borderStyle === 'Nintendo3Ds' ? 'border-3ds'
                : s.borderStyle === 'Line' ? (white ? 'border-white' : 'border-black')
                    : 'border-dsi';
            $(radio).checked = true;
        }
        if (s.borderThickness !== undefined) $('thick').checked = s.borderThickness >= 2;
        if (s.overwrite !== undefined) $('overwrite').checked = s.overwrite;
        if (s.destCustom !== undefined) $('dest-custom').checked = s.destCustom;
        if (s.dest) $('dest').value = s.dest;
    } catch { /* corrupt settings are not worth a broken page */ }

    const preset = Object.entries(SIZE_PRESETS).find(([, [w, h]]) => +$('w').value === w && +$('h').value === h);
    $(preset ? 'size-' + preset[0] : 'size-custom').checked = true;
    syncSettingsUx();
}

/** Grays out what the current choices make irrelevant, like the classic app did. */
function syncSettingsUx() {
    const custom = $('size-custom').checked;
    $('w').disabled = $('h').disabled = !custom;
    const border = $('border').checked;
    for (const el of document.querySelectorAll('input[name="borderstyle"]')) el.disabled = !border;
    $('thick').disabled = !border;
    const customDest = $('dest-custom').checked;
    $('dest').disabled = !customDest;
    if (!customDest) $('dest').value = BOXART_PATH;
}

const clamp = (v, lo, hi) => Math.min(Math.max(v, lo), hi);

/* UI plumbing */

let paintQueued = false;
/**
 * Throttled counter repaint. Deliberately setTimeout and not requestAnimationFrame: rAF stops
 * firing entirely in a background tab, which would freeze the progress display (including the
 * final totals) for anyone who starts a long scan and switches away.
 */
function paint() {
    if (paintQueued) return;
    paintQueued = true;
    setTimeout(() => {
        paintQueued = false;
        for (const [k, v] of Object.entries(counters)) {
            const el = $('c-' + k);
            if (el) el.textContent = v.toLocaleString();
        }
        // Reading the card fills the first half of the bar, fetching covers the second, so the
        // long first scan visibly moves from the start instead of sitting at zero.
        const total = counters.found || 1;
        const read = Math.min(1, counters.probed / total);
        const done = Math.min(1, (counters.written + counters.skipped + counters.missed) / total);
        $('bar-fill').style.width = `${(read * 50 + done * 50).toFixed(1)}%`;
    }, 120);
}

const MAX_LOG_LINES = 400;
function log(message, kind = '') {
    const el = $('log');
    const line = document.createElement('div');
    line.className = 'log-line' + (kind ? ' ' + kind : '');
    line.textContent = message;
    el.appendChild(line);
    while (el.childElementCount > MAX_LOG_LINES) el.firstElementChild.remove();
    el.scrollTop = el.scrollHeight;
}

function setStatus(message, kind = '') {
    const el = $('card-status');
    el.textContent = message;
    el.className = 'status ' + kind;
}

function renderMisses() {
    const missed = state.items.filter(i => i.status === 'missed' || i.status === 'error');
    $('misses').hidden = missed.length === 0;
    $('miss-count').textContent = missed.length ? `(${missed.length})` : '';
    const body = $('miss-body');
    body.replaceChildren();
    for (const item of missed.slice(0, 200)) {
        const tr = document.createElement('tr');
        const name = document.createElement('td');
        name.textContent = item.probe?.innerName ?? item.fileName;
        name.title = item.path;
        const why = document.createElement('td');
        why.textContent = item.reason ?? 'no reason recorded';
        tr.append(name, why);
        body.appendChild(tr);
    }
    $('miss-note').textContent = missed.length > 200 ? `Showing the first 200 of ${missed.length}.` : '';
    $('retry').hidden = missed.length === 0 || state.running;
}

/* picking the card */

const supportsFileSystemAccess = 'showDirectoryPicker' in window;

async function pickRoot() {
    try {
        const root = await window.showDirectoryPicker({ id: 'twilightboxart-sd', mode: 'readwrite' });
        await store.saveRoot(root);
        await useRoot(root);
    } catch (e) {
        if (e.name !== 'AbortError') setStatus(`Could not open that folder: ${e.message}`, 'bad');
    }
}

async function useRoot(root) {
    state.mode = 'write';
    state.root = root;
    $('reconnect').hidden = true;
    // Confirm the card is still the card. Removable media reappears under different drive
    // letters, and writing 18,000 PNGs onto the wrong volume is not a recoverable mistake.
    const sentinel = await scan.hasTwilightSentinel(root);
    if (sentinel) setStatus(`Ready: "${root.name}" looks like a TWiLightMenu++ card.`, 'good');
    else setStatus(`"${root.name}" doesn't look like a TWiLightMenu++ card. Pick the card itself, not a folder inside it.`, 'warn');
    $('start').disabled = false;
    $('card-name').textContent = root.name;
    refreshCacheNote();
}

async function restoreRoot() {
    const saved = await store.getSavedRoot().catch(() => null);
    if (!saved) return;
    const permission = await saved.queryPermission({ mode: 'readwrite' });
    if (permission === 'granted') { await useRoot(saved); return; }
    // requestPermission() must run inside a user gesture, so offer a button rather than nagging.
    $('reconnect-name').textContent = saved.name;
    $('reconnect').hidden = false;
    setStatus('Your card from last time is remembered; the browser just needs you to reconnect it.', 'warn');
    $('reconnect').onclick = async () => {
        const granted = await saved.requestPermission({ mode: 'readwrite' });
        if (granted === 'granted') await useRoot(saved);
        else setStatus('The browser said no. Pick your card again to continue.', 'bad');
    };
}

function useFileList(files) {
    state.mode = 'readonly';
    state.fileList = files;
    state.root = null;
    $('reconnect').hidden = true;
    const sentinel = scan.fileListHasSentinel(files);
    setStatus(sentinel
        ? `Reading ${files.length.toLocaleString()} files. This looks like a TWiLightMenu++ card.`
        : `Reading ${files.length.toLocaleString()} files. This doesn't look like a TWiLightMenu++ card.`,
        sentinel ? 'good' : 'warn');
    $('start').disabled = false;
    $('card-name').textContent = files[0]?.webkitRelativePath.split('/')[0] ?? '';
    refreshCacheNote();
}

/* the pipeline */

/** Runs `fn` over `items` with at most `limit` in flight. Stops promptly when aborted. */
async function pool(items, limit, fn, signal) {
    let next = 0;
    const worker = async () => {
        while (next < items.length && !signal.aborted) {
            const item = items[next++];
            try { await fn(item); }
            catch (e) {
                if (e.name === 'AbortError') return;
                item.status = 'error';
                item.reason = e.message;
                countMiss(item);
                log(`${item.fileName}: ${e.message}`, 'bad');
                paint();
            }
        }
    };
    await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
}

async function collectFiles(signal) {
    const found = [];
    if (state.mode === 'readonly') {
        for (const f of scan.walkFileList(state.fileList)) found.push(f);
    } else {
        const onError = (path, e) => log(`Skipped ${path}: ${e.message}`, 'warn');
        for await (const f of scan.walkDirectory(state.root, '', onError)) {
            if (signal.aborted) break;
            found.push(f);
            if (found.length % 250 === 0) { counters.found = found.length; paint(); await tick(); }
        }
    }
    counters.found = found.length;
    paint();
    return found;
}

const tick = () => new Promise(r => setTimeout(r, 0));

async function probeAll(found, signal) {
    const items = found.map((f, i) => ({
        tag: String(i), path: f.path, fileName: f.name, getFile: f.getFile,
        status: 'pending', reason: null, probe: null, identity: null,
    }));

    await pool(items, PROBE_CONCURRENCY, async (item) => {
        const file = await item.getFile();
        const probe = await scan.probeFile(file, item.fileName, false);
        counters.probed++;
        if (!probe.ok) {
            item.status = 'missed';
            item.reason = probe.reason;
            countMiss(item);
        } else {
            item.probe = probe;
            item.contentKey = store.contentKey(probe);
        }
        if (counters.probed % 100 === 0) paint();
    }, signal);

    paint();
    return items;
}

/** Give files that could not be read a second chance, keeping the reason if they still cannot. */
async function reprobeAll(items, signal) {
    if (!items.length) return;
    await pool(items, PROBE_CONCURRENCY, async (item) => {
        const file = await item.getFile();
        const probe = await scan.probeFile(file, item.fileName, false);
        if (probe.ok) { item.probe = probe; item.contentKey = store.contentKey(probe); }
        else { item.status = 'missed'; item.reason = probe.reason; countMiss(item); }
    }, signal);
    paint();
}

/**
 * Identify everything we do not already have a cached answer for.
 *
 * Two passes: the first sends only what was free (a CRC32, or the header a loose ROM gave us),
 * the second re-reads the misses with a real 512-byte ROM header. That is the two-phase
 * negotiation: 22 bytes per ROM for the common case.
 */
async function identifyAll(items, signal) {
    const pending = items.filter(i => i.probe);
    const cached = await store.loadIdentities([...new Set(pending.map(i => i.contentKey))]);

    const needed = [];
    for (const item of pending) {
        // Only a POSITIVE entry short-circuits. Builds before this one cached misses too, and those
        // entries are still in users' IndexedDB, so an entry carrying no identity is treated as a
        // cache miss rather than replayed. Without this an existing cache stays poisoned forever.
        const hit = cached.get(item.contentKey);
        if (hit?.identity) { applyIdentity(item, hit.identity, hit.reason); continue; }
        needed.push(item);
    }
    if (cached.size) log(`Remembered ${cached.size.toLocaleString()} games from last time.`);

    await identifyPass(needed, signal);

    // Escalate: anything still unidentified that we can cheaply get a ROM header for.
    const escalate = needed.filter(i => i.status === 'missed' && !i.probe.header && i.probe.container !== 'loose');
    if (escalate.length && !signal.aborted) {
        log(`Taking a closer look at ${plural(escalate.length, 'stubborn file')}.`);
        await pool(escalate, PROBE_CONCURRENCY, async (item) => {
            const file = await item.getFile();
            const probe = await scan.probeFile(file, item.fileName, true);
            if (probe.ok && probe.header) {
                item.probe = probe;
                item.contentKey = store.contentKey(probe);   // a header changes the content key
                item.status = 'pending';
                uncountMiss(item);
            }
        }, signal);
        await identifyPass(escalate.filter(i => i.status === 'pending'), signal);
    }

    // Cache what we FOUND, never what we failed to find.
    //
    // A match is a durable fact: that CRC32 is that game forever. A miss is not. It can mean the
    // index did not carry the title yet, an upstream was briefly down, the art had not been fetched,
    // or the escalation pass was cut short. Caching misses froze whichever of those happened to be
    // true at that moment, permanently: `saveIdentities` stamps an `at` that nothing ever reads and
    // `getMany` applies no expiry, so every later scan replayed the miss WITHOUT asking the server.
    //
    // Worse, a replayed miss never reached the escalation pass either, because that pass is computed
    // from `needed` and a cache hit never gets there. So the one thing that would have fixed it - a
    // second look with a real ROM header - was locked out by the record of it having failed once.
    // "Retry misses" bypasses this cache deliberately (see run()), which is why retrying was the only
    // thing that ever worked.
    const toCache = needed
        .filter(i => i.status === 'identified' && i.identity)
        .map(i => [i.contentKey, { identity: i.identity, reason: i.reason }]);
    await store.saveIdentities(toCache).catch(e => log(`Could not write the cache: ${e.message}`, 'warn'));
}

async function identifyPass(items, signal) {
    for (let i = 0; i < items.length && !signal.aborted; i += api.IDENTIFY_CHUNK) {
        const chunk = items.slice(i, i + api.IDENTIFY_CHUNK);
        const fingerprints = chunk.map(item => api.fingerprint({ ...item.probe, tag: item.tag }));
        let results;
        try {
            results = await api.identifyBatch(fingerprints, signal);
        } catch (e) {
            if (e.name === 'AbortError') return;
            log(`Identify failed for ${chunk.length} games: ${e.message}`, 'bad');
            for (const item of chunk) { item.status = 'error'; item.reason = `identify failed: ${e.message}`; countMiss(item); }
            paint();
            continue;
        }
        const byTag = new Map(results.map(r => [String(r.tag ?? ''), r]));
        for (const item of chunk) applyIdentity(item, byTag.get(item.tag), null);
        paint();
    }
}

function applyIdentity(item, identity, cachedReason) {
    if (identity && api.isMatched(identity)) {
        // Retries re-identify ROMs that were already identified but whose art 404'd; only the
        // first success counts, or the totals drift upward every time.
        if (!item.identity) counters.identified++;
        item.identity = identity;
        item.status = 'identified';
        return;
    }
    item.status = 'missed';
    item.reason = cachedReason ?? missReason(item);
    countMiss(item);
}

/** Says WHY, not just that. A user who knows the reason can actually do something about it. */
function missReason(item) {
    const p = item.probe;
    if (!p) return 'could not be read';
    if (p.container === '7z' && p.encodedHeader) return 'this .7z cannot be opened in a browser; re-zip it or use the desktop app';
    if (p.crc32 == null && !p.header) return 'the file could not be recognised';
    if (p.crc32 != null && !p.header) return 'not in the game database (romhacks and translations usually are not)';
    return 'not in the game database';
}

/**
 * The opt-in expensive path: checksum the loose files the first pass skipped for size. The scan
 * already hashes loose ROMs up to 64 MiB (see probeFile), so this only ever touches oversized
 * dumps that also failed every other rung.
 */
async function deepen(items, signal) {
    // Called only from the retry path, where everything has already been reset to 'pending'.
    const targets = items.filter(i => i.probe?.container === 'loose' && i.probe.crc32 == null);
    if (!targets.length) return;
    const bytes = targets.reduce((a, i) => a + i.probe.size, 0);
    log(`Taking a deep look at ${plural(targets.length, 'big file')} (${formatBytes(bytes)}); this can take a while.`);

    let done = 0;
    // Deliberately serial: this is disk-bound, and parallel full-file reads only thrash the card.
    for (const item of targets) {
        if (signal.aborted) break;
        const file = await item.getFile();
        item.probe = { ...item.probe, crc32: await crc32File(file, null, signal) };
        item.contentKey = store.contentKey(item.probe);
        if (++done % 10 === 0) log(`Checked ${done} of ${targets.length}…`);
    }
}

/* art */

/**
 * The PNG's name is the ROM's name, never the archive's. When the inner entry is not recognisably
 * a ROM (No-Intro's DSiWare blobs are named things like `00000000`), the file on the card is the
 * archive, so that is what TWiLightMenu will look the art up by.
 */
function outputName(item) {
    const inner = item.probe?.innerName;
    const base = inner && scan.isRom(inner) ? inner : item.fileName;
    return scan.safeFileName(base) + '.png';
}

async function deliverArt(items, settings, signal) {
    const key = renderKey(settings);
    const matched = items.filter(i => i.status === 'identified');
    if (!matched.length) return;

    // A custom destination salts the "already written" records so the default folder's history
    // cannot skip writes into a folder that never received them.
    const path = boxartPath();
    const writeKey = path === BOXART_PATH ? key : `${key}@${path}`;

    let boxart = null;
    if (state.mode === 'write') {
        boxart = await scan.boxartDirectory(state.root, path);
        log(`Writing into ${path}/. Nothing else on the card is touched.`);
    }

    const writtenCache = boxart
        ? await store.loadWritten(matched.map(i => store.writtenKey(i.contentKey, writeKey)))
        : new Map();
    const seen = new Set();
    const freshlyWritten = [];

    await pool(matched, ART_CONCURRENCY, async (item) => {
        item.outName = outputName(item);
        const skip = () => { item.status = 'skipped'; counters.skipped++; paint(); };

        // Two ROMs in different folders can share a name; download the art once.
        if (seen.has(item.outName)) { skip(); return; }
        seen.add(item.outName);

        // The "already downloaded" record only means anything when there was a card to write to.
        // In read-only mode skipping would quietly hand the user an empty .zip.
        if (boxart && !settings.overwrite) {
            if (writtenCache.has(store.writtenKey(item.contentKey, writeKey))) { skip(); return; }
            if (await scan.fileExists(boxart, item.outName)) { skip(); return; }
        }

        const png = await api.fetchArt(item.identity, settings, signal);
        if (!png) {
            item.status = 'missed';
            item.reason = `recognised as ${item.identity.canonicalName ?? item.identity.key} (${api.platformLabel(item.identity)}), but no cover exists for it yet`;
            countMiss(item); paint(); return;
        }

        // The backend is supposed to guarantee this ceiling. If it ever does not, TWiLightMenu
        // drops the image silently and the user gets no explanation at all, so say it here.
        if (png.length > TWILIGHT_MAX_PNG_BYTES) {
            log(`${item.outName} is ${formatBytes(png.length)}; TWiLightMenu ignores box art over ${formatBytes(TWILIGHT_MAX_PNG_BYTES)}.`, 'warn');
        }

        if (boxart) await scan.writePng(boxart, item.outName, png);
        else state.zipEntries.push([`${path}/${item.outName}`, png]);

        item.status = 'written';
        counters.written++;
        freshlyWritten.push([store.writtenKey(item.contentKey, writeKey), { name: item.outName }]);
        paint();
    }, signal);

    await store.saveWritten(freshlyWritten).catch(e => log(`Could not write the cache: ${e.message}`, 'warn'));
}

/* run control */

async function run({ retryOnly = false, deep = false } = {}) {
    if (state.running) return;
    state.running = true;
    state.abort = new AbortController();
    const signal = state.abort.signal;
    const settings = readSettings();
    saveSettings();

    $('start').disabled = true;
    $('cancel').hidden = false;
    $('retry').hidden = true;
    const started = performance.now();

    try {
        let items;
        if (retryOnly) {
            items = state.items.filter(i => i.status === 'missed' || i.status === 'error');
            for (const i of items) { uncountMiss(i); i.status = 'pending'; i.reason = null; }
            log(`Retrying ${plural(items.length, 'miss', 'misses')}.`);
            // Files we never managed to read get another look: a failed probe can be a
            // transient read error, and one that is not must reappear in the misses list rather
            // than quietly vanishing from it.
            await reprobeAll(items.filter(i => !i.probe), signal);
            if (deep) await deepen(items, signal);
            // A retry deliberately ignores the identity cache: it is what we are retrying.
            await identifyPass(items.filter(i => i.probe && i.status === 'pending'), signal);
        } else {
            Object.keys(counters).forEach(k => counters[k] = 0);
            state.zipEntries = [];
            $('zip-wrap').hidden = true;
            log('Looking for games…');
            const found = await collectFiles(signal);
            log(`Found ${found.length.toLocaleString()} games. Reading them…`);
            items = await probeAll(found, signal);
            state.items = items;
            await identifyAll(items, signal);
        }

        log(`Recognised ${counters.identified.toLocaleString()} games, ${counters.missed.toLocaleString()} without a match.`);
        // Do not create the boxart folder on a run the user stopped before it found anything.
        if (!signal.aborted) await deliverArt(items, settings, signal);

        if (signal.aborted) log('Stopped.', 'warn');
        else log(`Done in ${((performance.now() - started) / 1000).toFixed(1)}s: `
            + `${counters.written.toLocaleString()} ${state.mode === 'write' ? 'new covers' : 'ready to download'}, `
            + `${counters.skipped.toLocaleString()} already there, `
            + `${counters.missed.toLocaleString()} without art.`, 'good');

        if (state.mode === 'readonly' && state.zipEntries.length) {
            $('zip-wrap').hidden = false;
            $('zipbtn').textContent = `Download ${state.zipEntries.length.toLocaleString()} images as a .zip`;
        }
    } catch (e) {
        log(`Run failed: ${e.message}`, 'bad');
    } finally {
        state.running = false;
        $('start').disabled = false;
        $('cancel').hidden = true;
        renderMisses();
        refreshCacheNote();
        paint();
    }
}

const plural = (n, one, many = one + 's') => `${n.toLocaleString()} ${n === 1 ? one : many}`;

function formatBytes(n) {
    if (n < 1024) return `${n} B`;
    if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
    if (n < 1024 ** 3) return `${(n / 1024 ** 2).toFixed(1)} MB`;
    return `${(n / 1024 ** 3).toFixed(2)} GB`;
}

async function refreshCacheNote() {
    try {
        const { identities, written } = await store.cacheStats();
        $('cache-note').textContent = identities || written
            ? `Remembering ${plural(identities, 'game')} from earlier scans.`
            : '';
    } catch { /* private browsing can refuse IndexedDB; the app still works */ }
}

/* startup */

function wireUp() {
    restoreSettings();
    for (const [name, [w, h]] of Object.entries(SIZE_PRESETS)) {
        $('size-' + name).addEventListener('change', () => { $('w').value = w; $('h').value = h; });
    }
    for (const id of ['size-classic', 'size-large', 'size-xl', 'size-custom', 'w', 'h', 'ar',
        'border', 'border-dsi', 'border-3ds', 'border-black', 'border-white', 'thick', 'overwrite',
        'dest', 'dest-custom']) {
        $(id).addEventListener('change', () => { syncSettingsUx(); saveSettings(); });
    }

    $('pick').onclick = pickRoot;
    $('start').onclick = () => run();
    $('retry').onclick = () => run({ retryOnly: true, deep: true });
    $('cancel').onclick = () => { state.abort?.abort(); log('Stopping…', 'warn'); };
    $('dirinput').onchange = (e) => { if (e.target.files.length) useFileList(e.target.files); };
    $('zipbtn').onclick = () => downloadBlob(buildZip(state.zipEntries), 'twilightboxart.zip');
    $('clear-cache').onclick = async () => {
        await store.clearCache();
        log('Forgot the earlier scans. Nothing on your card changed.');
        refreshCacheNote();
    };

    if (supportsFileSystemAccess) {
        restoreRoot();
    } else {
        $('unsupported').hidden = false;
        $('pick').hidden = true;
        $('fallback-pick').hidden = false;
        setStatus('This browser cannot save onto your card; you will get a .zip to copy over instead.', 'warn');
    }

    // PWA install. An installed app keeps its folder permission across launches; a plain tab
    // loses it, and Chrome stops offering the persistent prompt after three dismissals.
    window.addEventListener('beforeinstallprompt', (e) => {
        e.preventDefault();
        state.installPrompt = e;
        $('install').hidden = false;
    });
    $('install').onclick = async () => {
        $('install').hidden = true;
        await state.installPrompt?.prompt();
        state.installPrompt = null;
    };
    window.addEventListener('appinstalled', () => { $('install').hidden = true; });

    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.register('sw.js').catch(() => { /* offline support is a bonus */ });
    }

    refreshCacheNote();
    paint();
}

wireUp();
