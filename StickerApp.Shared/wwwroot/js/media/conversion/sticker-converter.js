import { qualityCeiling } from './media-settings.js';
// Fit, rather than stretch or crop, so preparing a sticker preserves its artwork.
export function fit(context, image, width, height) {
    const scale = Math.min(1, 512 / Math.max(width, height));
    const w = Math.round(width * scale), h = Math.round(height * scale);
    context.clearRect(0, 0, 512, 512);
    context.drawImage(image, (512 - w) / 2, (512 - h) / 2, w, h);
}

// One bundled encoder on every platform, including Safari's iPhone WebView.
export async function encode(canvas, quality, signal) {
    signal?.throwIfAborted();
    const software = await import('../codecs/webp-encoder.js');
    signal?.throwIfAborted();
    const pixels = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height);
    return software.encode(pixels, Math.min(quality, qualityCeiling()));
}

// Intermediate editor artwork does not need the final sticker's size budget.
// Prefer the browser's fast encoder here; final export uses the shared encoder.
export async function encodeWorkingFrame(canvas, quality = 0.9) {
    const blob = canvas.convertToBlob
        ? await canvas.convertToBlob({ type: 'image/webp', quality })
        : await new Promise(resolve => canvas.toBlob(resolve, 'image/webp', quality));
    if (blob?.type === 'image/webp') return new Uint8Array(await blob.arrayBuffer());
    return encode(canvas, quality);
}

export function dataUrl(bytes) {
    let binary = '';
    for (let i = 0; i < bytes.length; i += 32768)
        binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
    return 'data:image/webp;base64,' + btoa(binary);
}

// WebP animation container, following Google's RIFF specification. Each frame is
// a complete canvas with no blending; transparent padding never leaves trails.
function join(parts) {
    const output = new Uint8Array(parts.reduce((size, part) => size + part.length, 0));
    let offset = 0;
    for (const part of parts) { output.set(part, offset); offset += part.length; }
    return output;
}
function write24(bytes, offset, value) {
    bytes[offset] = value; bytes[offset + 1] = value >> 8; bytes[offset + 2] = value >> 16;
}
function chunk(name, body) {
    const result = new Uint8Array(8 + body.length + (body.length & 1));
    result.set(new TextEncoder().encode(name));
    new DataView(result.buffer).setUint32(4, body.length, true);
    result.set(body, 8);
    return result;
}
export function animation(frames, width = 512, height = 512) {
    const header = new Uint8Array(10);
    header[0] = 0x12; // Alpha and animation flags.
    write24(header, 4, width - 1); write24(header, 7, height - 1);
    const parts = [chunk('VP8X', header), chunk('ANIM', new Uint8Array(6))];
    for (const { bytes, duration } of frames) {
        const imageChunks = [];
        for (let offset = 12; offset + 8 <= bytes.length;) {
            const length = new DataView(bytes.buffer, bytes.byteOffset).getUint32(offset + 4, true);
            const name = String.fromCharCode(...bytes.subarray(offset, offset + 4));
            const end = offset + 8 + length + (length & 1);
            if (['ALPH', 'VP8 ', 'VP8L'].includes(name)) imageChunks.push(bytes.subarray(offset, end));
            offset = end;
        }
        if (!imageChunks.length) throw new Error('Could not encode an animation frame.');
        const frameHeader = new Uint8Array(16);
        write24(frameHeader, 6, width - 1); write24(frameHeader, 9, height - 1);
        write24(frameHeader, 12, duration);
        frameHeader[15] = 2; // Replace the previous canvas, without blending.
        parts.push(chunk('ANMF', join([frameHeader, ...imageChunks])));
    }
    return chunk('RIFF', join([new TextEncoder().encode('WEBP'), ...parts]));
}

export async function prepare(source, animated, signal) {
    signal?.throwIfAborted();
    if (animated) {
        const editor = await import('../animation/gif-editor.js');
        try { return await editor.prepare(source); }
        finally { editor.dispose(); }
    }
    const image = new Image();
    const abort = () => { image.src = ''; };
    signal?.addEventListener('abort', abort, { once: true });
    let canvas;
    try {
        image.src = source;
        await image.decode();
        signal?.throwIfAborted();
        if (image.naturalWidth * image.naturalHeight > 32_000_000)
            throw new Error('This image is too large to prepare. Replace it with a smaller original.');
        canvas = document.createElement('canvas');
        canvas.width = canvas.height = 512;
        fit(canvas.getContext('2d'), image, image.naturalWidth, image.naturalHeight);
        for (const quality of [0.9, 0.75, 0.55, 0.35, 0.15]) {
            signal?.throwIfAborted();
            const bytes = await encode(canvas, quality, signal);
            signal?.throwIfAborted();
            if (bytes.length <= 100 * 1024) return dataUrl(bytes);
        }
        throw new Error('This image still exceeds 100 KB. Crop unnecessary detail in Edit, then save it for automatic preparation. Your image is unchanged.');
    } finally {
        signal?.removeEventListener('abort', abort);
        image.src = '';
        if (canvas) canvas.width = canvas.height = 0;
    }
}
