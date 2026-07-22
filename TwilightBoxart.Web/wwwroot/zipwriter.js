// zipwriter.js: minimal store-only ZIP writer for browsers without the File System Access API.
// PNGs are already compressed, so entries are stored verbatim: no deflate, no dependency.

import { crc32 } from './romprobe.js';

const enc = new TextEncoder();

/** MS-DOS date/time, the only timestamp a plain (non-ZIP64) zip carries. */
function dosDateTime(d = new Date()) {
    const time = (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1);
    const date = ((d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
    return { time, date };
}

function u32(v) { const b = new Uint8Array(4); new DataView(b.buffer).setUint32(0, v >>> 0, true); return b; }
function u16(v) { const b = new Uint8Array(2); new DataView(b.buffer).setUint16(0, v & 0xFFFF, true); return b; }

/**
 * Build a zip from `[name, bytes]` pairs.
 *
 * Deliberately plain-zip only. ZIP64 would be needed past 65,535 entries or 4 GiB; both are far
 * beyond a card's worth of 45 KB box art, and a hard error beats silently emitting a corrupt
 * archive.
 */
export function buildZip(files) {
    if (files.length > 0xFFFE) throw new Error(`too many files for a plain zip (${files.length}); download in batches`);

    const { time, date } = dosDateTime();
    const parts = [];       // local headers + data, in order
    const central = [];     // central directory records
    let offset = 0;

    for (const [name, bytes] of files) {
        const nameBytes = enc.encode(name);
        const crc = crc32(bytes);
        const local = [
            u32(0x04034b50), u16(20), u16(0x0800), u16(0),   // bit 11: names are UTF-8
            u16(time), u16(date), u32(crc), u32(bytes.length), u32(bytes.length),
            u16(nameBytes.length), u16(0), nameBytes,
        ];
        for (const p of local) parts.push(p);
        parts.push(bytes);

        central.push([
            u32(0x02014b50), u16(20), u16(20), u16(0x0800), u16(0),
            u16(time), u16(date), u32(crc), u32(bytes.length), u32(bytes.length),
            u16(nameBytes.length), u16(0), u16(0), u16(0), u16(0), u32(0), u32(offset), nameBytes,
        ]);

        offset += local.reduce((a, b) => a + b.length, 0) + bytes.length;
        if (offset > 0xFFFFFFFF) throw new Error('archive would exceed 4 GiB; download in batches');
    }

    const cdStart = offset;
    let cdSize = 0;
    for (const rec of central) for (const p of rec) { parts.push(p); cdSize += p.length; }

    parts.push(u32(0x06054b50), u16(0), u16(0), u16(files.length), u16(files.length),
        u32(cdSize), u32(cdStart), u16(0));

    return new Blob(parts, { type: 'application/zip' });
}

/** Hand a Blob to the browser's downloader. */
export function downloadBlob(blob, filename) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    // Revoking immediately can cancel the download in some builds; revoke after a safety-net
    // delay generous enough for the browser to have opened the Blob.
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
