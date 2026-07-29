// scan.js: what counts as a ROM, walking the card (File System Access handles or a
// webkitdirectory FileList, both normalised to { name, path, getFile() }), and probing each one.

import { probeZip, probe7z, zipEntryHeader, sevenZipEntryHeader, crc32, crc32File, lz77Decompress, CONST } from './romprobe.js';

/**
 * What a scan opens, rendered into the page by Index.cshtml straight from SupportedFiles. Hand-copied
 * into this file until 2.2, by which point it had fallen nineteen extensions behind the backend and
 * the browser client was walking silently past every WonderSwan, Neo Geo Pocket, PC Engine, Atari,
 * ColecoVision, Intellivision and MSX ROM on the card.
 */
const formats = JSON.parse(document.getElementById('formats').textContent);
const ROM_EXTENSIONS = new Set(formats.rom);
const ARCHIVE_EXTENSIONS = new Set(formats.archive);

/**
 * Directories that never hold ROMs. `_nds` and `_pico` are skipped because they hold the
 * launchers' own data, including the cover folders this tool writes to, which we must not read
 * back in as input.
 */
const SKIP_DIRS = new Set([
    '_nds', '_pico', 'system volume information', '$recycle.bin', '.trashes', '.spotlight-v100',
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

/**
 * A Nintendo-LZ77-wrapped ROM: "Game.lz77.sfc". The inner extension is the real console - which is why
 * extname()/isRom() already treat these as .sfc/.gen/... and the walk picks them up - while the ".lz77"
 * infix means the bytes are LZSS-0x10 compressed. Only the consoles nds-bootstrap decompresses on-device
 * qualify (its hb/arm9/source/main.cpp), matching Lz77RomProbe in Core, so a cover we make is one the
 * menu can actually use. Largest compressed ROM we will read into memory to inflate.
 */
const LZ77_EXTENSIONS = new Set(['.sfc', '.smc', '.gen', '.md', '.sms', '.gg', '.pce']);
const LZ77_MAX_COMPRESSED = 24 * 1024 * 1024;
function isLz77(name) {
    const ext = extname(name);
    if (!LZ77_EXTENSIONS.has(ext)) return false;
    const base = name.slice(name.lastIndexOf('/') + 1, name.length - ext.length);
    return base.toLowerCase().endsWith('.lz77');
}

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

/** The Pico Launcher equivalents: `_pico/` is the sentinel. */
export async function hasPicoSentinel(root) {
    try {
        await root.getDirectoryHandle('_pico');
        return true;
    } catch { return false; }
}

export function fileListHasPicoSentinel(files) {
    for (const f of files) {
        const rel = (f.webkitRelativePath || '').toLowerCase();
        if (rel.includes('/_pico/')) return true;
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

    if (isLz77(fileName)) {
        // Nintendo LZ77 (LZSS-0x10). These ROMs are a few MB, so inflate the whole thing and identify
        // off the raw bytes: the CRC32 of the decompressed ROM is the one No-Intro recorded, so an
        // ".lz77.sfc" resolves to the exact same game as its plain ".sfc" twin. The cover is keyed on
        // the on-card name (innerName = fileName), which is what the launcher looks the art up by.
        if (file.size > LZ77_MAX_COMPRESSED) {
            return { ok: false, container: 'lz77', reason: 'file too large to be a compressed ROM' };
        }
        const rom = lz77Decompress(new Uint8Array(await file.arrayBuffer()));
        if (!rom) return { ok: false, container: 'lz77', reason: 'not a valid LZ77 stream' };
        return {
            ok: true, container: 'lz77', innerName: fileName, size: rom.length,
            crc32: crc32(rom), header: rom.slice(0, Math.min(CONST.HDR_WANT, rom.length)),
        };
    }

    // Loose ROM. No container header to read, so the 512-byte ROM header is the cheap path, plus a
    // full-read CRC32. Mirrors LooseRomProbe in Core: the size budget applies only where a header
    // title id makes the hash redundant, which is DS and DSi and nothing else. Everything else is
    // hashed whatever its size, or the biggest SNES and N64 romhacks arrive with nothing to match on.
    const want = Math.min(CONST.HDR_WANT, file.size);
    const header = new Uint8Array(await file.slice(0, want).arrayBuffer());
    // Named `crc` rather than `crc32` so it does not shadow the imported crc32() across this function.
    const crc = !hasTitleId(fileName) || file.size <= CRC_BYTE_BUDGET ? await crc32File(file) : null;
    return {
        ok: true, container: 'loose', innerName: fileName,
        size: file.size, crc32: crc, header,
    };
}

/** Largest DS/DSi file read end-to-end for a CRC32; the same 64 MiB Core uses. */
const CRC_BYTE_BUDGET = 64 * 1024 * 1024;

/**
 * Whether the header carries a title id good enough to identify the file without a checksum, which
 * is DS and DSi and nothing else. Mirrors SupportedFiles.HasTitleId in Core.
 */
const TITLE_ID_EXTENSIONS = new Set(['.nds', '.ds', '.dsi', '.srl', '.ids', '.app']);
const hasTitleId = (name) => TITLE_ID_EXTENSIONS.has((name.match(/\.[^.]*$/) ?? [''])[0].toLowerCase());

/* writing */

/** Characters no filesystem we target will accept, plus the trailing dot/space Windows drops. */
export function safeFileName(name) {
    return name
        .replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_')
        .replace(/[. ]+$/, '')
        .slice(0, 180) || 'unnamed';
}

/**
 * Write one cover (PNG or Pico BMP). createWritable() buffers into a swap file and swaps it in
 * on close(), so a yanked card can never leave a half-written file behind.
 */
export async function writeArt(dir, name, bytes) {
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
