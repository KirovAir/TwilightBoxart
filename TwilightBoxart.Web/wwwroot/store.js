// store.js: IndexedDB persistence: the picked directory handle, plus identity and written-art
// caches keyed by CONTENT rather than path, so renaming or moving a ROM invalidates nothing.

import { crc32 } from './romprobe.js';

const DB_NAME = 'twilightboxart';
const DB_VERSION = 1;
const STORES = ['kv', 'identity', 'written'];

let dbPromise = null;

function openDb() {
    dbPromise ??= new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            for (const s of STORES) if (!req.result.objectStoreNames.contains(s)) req.result.createObjectStore(s);
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
    return dbPromise;
}

const done = (tx) => new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
});

const request = (req) => new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
});

async function get(store, key) {
    const db = await openDb();
    return request(db.transaction(store, 'readonly').objectStore(store).get(key));
}

async function put(store, key, value) {
    const db = await openDb();
    const tx = db.transaction(store, 'readwrite');
    tx.objectStore(store).put(value, key);
    return done(tx);
}

/** Reads many keys in one transaction: one round trip for a whole scan's worth of lookups. */
async function getMany(store, keys) {
    const db = await openDb();
    const tx = db.transaction(store, 'readonly');
    const os = tx.objectStore(store);
    const out = new Map();
    await Promise.all(keys.map(async (k) => {
        const v = await request(os.get(k));
        if (v !== undefined) out.set(k, v);
    }));
    return out;
}

/** Writes many entries in one transaction. */
async function putMany(store, entries) {
    if (!entries.length) return;
    const db = await openDb();
    const tx = db.transaction(store, 'readwrite');
    const os = tx.objectStore(store);
    for (const [k, v] of entries) os.put(v, k);
    return done(tx);
}

/* FileSystemDirectoryHandle is structured-cloneable, so it round-trips through IndexedDB. The
   *permission* does not: an ordinary tab loses the grant when the last tab closes. An installed
   PWA keeps it. See the install prompt in app.js. */

export const getSavedRoot = () => get('kv', 'root');
export const saveRoot = (handle) => put('kv', 'root', handle);

/**
 * A stable key for a piece of ROM *content*.
 *
 * CRC32 plus size when the archive gave us one; otherwise a CRC32 of the 512-byte header plus
 * size, which is not collision-proof in theory but is exact for every real ROM (headers carry the
 * title and serial). Falls back to name plus size when we have neither.
 */
export function contentKey({ crc32: crc, header, size, innerName }) {
    if (crc != null) return `c${(crc >>> 0).toString(16)}-${size}`;
    if (header?.length) return `h${crc32(header).toString(16)}-${size}`;
    return `n${innerName}-${size}`;
}

export const loadIdentities = (keys) => getMany('identity', keys);

export const saveIdentities = (entries) =>
    putMany('identity', entries.map(([k, v]) => [k, { ...v, at: Date.now() }]));

/* Render settings are part of the written-art key: changing the box art size or border must
   re-download, but changing nothing must not. */

export const writtenKey = (contentKey, renderKey) => `${contentKey}|${renderKey}`;

export const loadWritten = (keys) => getMany('written', keys);

export const saveWritten = (entries) =>
    putMany('written', entries.map(([k, v]) => [k, { ...v, at: Date.now() }]));

/** Forget every cached identity and download record. Does not touch anything on the SD card. */
export async function clearCache() {
    const db = await openDb();
    const tx = db.transaction(['identity', 'written'], 'readwrite');
    tx.objectStore('identity').clear();
    tx.objectStore('written').clear();
    return done(tx);
}

export async function cacheStats() {
    const db = await openDb();
    const tx = db.transaction(['identity', 'written'], 'readonly');
    const [identities, written] = await Promise.all([
        request(tx.objectStore('identity').count()),
        request(tx.objectStore('written').count()),
    ]);
    return { identities, written };
}
