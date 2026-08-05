// custom.js: the "add your own cover" modal. Three ways in (a miss row's button, a drop, the
// footnote under the counters), one flow out: pick the game if it is not known yet, preview the
// server's render of the image next to the original, confirm. app.js owns what happens to the
// confirmed bytes; this module owns the dialog.

import * as api from './api.js';
import * as store from './store.js';

const $ = (id) => document.getElementById(id);

/** The server's upload ceiling, from the same element every other backend fact comes from. */
const UPLOAD_MAX = JSON.parse(document.getElementById('formats').textContent).upload ?? 8 * 1024 * 1024;

/** Most games shown in the picker at once; typing narrows it, and a scroll of 25 is already noise. */
const PICKER_LIMIT = 25;

let ctx = null;
let current = null;
/** Stamps each preview render; a stale response must never overwrite a newer one. */
let renderSeq = 0;
let srcUrl = null;
let outUrl = null;

/**
 * Wire the dialog once. `context` is the seam to app.js:
 * items() and settings() read live state, onUse(item, file, rendered, options, fit) delivers a
 * confirmed cover, log/formatBytes reuse the app's own.
 */
export function init(context) {
    ctx = context;

    $('custom-file').addEventListener('change', (e) => {
        const file = e.target.files[0];
        e.target.value = '';
        if (!file || !current) return;
        current.file = file;
        if ($('custom-modal').open) showPreview();
        else openModal('preview');
    });

    $('custom-search').addEventListener('input', () => renderGameList($('custom-search').value));
    for (const id of ['custom-fit', 'custom-fill']) {
        $(id).addEventListener('change', () => renderPreview());
    }

    $('custom-use').addEventListener('click', async () => {
        if (!current?.rendered) return;
        $('custom-use').disabled = true;
        try {
            await ctx.onUse(current.item, current.file, current.rendered, current.settings,
                $('custom-fit').checked ? 'ar' : 'fill');
            $('custom-modal').close();
        } catch (e) {
            showError(`Could not save the cover: ${e.message}`);
            $('custom-use').disabled = false;
        }
    });

    // Native <dialog>: Esc already cancels; a click on the element itself is the backdrop.
    $('custom-modal').addEventListener('click', (e) => {
        if (e.target === $('custom-modal')) $('custom-modal').close();
    });
    $('custom-modal').addEventListener('close', () => {
        current = null;
        renderSeq++;
        srcUrl = revoke(srcUrl);
        outUrl = revoke(outUrl);
    });
}

/** From a miss row: the game is known. Without a file the OS picker opens first, then the dialog. */
export function openForItem(item, file = null) {
    current = {item, file, rendered: null, settings: null};
    if (!file) {
        $('custom-file').click();
        return;
    }
    openModal('preview');
}

/** From a drop anywhere on the page: the image is known, the game is not. */
export function openWithFile(file) {
    current = {item: null, file, rendered: null, settings: null};
    openModal('pick');
}

/** From the footnote: nothing is known yet. Game first, then the OS picker. */
export function openPicker() {
    current = {item: null, file: null, rendered: null, settings: null};
    openModal('pick');
}

function openModal(step) {
    $('custom-pick').hidden = step !== 'pick';
    $('custom-preview').hidden = step !== 'preview';
    if (step === 'pick') {
        $('custom-title').textContent = 'Add your own cover';
        $('custom-search').value = '';
        renderGameList('');
    }
    $('custom-modal').showModal();
    if (step === 'pick') $('custom-search').focus();
    else showPreview();
}

/* the game picker */

const displayName = (item) => item.probe?.innerName ?? item.fileName;

/** What the cover situation is, in the user's terms; the picker is not a status console. */
function pickerHint(item) {
    if (item.customResolved) return 'your cover';
    if (item.status === 'written' || item.status === 'skipped') return 'has a cover';
    return item.identity ? 'no cover yet' : 'no match';
}

function renderGameList(query) {
    const q = query.trim().toLowerCase();
    // Games without art first: they are who this feature exists for.
    const eligible = ctx.items().filter(i => i.contentKey)
        .filter(i => !q || displayName(i).toLowerCase().includes(q));
    eligible.sort((a, b) =>
        (a.status === 'missed' || a.status === 'error' ? 0 : 1)
        - (b.status === 'missed' || b.status === 'error' ? 0 : 1));

    const list = $('custom-games');
    list.replaceChildren();
    for (const item of eligible.slice(0, PICKER_LIMIT)) {
        const li = document.createElement('li');
        const button = document.createElement('button');
        button.type = 'button';
        const name = document.createElement('span');
        name.textContent = displayName(item);
        const hint = document.createElement('em');
        hint.textContent = pickerHint(item);
        button.append(name, hint);
        button.onclick = () => {
            current.item = item;
            if (current.file) showPreview();
            else $('custom-file').click();
        };
        li.appendChild(button);
        list.appendChild(li);
    }
    $('custom-more').textContent = eligible.length > PICKER_LIMIT
        ? `Showing ${PICKER_LIMIT} of ${eligible.length.toLocaleString()}; keep typing to narrow it down.`
        : (eligible.length ? '' : 'No scanned game matches that.');
}

/* the preview */

async function showPreview() {
    const item = current.item;
    $('custom-pick').hidden = true;
    $('custom-preview').hidden = false;
    $('custom-title').textContent = `Cover for ${displayName(item)}`;

    // Replacing existing art deserves a plain word; filling a gap needs none.
    $('custom-replaces').hidden =
        !(item.status === 'written' || item.status === 'skipped') || item.customResolved;

    srcUrl = revoke(srcUrl);
    srcUrl = URL.createObjectURL(current.file);
    $('custom-src').src = srcUrl;

    // An earlier choice for this game sets the fit it was saved with; otherwise follow step 2.
    const saved = await store.getCustomArt(item.contentKey).catch(() => null);
    const ar = saved?.fit ? saved.fit === 'ar' : ctx.settings().keepAspectRatio;
    $(ar ? 'custom-fit' : 'custom-fill').checked = true;

    await renderPreview();
}

async function renderPreview() {
    if (!current?.file || !current.item) return;
    const seq = ++renderSeq;
    current.rendered = null;
    $('custom-use').disabled = true;
    showError('');

    if (current.file.size > UPLOAD_MAX) {
        showError(`That file is ${ctx.formatBytes(current.file.size)}; `
            + `the most that can be converted is ${ctx.formatBytes(UPLOAD_MAX)}.`);
        return;
    }

    $('custom-meta').textContent = 'Converting…';
    const settings = {...ctx.settings(), keepAspectRatio: $('custom-fit').checked};
    let bytes;
    try {
        bytes = await api.renderCustom(current.file, settings, null);
    } catch (e) {
        if (seq === renderSeq) showError(`Could not convert the image: ${e.message}`);
        return;
    }
    if (seq !== renderSeq) return;
    if (!bytes) {
        showError('That file does not seem to be an image this can use. A PNG, JPG, GIF, BMP or WebP works.');
        return;
    }

    current.rendered = bytes;
    current.settings = settings;
    outUrl = revoke(outUrl);
    outUrl = URL.createObjectURL(new Blob([bytes], {type: settings.target === 'pico' ? 'image/bmp' : 'image/png'}));
    const out = $('custom-out');
    out.onload = () => {
        // Twice the real pixels: 128x115 is a postage stamp on a desktop screen, and the launcher
        // scales up anyway. Pixelated, so the preview shows the pixels the DS will actually get.
        out.style.width = `${out.naturalWidth * 2}px`;
        $('custom-meta').textContent =
            `${out.naturalWidth} × ${out.naturalHeight} · ${ctx.formatBytes(bytes.length)} · fits ✓`;
        $('custom-use').disabled = false;
    };
    out.src = outUrl;
}

function showError(message) {
    $('custom-error').textContent = message;
    $('custom-error').hidden = !message;
}

function revoke(url) {
    if (url) URL.revokeObjectURL(url);
    return null;
}
