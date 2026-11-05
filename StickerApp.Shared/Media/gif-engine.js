import { fit, encode, animation, dataUrl } from './sticker-converter.js';
import { parseGIF, decompressFrame } from 'gifuct-js';
import { GIFEncoder, quantize, applyPalette } from 'gifenc';

let cachedSource, cached;
const delayOf = frame => Math.max(20, (frame.gce?.delay || 10) * 10);

function load(source) {
    if (source === cachedSource) return cached;
    if (!source.startsWith('data:image/gif;base64,')) throw new Error('Select a GIF.');
    const binary = atob(source.slice(source.indexOf(',') + 1));
    if (binary.length > 10 * 1024 * 1024) throw new Error('Maximum file size: 10 MB.');
    const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
    const gif = parseGIF(bytes.buffer);
    const frames = gif.frames.filter(frame => frame.image);
    const { width, height } = gif.lsd;
    if (!width || !height || !frames.length) throw new Error('Invalid GIF.');
    if (width * height > 4_000_000 || frames.length > 1000 || width * height * frames.length > 80_000_000)
        throw new Error('GIF too large to edit. Use a smaller GIF.');
    const duration = frames.reduce((sum, frame) => sum + delayOf(frame), 0);
    cached = { gif, frames, width, height, duration };
    cachedSource = source;
    return cached;
}

async function trim(source, startSeconds, endSeconds, thumbnail = false, onFrame = null) {
    const { gif, frames, width, height, duration } = load(source);
    const start = Math.round(startSeconds * 1000 / 10) * 10;
    const end = Math.round(endSeconds * 1000 / 10) * 10;
    if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end > duration || end - start < 10)
        throw new Error('Choose a start before the end, within the GIF duration.');

    const screen = new Uint8ClampedArray(width * height * 4);
    const background = gif.gct?.[gif.lsd.backgroundColorIndex] || [0, 0, 0];
    const encoder = GIFEncoder();
    let previous, restore, clock = 0, written = 0;
    function clear(rect, transparent) {
        for (let y = rect.top; y < rect.top + rect.height; y++) {
            for (let x = rect.left; x < rect.left + rect.width; x++) {
                const pixel = (y * width + x) * 4;
                screen[pixel] = transparent ? 0 : background[0];
                screen[pixel + 1] = transparent ? 0 : background[1];
                screen[pixel + 2] = transparent ? 0 : background[2];
                screen[pixel + 3] = transparent ? 0 : 255;
            }
        }
    }

    for (const raw of frames) {
        if (clock >= end) break;
        // Earlier frames still need compositing: the first retained frame may only be a patch.
        const frame = decompressFrame(raw, gif.gct, true);
        const rect = frame.dims;
        if (!rect.width || !rect.height || rect.left + rect.width > width || rect.top + rect.height > height)
            throw new Error('Invalid GIF frame.');
        if (!previous) clear({ top: 0, left: 0, width, height }, frame.transparentIndex !== undefined);
        if (previous?.disposalType === 2) clear(previous.dims, previous.transparentIndex !== undefined);
        if (previous?.disposalType === 3 && restore) screen.set(restore);
        restore = frame.disposalType === 3 ? screen.slice() : undefined;
        for (let y = 0; y < rect.height; y++) {
            for (let x = 0; x < rect.width; x++) {
                const from = (y * rect.width + x) * 4;
                if (!frame.patch[from + 3]) continue;
                const to = ((rect.top + y) * width + rect.left + x) * 4;
                screen.set(frame.patch.subarray(from, from + 4), to);
            }
        }
        const next = clock + delayOf(raw);
        const retained = Math.min(end, next) - Math.max(start, clock);
        if (retained > 0) {
            // Full composited frames prevent dependencies on discarded frames. Reserve transparent
            // index zero on every frame, including opaque frames, so disposal clears consistently.
            let pixels = screen, outputWidth = width, outputHeight = height;
            if (thumbnail) {
                const scale = Math.min(1, 96 / Math.max(width, height));
                outputWidth = Math.max(1, Math.round(width * scale)); outputHeight = Math.max(1, Math.round(height * scale));
                pixels = new Uint8ClampedArray(outputWidth * outputHeight * 4);
                for (let y = 0; y < outputHeight; y++) for (let x = 0; x < outputWidth; x++) {
                    const from = (Math.min(height - 1, Math.floor(y / scale)) * width + Math.min(width - 1, Math.floor(x / scale))) * 4;
                    pixels.set(screen.subarray(from, from + 4), (y * outputWidth + x) * 4);
                }
            }
            if (onFrame) {
                await onFrame(screen, width, height, retained);
                written++;
                previous = frame;
                clock = next;
                continue;
            }
            const colors = quantize(pixels, 255, { format: 'rgba4444', oneBitAlpha: true });
            const palette = [[0, 0, 0, 0], ...colors.filter(color => color[3] !== 0)];
            const index = applyPalette(pixels, palette, 'rgba4444');
            encoder.writeFrame(index, outputWidth, outputHeight, { palette, delay: retained, transparent: true, transparentIndex: 0, dispose: 2, repeat: 0 });
            written++;
            if (thumbnail) break;
        }
        previous = frame;
        clock = next;
    }
    if (!written) throw new Error('No frames in this range.');
    if (onFrame) return;
    encoder.finish();
    const output = encoder.bytes();
    if (output.length > 10 * 1024 * 1024) throw new Error('Result exceeds 10 MB. Shorten the selection.');
    let binary = '';
    for (let i = 0; i < output.length; i += 32768) binary += String.fromCharCode(...output.subarray(i, i + 32768));
    return 'data:image/gif;base64,' + btoa(binary);
}

function makeCanvas(width, height) {
    if (typeof OffscreenCanvas !== 'undefined') return new OffscreenCanvas(width, height);
    const canvas = document.createElement('canvas');
    canvas.width = width; canvas.height = height;
    return canvas;
}

async function prepare(source) {
    const { duration } = load(source);
    if (duration > 10000)
        throw new Error('This GIF is longer than 10 seconds. Open Edit GIF, choose a section of at most 10 seconds, save, then prepare again.');
    const sourceCanvas = makeCanvas(1, 1);
    const canvas = makeCanvas(512, 512);
    for (const quality of [0.8, 0.55, 0.3, 0.1]) {
        const frames = [];
        let total = 0;
        try {
            await trim(source, 0, duration / 1000, false, async (pixels, width, height, delay) => {
                if (sourceCanvas.width !== width || sourceCanvas.height !== height) {
                    sourceCanvas.width = width; sourceCanvas.height = height;
                }
                sourceCanvas.getContext('2d').putImageData(new ImageData(pixels, width, height), 0, 0);
                fit(canvas.getContext('2d'), sourceCanvas, width, height);
                // Let iOS paint controls between frames when a worker canvas is unavailable.
                if (typeof document !== 'undefined') await new Promise(resolve => setTimeout(resolve, 0));
                const bytes = await encode(canvas, quality);
                total += bytes.length + 24;
                if (total > 500 * 1024) throw new Error('size');
                frames.push({ bytes, duration: delay });
            });
            const bytes = animation(frames);
            if (bytes.length <= 500 * 1024) return dataUrl(bytes);
        } catch (error) { if (error.message !== 'size') throw error; }
    }
    throw new Error('This animation still exceeds 500 KB. Open Edit GIF and trim a shorter section, then prepare again. Your GIF is unchanged.');
}

// The same engine can run on the main thread on iOS versions without OffscreenCanvas.
export async function process(data) {
    if (data.action === 'prepare') return prepare(data.source);
    if (data.action === 'inspect') {
        const gif = load(data.source);
        const thumbnails = await Promise.all(Array.from({ length: 6 }, (_, i) => {
            const start = Math.floor((gif.duration - 10) * i / 5 / 10) * 10;
            return trim(data.source, start / 1000, (start + 10) / 1000, true);
        }));
        return { duration: gif.duration / 1000, frameCount: gif.frames.length, thumbnails };
    }
    return trim(data.source, data.start, data.end);
}
