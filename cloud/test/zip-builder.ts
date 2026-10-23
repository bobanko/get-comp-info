// Builds ZIP archives in tests, with stored or deflate entries and real CRC-32 values.
import { sha256Hex } from '../src/crypto';

export interface BuildEntry {
  name: string;
  data: Uint8Array | string;
  deflate?: boolean;
}

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(data: Uint8Array): number {
  let crc = 0xffffffff;
  for (const byte of data) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

async function deflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data]).stream().pipeThrough(new CompressionStream('deflate-raw'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

export function bytesOf(value: Uint8Array | string): Uint8Array {
  return typeof value === 'string' ? new TextEncoder().encode(value) : value;
}

export async function buildZip(entries: BuildEntry[]): Promise<Uint8Array> {
  const encoder = new TextEncoder();
  const locals: Uint8Array[] = [];
  const centrals: Uint8Array[] = [];
  let offset = 0;
  for (const entry of entries) {
    const data = bytesOf(entry.data);
    const stored = entry.deflate ? await deflateRaw(data) : data;
    const name = encoder.encode(entry.name);
    const crc = crc32(data);
    const method = entry.deflate ? 8 : 0;

    const local = new Uint8Array(30 + name.length + stored.length);
    const lv = new DataView(local.buffer);
    lv.setUint32(0, 0x04034b50, true);
    lv.setUint16(4, 20, true);
    lv.setUint16(6, 0x0800, true);
    lv.setUint16(8, method, true);
    lv.setUint32(14, crc, true);
    lv.setUint32(18, stored.length, true);
    lv.setUint32(22, data.length, true);
    lv.setUint16(26, name.length, true);
    local.set(name, 30);
    local.set(stored, 30 + name.length);

    const central = new Uint8Array(46 + name.length);
    const cv = new DataView(central.buffer);
    cv.setUint32(0, 0x02014b50, true);
    cv.setUint16(4, 20, true);
    cv.setUint16(6, 20, true);
    cv.setUint16(8, 0x0800, true);
    cv.setUint16(10, method, true);
    cv.setUint32(16, crc, true);
    cv.setUint32(20, stored.length, true);
    cv.setUint32(24, data.length, true);
    cv.setUint16(28, name.length, true);
    cv.setUint32(42, offset, true);
    central.set(name, 46);

    locals.push(local);
    centrals.push(central);
    offset += local.length;
  }
  const centralSize = centrals.reduce((sum, part) => sum + part.length, 0);
  const end = new Uint8Array(22);
  const ev = new DataView(end.buffer);
  ev.setUint32(0, 0x06054b50, true);
  ev.setUint16(8, entries.length, true);
  ev.setUint16(10, entries.length, true);
  ev.setUint32(12, centralSize, true);
  ev.setUint32(16, offset, true);

  const result = new Uint8Array(offset + centralSize + 22);
  let position = 0;
  for (const part of [...locals, ...centrals, end]) {
    result.set(part, position);
    position += part.length;
  }
  return result;
}

// A release archive with a manifest describing its files.
export async function buildRelease(
  version: string,
  files: Record<string, string>,
  options: { deflate?: boolean; manifest?: Record<string, unknown>; extra?: BuildEntry[] } = {},
): Promise<Uint8Array> {
  const manifestFiles = [];
  for (const [path, content] of Object.entries(files)) {
    const data = bytesOf(content);
    manifestFiles.push({ path, sha256: await sha256Hex(data), size: data.length });
  }
  const manifest = { version, commit: 'abc123', notes: ['feat: test release'], files: manifestFiles, ...options.manifest };
  return buildZip([
    { name: 'manifest.json', data: JSON.stringify(manifest), deflate: options.deflate },
    ...Object.entries(files).map(([name, data]) => ({ name, data, deflate: options.deflate })),
    ...(options.extra ?? []),
  ]);
}
