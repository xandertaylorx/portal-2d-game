// Writes favicon.png: the hero (default look), transparent background, scaled 4x with nearest neighbour.
// node tools/make-favicon.mjs
import { writeFileSync } from 'node:fs';
import zlib from 'node:zlib';
import { buildPlayerSheet, DEFAULT_LOOK } from '../src/art.js';

const S = 4, N = 16, Y0 = 4, W = N * S;
const sheet = buildPlayerSheet(DEFAULT_LOOK);
const rgba = Buffer.alloc(W * W * 4);
for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
  const c = sheet.get(x, y + Y0);
  if (!c.a) continue;
  for (let dy = 0; dy < S; dy++) for (let dx = 0; dx < S; dx++) {
    const o = ((y * S + dy) * W + x * S + dx) * 4;
    rgba[o] = c.r; rgba[o + 1] = c.g; rgba[o + 2] = c.b; rgba[o + 3] = c.a;
  }
}
const raw = Buffer.alloc((W * 4 + 1) * W);
for (let y = 0; y < W; y++) rgba.copy(raw, y * (W * 4 + 1) + 1, y * W * 4, (y + 1) * W * 4);
const crcT = new Uint32Array(256).map((_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
const ih = Buffer.alloc(13); ih.writeUInt32BE(W, 0); ih.writeUInt32BE(W, 4); ih[8] = 8; ih[9] = 6;
const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ih), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
writeFileSync(new URL('../favicon.png', import.meta.url), png);
console.log('favicon.png', png.length, 'bytes');
