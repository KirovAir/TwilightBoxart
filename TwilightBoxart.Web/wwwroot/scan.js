// scan.js: what counts as a ROM, walking the card (File System Access handles or a
// webkitdirectory FileList, both normalised to { name, path, getFile() }), and probing each one.

import { probeZip, probe7z, zipEntryHeader, sevenZipEntryHeader, crc32File, CONST } from './romprobe.js';

/**
 * Bare ROM extensions; mirrors SupportedFiles.Rom in TwilightBoxart.Core/Probe/ProbeContracts.cs.
 * Keep the two lists in step.
 */
const ROM_EXTENSIONS = new Set([
    '.nes', '.fds', '.sfc', '.smc', '.snes', '.gb', '.sgb', '.gbc', '.gba',
    '.nds', '.ds', '.dsi', '.n64', '.z64', '.v64', '.gg', '.gen', '.md', '.sms',
]);

/** Archive containers we can look inside. Mirrors SupportedFiles.Archive. */
const ARCHIVE_EXTENSIONS = new Set(['.zip', '.7z']);

/**
 * Directories that never hold ROMs. `_nds` is skipped because it holds TWiLightMenu's own data,
 * including the boxart folder this tool writes to, which we must not read back in as input.
 */
const SKIP_DIRS = new Set([
    '_nds', 'system volume information', '$recycle.bin', '.trashes', '.spotlight-v100',
    '.fseventsd', '.temporaryitems', 'found.000',
]);

/**
 * Lowercase file extension including the dot.
 *
 * Note for anyone porting this back to C#: JavaScript's toLowerCase() is defined by the Unicode
 * default case conversion and is NOT locale-sensitive; toLocaleLowerCase() is the dangerous one.
 * The 2020 client used .NET's culture-sensitive ToLower(), which skipped every .ZIP under a
 * Turkish locale.
 */
function extname(name) {
    const base = name.slice(name.lastIndexOf('/') + 1);
    const dot = base.lastIndexOf('.');
    return dot <= 0 ? '' : base.slice(dot).toLowerCase();
}

export const isRom = (name) => ROM_EXTENSIONS.has(extname(name));
const isArchive = (name) => ARCHIVE_EXTENSIONS.has(extname(name));
export const isScannable = (name) => isRom(name) || isArchive(name);

/* walking */

/**
 * Recursively yield every scannable file under a directory handle. Errors on one subdirectory
 * are reported and skipped; one unreadable folder must not abort an 18,000-file scan.
 */
export async function* walkDirectory(dir, path = '', onError) {
    let entries;
    try { entries = dir.values(); }
    catch (e) { onError?.(path || '/', e); return; }

    for await (const handle of entries) {
        const name = handle.name;
        // AppleDouble resource forks look like real ROMs but are 4 KB of metadata.
        if (name.startsWith('._') || name === '.DS_Store') continue;
        const child = path ? `${path}/${name}` : name;

        if (handle.kind === 'directory') {
            if (SKIP_DIRS.has(name.toLowerCase())) continue;
            yield* walkDirectory(handle, child, onError);
            continue;
        }
        if (!isScannable(name)) continue;
        yield { name, path: child, getFile: () => handle.getFile() };
    }
}

/** The read-only path: a FileList from <input webkitdirectory>. */
export function* walkFileList(files) {
    for (const file of files) {
        const rel = file.webkitRelativePath || file.name;
        // Drop the picked folder's own name so paths match the File System Access walk.
        const path = rel.slice(rel.indexOf('/') + 1);
        if (file.name.startsWith('._') || file.name === '.DS_Store') continue;
        if (path.split('/').some(seg => SKIP_DIRS.has(seg.toLowerCase()))) continue;
        if (!isScannable(file.name)) continue;
        yield { name: file.name, path, getFile: async () => file };
    }
}

/**
 * Is this actually a TWiLightMenu++ card? `_nds/TWiLightMenu/` is the sentinel. A miss is not
 * fatal (a freshly formatted card is legitimate), but the user deserves to be told.
 */
export async function hasTwilightSentinel(root) {
    try {
        const nds = await root.getDirectoryHandle('_nds');
        await nds.getDirectoryHandle('TWiLightMenu');
        return true;
    } catch { return false; }
}

/** Same check for the read-only path, where we only have relative paths. */
export function fileListHasSentinel(files) {
    for (const f of files) {
        const rel = (f.webkitRelativePath || '').toLowerCase();
        if (rel.includes('/_nds/twilightmenu/')) return true;
    }
    return false;
}

/** The box art folder under the card root, created on demand. The only directory this tool ever writes to. */
export async function boxartDirectory(root, path) {
    let dir = root;
    for (const segment of path.split('/').filter(s => s && s !== '.' && s !== '..')) {
        dir = await dir.getDirectoryHandle(segment, { create: true });
    }
    return dir;
}

/* probing */

/**
 * Pick the ROM out of an archive's entry list. Prefer a known ROM extension; otherwise take the
 * largest entry.
 *
 * That fallback matters: No-Intro packs DSiWare as raw CDN blobs whose entries are named
 * `00000000`, `tik`, `tmd.0` with no extension at all. The 2020 client returned null here and
 * then crashed on a disposed stream (bug B3), losing 945 of 1,069 DSi titles. The largest entry
 * is the content blob, and its CRC32 is in the DAT, so it can still be identified.
 */
function chooseEntry(entries) {
    const files = entries.filter(e => !e.name.endsWith('/') && e.usize > 0);
    if (!files.length) return null;
    return files.find(e => isRom(e.name)) ?? files.reduce((a, b) => (b.usize > a.usize ? b : a));
}

/**
 * A zero CRC32 means "unknown", not zero.
 *
 * 7z stores availability in a flag that most readers drop, so 0 is ambiguous there by
 * construction. For zip a genuine 0 is a 1-in-4-billion coincidence,
 * and treating it as unknown costs one header read. Both containers therefore fall through.
 */
const usableCrc = (crc) => (crc == null || crc === 0 ? null : crc >>> 0);

/**
 * Read a ROM's identity out of a file as cheaply as it can be had.
 *
 * `wantHeader` requests the ROM's leading bytes, which costs a second slice and a bounded
 * inflate. Leave it off for the first pass; a CRC32 alone identifies most of the library.
 */
export async function probeFile(file, fileName, wantHeader = false) {
    const ext = extname(fileName);

    if (ext === '.zip') {
        const r = await probeZip(file);
        if (!r.ok) return { ok: false, container: 'zip', reason: r.reason };
        const entry = chooseEntry(r.entries);
        if (!entry) return { ok: false, container: 'zip', reason: 'archive contains no files' };
        if (entry.encrypted) return { ok: false, container: 'zip', reason: 'archive is password protected' };

        let header = null;
        if (wantHeader) {
            const h = await zipEntryHeader(file, entry, CONST.HDR_WANT);
            if (h.ok) header = h.bytes;
        }
        // Rule: the ROM's name is the INNER entry name, never the archive's. Sending "Foo.zip"
        // is what made the 2020 server answer HTTP 500 for every unmatched ROM (bug B4).
        return {
            ok: true, container: 'zip', innerName: entry.name.split('/').pop(),
            size: entry.usize, crc32: usableCrc(entry.crc32), header,
        };
    }

    if (ext === '.7z') {
        const r = await probe7z(file);
        if (!r.ok) return { ok: false, container: '7z', reason: r.reason, encodedHeader: r.encodedHeader };
        const entry = chooseEntry(r.entries);
        if (!entry) return { ok: false, container: '7z', reason: 'archive contains no files' };

        let header = null;
        if (wantHeader) header = await sevenZipEntryHeader(file, r, r.entries.indexOf(entry), CONST.HDR_WANT);
        return {
            ok: true, container: '7z', innerName: entry.name.split(/[\\/]/).pop(),
            size: entry.usize, crc32: usableCrc(entry.crc32), header,
        };
    }

    // Loose ROM. No container header to read, so the 512-byte ROM header is the cheap path, plus
    // a full-read CRC32 when the file is small enough for that to be genuinely cheap. Mirrors
    // LooseRomProbe in Core: the budget covers every GB, GBC, GBA, NES, SNES, Mega Drive and Game
    // Gear ROM outright (the consoles with no serial to match on), while anything bigger is a
    // DS or N64 dump that carries a header serial and never needs the hash.
    const want = Math.min(CONST.HDR_WANT, file.size);
    const header = new Uint8Array(await file.slice(0, want).arrayBuffer());
    const crc32 = file.size <= CRC_BYTE_BUDGET ? await crc32File(file) : null;
    return {
        ok: true, container: 'loose', innerName: fileName,
        size: file.size, crc32, header,
    };
}

/** Largest loose file read end-to-end for a CRC32; the same 64 MiB Core uses. */
const CRC_BYTE_BUDGET = 64 * 1024 * 1024;

/* writing */

/** Characters no filesystem we target will accept, plus the trailing dot/space Windows drops. */
export function safeFileName(name) {
    return name
        .replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_')
        .replace(/[. ]+$/, '')
        .slice(0, 180) || 'unnamed';
}

/**
 * Write one PNG. createWritable() buffers into a swap file and swaps it in on close(), so a
 * yanked card can never leave a half-written PNG behind.
 */
export async function writePng(dir, name, bytes) {
    const handle = await dir.getFileHandle(name, { create: true });
    const stream = await handle.createWritable();
    try {
        await stream.write(bytes);
        await stream.close();
    } catch (e) {
        await stream.abort().catch(() => { });
        throw e;
    }
}

export async function fileExists(dir, name) {
    try { await dir.getFileHandle(name); return true; }
    catch { return false; }
}
