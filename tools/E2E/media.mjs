import { deflateSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import gifenc from 'gifenc';
import { zipSync, strToU8 } from 'fflate';

function crc32(bytes) {
    let crc = -1;
    for (const value of bytes) {
        crc ^= value;
        for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
    }
    return (crc ^ -1) >>> 0;
}
function chunk(type, data) {
    const name = Buffer.from(type), size = Buffer.alloc(4), crc = Buffer.alloc(4);
    size.writeUInt32BE(data.length); crc.writeUInt32BE(crc32(Buffer.concat([name, data])));
    return Buffer.concat([size, name, data, crc]);
}
export function png(width = 512, height = 512, artwork = { x: 192, y: 224, width: 128, height: 64 }) {
    const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
    const rows = Buffer.alloc((width * 4 + 1) * height);
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
        if (x < artwork.x || y < artwork.y || x >= artwork.x + artwork.width || y >= artwork.y + artwork.height) continue;
        const offset = y * (width * 4 + 1) + 1 + x * 4;
        rows.set([220, 45, 65, 255], offset);
    }
    return Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), chunk('IHDR', header), chunk('IDAT', deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
}
export function movingGif(delay = 80) {
    const encoder = gifenc.GIFEncoder();
    for (const x of [8, 104]) {
        const indices = new Uint8Array(128 * 128);
        for (let y = 48; y < 80; y++) indices.fill(1, y * 128 + x, y * 128 + x + 16);
        encoder.writeFrame(indices, 128, 128, { palette: [[0, 0, 0], [220, 45, 65]], transparent: true, transparentIndex: 0, dispose: 2, delay, repeat: 0 });
    }
    encoder.finish(); return Buffer.from(encoder.bytes());
}
export function noisyGif() {
    const encoder = gifenc.GIFEncoder();
    const palette = Array.from({ length: 256 }, (_, i) => [i, (i * 73) % 256, (i * 151) % 256]);
    let seed = 12345;
    for (let frame = 0; frame < 12; frame++) {
        const indices = new Uint8Array(512 * 512);
        for (let i = 0; i < indices.length; i++) { seed = (Math.imul(seed, 1664525) + 1013904223) | 0; indices[i] = seed >>> 24; }
        encoder.writeFrame(indices, 512, 512, { palette, delay: 80, repeat: 0 });
    }
    encoder.finish(); return Buffer.from(encoder.bytes());
}
export function packArchive(webp, name = 'Imported fixture') {
    return Buffer.from(zipSync({
        '00.webp': new Uint8Array(webp),
        'cover.png': png(96, 96, { x: 0, y: 0, width: 96, height: 96 }),
        'title.txt': strToU8(name), 'author.txt': strToU8('E2E fixtures')
    }));
}
export const hash = bytes => createHash('sha256').update(bytes).digest('hex');
