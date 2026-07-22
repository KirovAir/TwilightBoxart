// romprobe.js: identify a ROM inside an archive without decompressing it.
//
// The whole point: a ZIP central directory and a 7z header both already store the CRC32 of the
// *uncompressed* entry, and No-Intro DATs are 100% CRC-populated. So identification is a tail
// read plus an index lookup: 116-508 bytes for a zip, ~200 for a 7z, instead of decompressing
// a 512 MB DS ROM.
//
// Everything in here uses only Blob.slice(), DataView and DecompressionStream. No dependencies.

const TAIL = 4096;          // one tail slice covers 100.00% of the measured 18,087-file corpus
const HDR_WANT = 512;       // bytes of ROM header the identifier needs
const PREFIX = 4096;        // compressed prefix to inflate when we need HDR_WANT bytes

export const CONST = { TAIL, HDR_WANT, PREFIX };

const dec = new TextDecoder();
const u8 = (b) => new Uint8Array(b);

async function slice(file, start, end) {
    if (start < 0) start = 0;
    if (end > file.size) end = file.size;
    return u8(await file.slice(start, end).arrayBuffer());
}

/* CRC32, needed in two places: hashing loose ROMs on demand (the retry-misses path) and
   stamping entries into the fallback .zip download. */

const CRC_TABLE = (() => {
    const t = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
        t[n] = c >>> 0;
    }
    return t;
})();

/** Incremental CRC32. Pass the previous return value back in as `crc` to continue a stream. */
export function crc32(bytes, crc = 0) {
    let c = ~crc >>> 0;
    for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xFF] ^ (c >>> 8);
    return ~c >>> 0;
}

/**
 * CRC32 of an entire file, streamed so a 512 MB ROM never lands in memory at once.
 * This is the expensive path, only worth it for loose files that missed everything else.
 */
export async function crc32File(file, signal) {
    const reader = file.stream().getReader();
    let crc = 0;
    try {
        for (; ;) {
            if (signal?.aborted) throw new DOMException('aborted', 'AbortError');
            const { value, done: end } = await reader.read();
            if (end) break;
            crc = crc32(value, crc);
        }
    } finally {
        reader.cancel().catch(() => { });
    }
    return crc;
}

/* ZIP */

/**
 * Read a zip's central directory. Returns every entry with the CRC32 and uncompressed size
 * the archive itself recorded, plus enough to inflate a header later.
 */
export async function probeZip(file) {
    if (file.size < 22) return { ok: false, reason: 'too small to be a zip' };

    const tailLen = Math.min(TAIL, file.size);
    let buf = await slice(file, file.size - tailLen, file.size);
    let base = file.size - tailLen;

    // locate EOCD (PK\5\6), scanning backwards
    let eo = -1;
    for (let i = buf.length - 22; i >= 0; i--) {
        if (buf[i] === 0x50 && buf[i + 1] === 0x4b && buf[i + 2] === 0x05 && buf[i + 3] === 0x06) { eo = i; break; }
    }
    if (eo < 0) {                      // ZIP comment > TAIL-22; rescan the largest legal window
        const big = Math.min(65557 + 22, file.size);
        buf = await slice(file, file.size - big, file.size);
        base = file.size - big;
        for (let i = buf.length - 22; i >= 0; i--)
            if (buf[i] === 0x50 && buf[i + 1] === 0x4b && buf[i + 2] === 0x05 && buf[i + 3] === 0x06) { eo = i; break; }
        if (eo < 0) return { ok: false, reason: 'no end-of-central-directory record' };
    }
    const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
    let nEnt = dv.getUint16(eo + 10, true);
    let cdSize = dv.getUint32(eo + 12, true);
    let cdOff = dv.getUint32(eo + 16, true);

    // ZIP64: any of the three fields saturated means the real value lives in the ZIP64 EOCD.
    if (cdOff === 0xFFFFFFFF || nEnt === 0xFFFF || cdSize === 0xFFFFFFFF) {
        let lo = -1;
        for (let i = eo - 20; i >= 0; i--)
            if (buf[i] === 0x50 && buf[i + 1] === 0x4b && buf[i + 2] === 0x06 && buf[i + 3] === 0x07) { lo = i; break; }
        if (lo < 0) return { ok: false, reason: 'zip64 locator missing' };
        const z64at = Number(dv.getBigUint64(lo + 8, true));
        const z = await slice(file, z64at, z64at + 56);
        const zv = new DataView(z.buffer, z.byteOffset, z.byteLength);
        nEnt = Number(zv.getBigUint64(32, true));
        cdSize = Number(zv.getBigUint64(40, true));
        cdOff = Number(zv.getBigUint64(48, true));
    }

    // central directory, usually already inside the tail buffer we read
    let cd;
    if (cdOff >= base && cdOff + cdSize <= base + buf.length) cd = buf.subarray(cdOff - base, cdOff - base + cdSize);
    else cd = await slice(file, cdOff, cdOff + cdSize);

    const cv = new DataView(cd.buffer, cd.byteOffset, cd.byteLength);
    const entries = []; let p = 0;
    for (let i = 0; i < nEnt && p + 46 <= cd.length; i++) {
        if (cv.getUint32(p, true) !== 0x02014b50) break;
        const flag = cv.getUint16(p + 8, true);
        const method = cv.getUint16(p + 10, true);
        const entryCrc = cv.getUint32(p + 16, true) >>> 0;
        let csize = cv.getUint32(p + 20, true);
        let usize = cv.getUint32(p + 24, true);
        const nl = cv.getUint16(p + 28, true), el = cv.getUint16(p + 30, true), cl = cv.getUint16(p + 32, true);
        let lho = cv.getUint32(p + 42, true);
        const rawName = cd.subarray(p + 46, p + 46 + nl);
        // UTF-8 when bit 11 is set, CP437 otherwise. Decoding as UTF-8 is right for every
        // observed file and degrades to mojibake rather than failure on the rest.
        const name = dec.decode(rawName);
        // walk the ZIP64 extra field for the real sizes / local header offset
        const ex = cd.subarray(p + 46 + nl, p + 46 + nl + el);
        if (usize === 0xFFFFFFFF || csize === 0xFFFFFFFF || lho === 0xFFFFFFFF) {
            const ev = new DataView(ex.buffer, ex.byteOffset, ex.byteLength);
            for (let q = 0; q + 4 <= ex.length;) {
                const id = ev.getUint16(q, true), sz = ev.getUint16(q + 2, true); let r = q + 4;
                if (id === 0x0001) {
                    if (usize === 0xFFFFFFFF) { usize = Number(ev.getBigUint64(r, true)); r += 8; }
                    if (csize === 0xFFFFFFFF) { csize = Number(ev.getBigUint64(r, true)); r += 8; }
                    if (lho === 0xFFFFFFFF) { lho = Number(ev.getBigUint64(r, true)); r += 8; }
                    break;
                }
                q += 4 + sz;
            }
        }
        entries.push({ name, crc32: entryCrc, csize, usize, method, flag, lho, encrypted: (flag & 1) === 1 });
        p += 46 + nl + el + cl;
    }
    return { ok: true, format: 'zip', entries };
}

/**
 * Inflate the first `want` bytes of a zip entry: one small slice plus the browser's own
 * DecompressionStream. Measured at 0.2 ms for a deflate entry, 0.004 ms for a stored one.
 */
export async function zipEntryHeader(file, e, want = HDR_WANT) {
    if (e.encrypted) return { ok: false, reason: 'entry is encrypted' };

    const lh = await slice(file, e.lho, e.lho + 30);
    const lv = new DataView(lh.buffer, lh.byteOffset, lh.byteLength);
    if (lv.getUint32(0, true) !== 0x04034b50) return { ok: false, reason: 'bad local header' };
    // The local header's name/extra lengths can differ from the central directory's, so they
    // must be read here rather than reused.
    const dataAt = e.lho + 30 + lv.getUint16(26, true) + lv.getUint16(28, true);

    if (e.method === 0) {                                   // stored: direct read, no inflate
        return { ok: true, via: 'stored', bytes: await slice(file, dataAt, dataAt + want) };
    }
    if (e.method !== 8) return { ok: false, reason: `compression method ${e.method} unsupported` };

    const prefix = await slice(file, dataAt, dataAt + Math.min(PREFIX, e.csize));
    const ds = new DecompressionStream('deflate-raw');
    const wtr = ds.writable.getWriter();
    const collect = (async () => {
        const rd = ds.readable.getReader(); const parts = []; let n = 0;
        try {
            for (; ;) {
                const { value, done } = await rd.read();
                if (done) break;
                parts.push(value); n += value.length;
                if (n >= want) { rd.cancel().catch(() => { }); break; }
            }
        } catch { /* the truncated deflate tail always throws; we already have what we need */ }
        const out = new Uint8Array(n); let o = 0; for (const x of parts) { out.set(x, o); o += x.length; }
        return out;
    })();
    wtr.write(prefix).catch(() => { });
    wtr.close().catch(() => { });
    const out = await collect;
    return out.length >= Math.min(want, e.usize)
        ? { ok: true, via: 'deflate-raw', bytes: out.subarray(0, want) }
        : { ok: false, reason: `only ${out.length} of ${want} header bytes recovered` };
}

/* 7z */

class Rd {
    constructor(b) { this.b = b; this.p = 0; }
    u8() { return this.b[this.p++]; }
    take(n) { const v = this.b.subarray(this.p, this.p + n); this.p += n; return v; }
    /** Little-endian uint32 out of the next 4 bytes. */
    u32() { const q = this.take(4); return (q[0] | (q[1] << 8) | (q[2] << 16) | (q[3] << 24)) >>> 0; }
    num() { // 7z variable-length NUMBER
        const first = this.u8(); let mask = 0x80, value = 0;
        for (let i = 0; i < 8; i++) {
            if (!(first & mask)) return value + ((first & (mask - 1)) * 2 ** (8 * i));
            value += this.b[this.p++] * 2 ** (8 * i);
            mask >>= 1;
        }
        return value;
    }
    bits(n) { const o = []; let b = 0, m = 0; for (let i = 0; i < n; i++) { if (m === 0) { b = this.u8(); m = 0x80; } o.push(!!(b & m)); m >>= 1; } return o; }
    boolvec(n) { return this.u8() ? new Array(n).fill(true) : this.bits(n); }
}

function parseStreams(r) {
    const info = { packPos: 0, packSizes: [], folders: [], unpackSizes: [], folderCrcs: [], subSizes: [], subCrcs: [] };
    for (; ;) {
        const t = r.num(); if (t === 0x00) break;
        if (t === 0x06) {
            info.packPos = r.num(); const n = r.num();
            for (; ;) {
                const tt = r.num(); if (tt === 0x00) break;
                if (tt === 0x09) { for (let i = 0; i < n; i++) info.packSizes.push(r.num()); }
                else if (tt === 0x0A) { const d = r.boolvec(n); for (const x of d) if (x) r.take(4); }
                else throw Error('packinfo ' + tt);
            }
        }
        else if (t === 0x07) {
            for (; ;) {
                const tt = r.num(); if (tt === 0x00) break;
                if (tt === 0x0B) {
                    const nf = r.num(); r.u8();
                    for (let i = 0; i < nf; i++) {
                        const nc = r.num(); const f = { coders: [], numIn: 0, numOut: 0, props: [] };
                        for (let c = 0; c < nc; c++) {
                            const fl = r.u8(); const id = r.take(fl & 0x0F);
                            let nin = 1, nout = 1; if (fl & 0x10) { nin = r.num(); nout = r.num(); }
                            if (fl & 0x20) { f.props.push(r.take(r.num())); } else f.props.push(null);
                            f.coders.push({ id: [...id].map(x => x.toString(16).padStart(2, '0')).join(''), nin, nout });
                            f.numIn += nin; f.numOut += nout;
                        }
                        for (let k = 0; k < f.numOut - 1; k++) { r.num(); r.num(); }
                        const nps = f.numIn - (f.numOut - 1); if (nps > 1) for (let k = 0; k < nps; k++) r.num();
                        info.folders.push(f);
                    }
                }
                else if (tt === 0x0C) { for (const f of info.folders) { f.unpackSizes = []; for (let k = 0; k < f.numOut; k++) f.unpackSizes.push(r.num()); info.unpackSizes.push(f.unpackSizes[f.unpackSizes.length - 1]); } }
                // Folder CRCs. The prototype read these through `new DataView(take(4).buffer.slice(0))`,
                // which resolves to offset 0 of the *whole* backing buffer rather than the 4 bytes it
                // just took: garbage whenever the header is a subarray of the tail read, which is the
                // normal case. Read the four bytes directly instead.
                else if (tt === 0x0A) { const d = r.boolvec(info.folders.length); for (const x of d) info.folderCrcs.push(x ? r.u32() : null); }
                else throw Error('unpackinfo ' + tt);
            }
        }
        else if (t === 0x08) {
            let nun = info.folders.map(() => 1); const sizes = [], crcs = [];
            for (; ;) {
                const tt = r.num(); if (tt === 0x00) break;
                if (tt === 0x0D) { nun = info.folders.map(() => r.num()); }
                else if (tt === 0x09) {
                    info.folders.forEach((f, fi) => {
                        if (!nun[fi]) return; let tot = 0;
                        for (let k = 0; k < nun[fi] - 1; k++) { const s = r.num(); sizes.push(s); tot += s; }
                        sizes.push(info.unpackSizes[fi] - tot);
                    });
                }
                else if (tt === 0x0A) {
                    // Per the 7z spec a digest is only stored for substreams whose CRC is not
                    // already known, and it *is* known when a folder holds exactly one stream and
                    // that folder carried a CRC in kUnPackInfo. Counting every substream (as the
                    // prototype did) desynchronises the digest list on such archives.
                    const folderOf = [], unknown = [];
                    info.folders.forEach((f, fi) => {
                        const known = nun[fi] === 1 && info.folderCrcs[fi] != null;
                        for (let k = 0; k < nun[fi]; k++) { folderOf.push(fi); unknown.push(!known); }
                    });
                    const d = r.boolvec(unknown.filter(Boolean).length);
                    let di = 0;
                    for (let i = 0; i < unknown.length; i++) {
                        if (!unknown[i]) { crcs.push(info.folderCrcs[folderOf[i]]); continue; }
                        crcs.push(d[di++] ? r.u32() : null);
                    }
                }
                else throw Error('substreams ' + tt);
            }
            info.subSizes = sizes.length ? sizes : info.unpackSizes.slice();
            info.subCrcs = crcs.length ? crcs : info.folderCrcs.slice();
        }
        else throw Error('streamsinfo ' + t);
    }
    if (!info.subSizes.length) info.subSizes = info.unpackSizes.slice();
    if (!info.subCrcs.length) info.subCrcs = info.folderCrcs.length ? info.folderCrcs.slice() : info.subSizes.map(() => null);
    return info;
}

function parseHeader(buf) {
    const r = new Rd(buf); const t = r.num();
    if (t === 0x17) return { kind: 'encoded', encoded: parseStreams(r) };
    if (t !== 0x01) throw Error('unexpected header kind ' + t);
    const out = { kind: 'header' };
    for (; ;) {
        const tt = r.num(); if (tt === 0x00) break;
        if (tt === 0x04) out.streams = parseStreams(r);
        else if (tt === 0x05) {
            const nf = r.num(); let names = [];
            let emptyStream = new Array(nf).fill(false), emptyFile = [];
            for (; ;) {
                const pt = r.num(); if (pt === 0x00) break; const sz = r.num(); const end = r.p + sz;
                if (pt === 0x11) {
                    r.u8(); const raw = r.take(end - r.p);
                    names = new TextDecoder('utf-16le').decode(raw).split('\u0000').filter(Boolean);
                }
                else if (pt === 0x0E) { emptyStream = r.bits(nf); }
                else if (pt === 0x0F) { emptyFile = r.bits(emptyStream.filter(Boolean).length); }
                r.p = end;
            }
            // Only !emptyStream files own an unpacked substream, in order. Directories and
            // zero-byte files must be skipped or names desynchronise from CRCs. (Found the hard
            // way during prototype validation.)
            let ei = 0;
            out.files = names.map((name, i) => ({
                name, hasStream: !emptyStream[i],
                streamIndex: emptyStream[i] ? -1 : ei++,
                isDir: emptyStream[i] && !emptyFile[emptyStream.slice(0, i).filter(Boolean).length],
            }));
            out.nFiles = nf;
        }
        else throw Error('header ' + tt);
    }
    return out;
}

/**
 * Read a .7z header. 32 bytes at offset 0 plus one tail slice.
 *
 * When 7-Zip compressed its own header (kEncodedHeader) the browser cannot read it: there is no
 * native LZMA decoder and DecompressionStream only speaks deflate/gzip/deflate-raw. We report
 * that honestly rather than shipping a 1.6 MB WASM blob to every visitor.
 */
export async function probe7z(file) {
    if (file.size < 32) return { ok: false, reason: 'too small to be a 7z' };

    const head = await slice(file, 0, 32);
    if (!(head[0] === 0x37 && head[1] === 0x7A && head[2] === 0xBC && head[3] === 0xAF && head[4] === 0x27 && head[5] === 0x1C))
        return { ok: false, reason: 'bad 7z signature' };
    const hv = new DataView(head.buffer, head.byteOffset, head.byteLength);
    const nhOff = Number(hv.getBigUint64(12, true)), nhSize = Number(hv.getBigUint64(20, true));
    const nhAt = 32 + nhOff;

    // one tail slice normally covers BOTH the next header and (if encoded) the packed header
    const tailLen = Math.min(Math.max(TAIL, nhSize + TAIL), file.size);
    const tail = await slice(file, file.size - tailLen, file.size);
    const tBase = file.size - tailLen;
    const inTail = (off, len) => off >= tBase && off + len <= tBase + tail.length;
    const nh = inTail(nhAt, nhSize) ? tail.subarray(nhAt - tBase, nhAt - tBase + nhSize)
        : await slice(file, nhAt, nhAt + nhSize);

    let h;
    try { h = parseHeader(nh); }
    catch (e) { return { ok: false, reason: '7z header: ' + e.message }; }

    if (h.kind === 'encoded') {
        return {
            ok: false, format: '7z', encodedHeader: true,
            reason: '7z header is LZMA-compressed and browsers have no LZMA decoder',
        };
    }
    const s = h.streams;
    const entries = (h.files || []).filter(f => f.hasStream).map(f => ({
        name: f.name, usize: s.subSizes[f.streamIndex], crc32: s.subCrcs[f.streamIndex],
    }));
    return {
        ok: true, format: '7z', encodedHeader: false, entries,
        coder: s.folders[0]?.coders[0]?.id, packPos: 32 + s.packPos, packSizes: s.packSizes,
    };
}

/**
 * The first `want` bytes of a 7z entry, but only for the one case a browser can serve: an
 * uncompressed (Copy coder) first entry. Anything LZMA-compressed returns null and the caller
 * falls back to the CRC32 the header already gave us.
 */
export async function sevenZipEntryHeader(file, probe, index, want = HDR_WANT) {
    if (index !== 0 || probe.coder !== '00') return null;
    return slice(file, probe.packPos, probe.packPos + want);
}
