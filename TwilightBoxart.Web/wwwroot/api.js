// api.js: the only file that knows the backend's wire format. This client never BUILDS an art
// URL: it appends render parameters to the artPath the server handed out in identify.

const BASE = '/v2';

/**
 * Sent on every /v2 request. NOT a secret: it ships in this file, which the server serves to
 * anyone who asks, and it is in the public repository besides. It marks a request as coming from
 * something written against this API, which is enough to turn away drive-by scrapers and hotlinkers
 * pointed at the art routes; that traffic costs us bandwidth from volunteer-run upstreams. Keep in
 * step with ApiKey.cs. The legacy POST /api shim is deliberately not gated on it.
 */
const API_KEY_HEADER = 'X-Twilight-Key';
const API_KEY = 'tb2_9f4c1d7a3e8b5062';

/**
 * Says which client is calling, the way the DSi build's User-Agent does. A browser cannot set
 * User-Agent from script, so it goes in a header of our own (ClientHeader.cs server-side); the
 * backend's anonymous activity counters group us under this label.
 *
 * The version is written out here because wwwroot has no build step to inject it. It has to be
 * bumped alongside <Version> in Directory.Build.props, which is where every other copy comes from.
 */
const CLIENT_HEADER = 'X-Twilight-Client';
const CLIENT = 'web/2.0';

/** Server-side limits: <=500 items and <=1 MB per identify call. */
export const IDENTIFY_CHUNK = 200;

// The batch envelope lives in exactly these two functions. The server is pinned to
// { items, matched } with camelCase fields and string enums.
const identifyBody = (items) => ({ items });
const identifyResults = (json) => json?.items ?? [];

/**
 * Human labels for the misses table, keyed by the ConsoleType name the wire carries. Display only:
 * nothing here ever travels back to the server, so an unknown name degrades to itself rather than
 * to a broken request.
 */
const PLATFORM_LABELS = {
    GameBoy: 'Game Boy', GameBoyColor: 'Game Boy Color', GameBoyAdvance: 'Game Boy Advance',
    NintendoDs: 'Nintendo DS', NintendoDsi: 'Nintendo DSi', Nes: 'NES', Snes: 'SNES',
    Nintendo64: 'Nintendo 64', FamicomDiskSystem: 'Famicom Disk System',
    MegaDrive: 'Mega Drive', MasterSystem: 'Master System', GameGear: 'Game Gear',
};

/** The identity's console as a human label, e.g. "Nintendo DS". */
export function platformLabel(identity) {
    const c = identity?.consoleType;
    return PLATFORM_LABELS[c] ?? (typeof c === 'string' ? c : 'Unknown');
}

/** MatchMethod as a lowercase word, e.g. "header serial". */
function matchMethodName(identity) {
    const m = identity?.matchMethod;
    return typeof m === 'string' ? m.replace(/([a-z])([A-Z0-9])/g, '$1 $2').toLowerCase() : 'unknown';
}

export const isMatched = (identity) =>
    !!identity && !!identity.key && matchMethodName(identity) !== 'none';

const base64 = (bytes) => {
    let s = '';
    for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(s);
};

/**
 * Build one RomFingerprint. Field names match the C# record, which System.Text.Json camel-cases.
 * Everything except fileName is optional; send only what was cheap to obtain.
 */
export function fingerprint({ innerName, crc32, header, size, tag }) {
    const fp = { fileName: innerName, tag };
    if (crc32 != null) fp.crc32 = crc32 >>> 0;
    if (header?.length) fp.header = base64(header);   // byte[] serialises as base64
    if (size != null) fp.size = size;
    return fp;
}

class HttpError extends Error {
    constructor(status, url) {
        super(`${status} from ${url}`);
        this.status = status;
    }
}

/** One retry, and only for the statuses where retrying is meaningful. Honours Retry-After. */
async function fetchWithRetry(url, init = {}) {
    const attempts = 2;
    let last;
    // Applied here rather than at each call site so no future request can forget it.
    const headers = { ...init.headers, [API_KEY_HEADER]: API_KEY, [CLIENT_HEADER]: CLIENT };
    for (let i = 0; i < attempts; i++) {
        const res = await fetch(url, { ...init, headers });
        if (res.ok || res.status === 404) return res;
        last = new HttpError(res.status, url);
        if (res.status !== 429 && res.status < 500) throw last;
        const after = Number(res.headers.get('Retry-After'));
        const waitMs = Number.isFinite(after) && after > 0 ? Math.min(after * 1000, 30_000) : 500 * (i + 1);
        if (i < attempts - 1) await new Promise(r => setTimeout(r, waitMs));
    }
    throw last;
}

/**
 * Identify one batch. Returns the identities in whatever order the server sent them; callers
 * must correlate on `tag`, which is echoed back for exactly that reason.
 */
export async function identifyBatch(fingerprints, signal) {
    const res = await fetchWithRetry(`${BASE}/identify`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(identifyBody(fingerprints)),
        signal,
    });
    if (!res.ok) throw new HttpError(res.status, `${BASE}/identify`);
    return identifyResults(await res.json());
}

/**
 * The identity's art URL with render parameters appended. The path itself comes from the server
 * (identify's artPath) and is followed verbatim: this client owns the render PREFERENCES, the
 * server owns the URL. The query mirrors RenderOptions.ToQueryString() exactly: w/h pixels, ar
 * aspect ratio as 1/0, b border style, bt thickness, bc colour as AARRGGBB hex. Matching the
 * server's own encoder makes these URLs textually identical to the Content-Location it advertises,
 * so every client converges on one cacheable URL per render.
 */
function artUrl(identity, o) {
    const path = identity?.artPath;
    if (!path) return null;
    const q = new URLSearchParams({
        w: String(o.width),
        h: String(o.height),
        ar: o.keepAspectRatio ? '1' : '0',
        b: o.borderStyle,
        bt: String(o.borderThickness),
        bc: (o.borderColor >>> 0).toString(16).toUpperCase().padStart(8, '0'),
    });
    return `${path}?${q}`;
}

/**
 * Fetch rendered art for an identity. Returns null on 404 or when the identity carries no art URL;
 * a miss is an ordinary outcome, not an error, and the user gets told why rather than seeing a
 * stack trace.
 */
export async function fetchArt(identity, options, signal) {
    const url = artUrl(identity, options);
    if (!url) return null;
    const res = await fetchWithRetry(url, { signal });
    if (res.status === 404) return null;
    if (!res.ok) throw new HttpError(res.status, url);
    return new Uint8Array(await res.arrayBuffer());
}
