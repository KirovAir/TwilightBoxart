// Unit tests for the browser client's Nintendo-LZ77 decoder. Run from the repo root with:
//   node --test TwilightBoxart.Web/tests/lz77.test.mjs
//
// romprobe.js has no DOM dependencies at module scope, so it imports straight into Node. These pin the
// same decode as Lz77RomProbeTests.cs in the C# core and lz77.c in the DS client - one algorithm, three
// languages, one set of vectors.

import test from 'node:test';
import assert from 'node:assert/strict';

import {lz77Decompress, crc32} from '../wwwroot/romprobe.js';

// scan.js reads the server-rendered formats out of the page at import time; stub the one element it
// wants before importing it, so probeFile() can be exercised under Node.
globalThis.document = {
    getElementById: () => ({
        textContent: JSON.stringify({
            rom: ['.sfc', '.smc', '.gen', '.md', '.sms', '.gg', '.pce', '.nds'],
            archive: ['.zip', '.7z'],
            labels: {},
        }),
    }),
};
const scan = await import('../wwwroot/scan.js');

/** Wrap bytes in a valid LZ77 (type 0x10) stream using literal tokens only - the simplest stream the
 decoder must accept, and enough to prove the round trip. Back-references are asserted separately. */
function packLiterals(data) {
    const out = [0x10, data.length & 0xFF, (data.length >> 8) & 0xFF, (data.length >> 16) & 0xFF];
    for (let i = 0; i < data.length; i += 8) {
        out.push(0x00); // eight literal flags; the decoder stops at the declared length
        for (let j = i; j < i + 8 && j < data.length; j++) out.push(data[j]);
    }
    return new Uint8Array(out);
}

test('round-trips a ROM and the CRC32 matches the plain (uncompressed) bytes', () => {
    // A non-multiple-of-8 length exercises the partial final flag group and the exact-length stop.
    const rom = Uint8Array.from({length: 5003}, (_, i) => (i * 31 + 7) & 0xFF);

    const decoded = lz77Decompress(packLiterals(rom));

    assert.deepEqual(decoded, rom);
    // The whole point: the compressed cart identifies by the same CRC32 as its plain ".sfc" twin.
    assert.equal(crc32(decoded), crc32(rom));
});

test('decodes literals and back-references', () => {
    // All literals: header (length 5), one flag byte of zeros, then the five bytes.
    const hello = lz77Decompress(Uint8Array.from([0x10, 5, 0, 0, 0x00, 72, 69, 76, 76, 79]));
    assert.equal(new TextDecoder().decode(hello), 'HELLO');

    // "AAAA": a literal 'A', then a 3-byte back-reference at distance 1 (flag bit 1 set = 0x40).
    const aaaa = lz77Decompress(Uint8Array.from([0x10, 4, 0, 0, 0x40, 65, 0, 0]));
    assert.equal(new TextDecoder().decode(aaaa), 'AAAA');
});

test('returns null on malformed streams', () => {
    assert.equal(lz77Decompress(Uint8Array.from([0x11, 4, 0, 0, 0x00, 1, 2, 3, 4])), null); // wrong type byte
    assert.equal(lz77Decompress(Uint8Array.from([0x10, 8, 0, 0, 0x00, 1, 2, 3])), null);     // declares 8, truncated
    assert.equal(lz77Decompress(Uint8Array.from([0x10, 4, 0, 0, 0x80, 0, 0])), null);         // back-ref before the start
    assert.equal(lz77Decompress(Uint8Array.from([0x10, 4])), null);                            // too short for a header
});

test('probeFile identifies an .lz77 ROM by the decompressed CRC and keeps the on-card name', async () => {
    const rom = Uint8Array.from({length: 4096}, (_, i) => (i * 17 + 3) & 0xFF);
    const probe = await scan.probeFile(new Blob([packLiterals(rom)]), 'game.lz77.gen', false);

    assert.equal(probe.ok, true);
    assert.equal(probe.container, 'lz77');
    assert.equal(probe.crc32, crc32(rom));                    // the same CRC the plain ".gen" would give
    assert.equal(probe.innerName, 'game.lz77.gen');           // cover keyed on the file ON THE CARD
    assert.equal(probe.size, rom.length);
    assert.equal(probe.header.length, Math.min(512, rom.length));
});

test('probeFile leaves a plain ROM on the loose path, untouched by the .lz77 branch', async () => {
    const rom = Uint8Array.from({length: 4096}, (_, i) => (i * 17 + 3) & 0xFF);
    const probe = await scan.probeFile(new Blob([rom]), 'game.gen', false);

    assert.equal(probe.container, 'loose');
    // Same bytes as the compressed twin above => same CRC => resolves to the same game.
    assert.equal(probe.crc32, crc32(rom));
});
