import { samePixels, ensureAnimationFrames } from '../../wwwroot/js/media/shared/frame-dedup.js';
import { drawLayerArtwork } from '../../wwwroot/js/media/effects/object-outline.js';
import { applyColorEffect } from '../../wwwroot/js/media/effects/color-effect.js';
import { fit, encode, encodeWorkingFrame, animation, dataUrl } from '../../wwwroot/js/media/conversion/sticker-converter.js';
import { parseGIF, decompressFrame } from 'gifuct-js';
import { openAnimation } from '../../wwwroot/js/media/animation/animation-reader.js';
import { createRasterRenderer } from '../../wwwroot/js/media/effects/raster-edits.js';
import { GIFEncoder, quantize, applyPalette } from 'gifenc';

let cachedSource, cached;
const delayOf = frame => Math.max(20, (frame.gce?.delay || 10) * 10);

function load(source) {
    if (source === cachedSource) return cached;
    if (!source.startsWith('data:image/gif;base64,')) throw new Error('Select a GIF.');
    const binary = atob(source.slice(source.indexOf(',') + 1));
    if (binary.length > 20 * 1024 * 1024) throw new Error('Maximum file size: 20 MiB.');
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

async function trim(source, startSeconds, endSeconds, thumbnail = false, onFrame = null, signal) {
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
        signal?.throwIfAborted();
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
    if (output.length > 20 * 1024 * 1024) throw new Error('Result exceeds 20 MiB. Shorten the selection.');
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

async function prepare(source, signal, onProgress) {
    const reader = await openAnimation(source);
    const recipe = { width: reader.width, height: reader.height, edits: [] }, duration = reader.duration;
    reader.dispose?.();
    return editAnimation(source, 0, duration / 1000, recipe, false, signal, onProgress);
}

// Build all six timeline previews while compositing the animation once. Calling
// trim separately for each preview repeatedly decoded every preceding GIF patch.
async function inspect(source) {
    const gif = load(source);
    const targets = Array.from({ length: 6 }, (_, i) => Math.floor((gif.duration - 10) * i / 5 / 10) * 10);
    const thumbnails = [];
    let clock = 0, frameCount = 0;
    await trim(source, 0, gif.duration / 1000, false, async (screen, width, height, delay) => {
        const end = clock + delay;
        if (thumbnails.length < targets.length && targets[thumbnails.length] < end) {
            const scale = Math.min(1, 96 / Math.max(width, height));
            const outputWidth = Math.max(1, Math.round(width * scale));
            const outputHeight = Math.max(1, Math.round(height * scale));
            const pixels = new Uint8ClampedArray(outputWidth * outputHeight * 4);
            for (let y = 0; y < outputHeight; y++) for (let x = 0; x < outputWidth; x++) {
                const from = (Math.min(height - 1, Math.floor(y / scale)) * width + Math.min(width - 1, Math.floor(x / scale))) * 4;
                pixels.set(screen.subarray(from, from + 4), (y * outputWidth + x) * 4);
            }
            const canvas = makeCanvas(outputWidth, outputHeight);
            canvas.getContext('2d').putImageData(new ImageData(pixels, outputWidth, outputHeight), 0, 0);
            const blob = canvas.convertToBlob
                ? await canvas.convertToBlob({ type: 'image/png' })
                : await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
            if (!blob) throw new Error('Could not create GIF previews.');
            const bytes = new Uint8Array(await blob.arrayBuffer());
            let binary = '';
            for (let i = 0; i < bytes.length; i += 32768) binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
            const preview = 'data:image/png;base64,' + btoa(binary);
            while (thumbnails.length < targets.length && targets[thumbnails.length] < end) thumbnails.push(preview);
        }
        clock = end;
        // Keep the loading indicator responsive on older iOS without worker canvases.
        if (typeof document !== 'undefined' && ++frameCount % 8 === 0)
            await new Promise(resolve => setTimeout(resolve, 0));
    });
    return { duration: gif.duration / 1000, frameCount: gif.frames.length, thumbnails };
}

// Incremental, disposal-aware GIF decoding, shared by live layer preview and export.
export function createReader(source) {
    const { gif, frames, width, height, duration } = load(source);
    const canvas = makeCanvas(width, height), context = canvas.getContext('2d');
    const screen = new Uint8ClampedArray(width * height * 4);
    const background = gif.gct?.[gif.lsd.backgroundColorIndex] || [0, 0, 0];
    let index = 0, end = 0, previous, restore, lastTime = -1, dirty = true;
    function clear(rect, transparent) {
        for (let y = rect.top; y < rect.top + rect.height; y++) for (let x = rect.left; x < rect.left + rect.width; x++) {
            const offset = (y * width + x) * 4;
            screen[offset] = transparent ? 0 : background[0]; screen[offset + 1] = transparent ? 0 : background[1];
            screen[offset + 2] = transparent ? 0 : background[2]; screen[offset + 3] = transparent ? 0 : 255;
        }
    }
    return { width, height, duration, frameCount: frames.length, frameDurations: frames.map(delayOf),
        at(time) {
            time = Math.max(0, Math.min(duration - 1, time));
            if (time < lastTime) { index = 0; end = 0; previous = restore = undefined; screen.fill(0); }
            lastTime = time;
            while (index < frames.length && time >= end) {
                const raw = frames[index++], frame = decompressFrame(raw, gif.gct, true), rect = frame.dims;
                if (!rect.width || !rect.height || rect.left + rect.width > width || rect.top + rect.height > height) throw new Error('Invalid GIF frame.');
                if (!previous) clear({ top: 0, left: 0, width, height }, frame.transparentIndex !== undefined);
                if (previous?.disposalType === 2) clear(previous.dims, previous.transparentIndex !== undefined);
                if (previous?.disposalType === 3 && restore) screen.set(restore);
                restore = frame.disposalType === 3 ? screen.slice() : undefined;
                for (let y = 0; y < rect.height; y++) for (let x = 0; x < rect.width; x++) {
                    const from = (y * rect.width + x) * 4;
                    if (frame.patch[from + 3]) screen.set(frame.patch.subarray(from, from + 4), ((rect.top + y) * width + rect.left + x) * 4);
                }
                end += delayOf(raw); previous = frame; dirty = true;
            }
            if (dirty) { context.putImageData(new ImageData(screen, width, height), 0, 0); dirty = false; }
            return canvas;
        }
    };
}

async function compose(composition, signal, onProgress) {
    const layers = composition.layers;
    const readers = new Map(), stills = new Map();
    const duration = Math.min(10000, Math.max(...layers.filter(layer => layer.gifSource).map(layer => (layer.trimEnd - layer.trimStart) * 1000)));
    if (!Number.isFinite(duration) || duration < 20) throw new Error('Choose an animated layer selection first.');
    try {
        for (const layer of layers) {
            if (layer.gifSource) readers.set(layer.id, await openAnimation(layer.gifSource));
            else if (typeof createImageBitmap !== 'undefined') stills.set(layer.id, await createImageBitmap(await (await fetch(layer.source)).blob()));
            else { const image = new Image(); image.src = layer.source; await image.decode(); stills.set(layer.id, image); }
        }
        const canvas = makeCanvas(512, 512), context = canvas.getContext('2d');
        const tintCanvas = makeCanvas(1, 1), tintContext = tintCanvas.getContext('2d');
        const changes = new Set([0, Math.round(duration)]);
        for (const layer of layers.filter(layer => layer.gifSource)) {
            const reader = readers.get(layer.id), start = layer.trimStart * 1000, length = (layer.trimEnd - layer.trimStart) * 1000;
            for (let repeat = 0; repeat < duration; repeat += length) {
                let time = 0;
                for (const delay of reader.frameDurations) {
                    const change = repeat + time - start;
                    if (time >= start && time < start + length && change < duration) changes.add(Math.round(change));
                    time += delay;
                }
                if (repeat > 0) changes.add(Math.round(repeat));
            }
        }
        const times = [...changes].filter(time => time >= 0 && time <= duration).sort((a,b) => a-b);
        if (times.length === 2) times.splice(1,0,Math.round(duration/2));
        const frameCount = times.length - 1;
        const attempts = [{quality:.65,detail:512},{quality:.4,detail:512},{quality:.2,detail:512},{quality:.08,detail:512},{quality:.2,detail:384},{quality:.15,detail:256},{quality:.1,detail:192},{quality:.08,detail:128}];
        const detailCanvas=makeCanvas(1,1);
        for (const [attemptIndex,{quality,detail}] of attempts.entries()) {
            const output = []; let total = 0;
            for (let frameIndex = 0; frameIndex < frameCount; frameIndex++) {
                signal?.throwIfAborted();
                onProgress?.({percent:Math.floor(frameIndex/frameCount*100),completed:frameIndex,total:frameCount,attempt:attemptIndex+1,attempts:attempts.length});
                const clock = times[frameIndex];
                context.clearRect(0, 0, 512, 512);
                for (const layer of layers) {
                    let source = layer.gifSource ? readers.get(layer.id).at(layer.trimStart * 1000 + clock % ((layer.trimEnd - layer.trimStart) * 1000)) : stills.get(layer.id);
                    if (layer.tint || layer.hue && layer.colorStrength !== 0) {
                        tintCanvas.width = source.width; tintCanvas.height = source.height;
                        tintContext.globalCompositeOperation = 'source-over'; tintContext.drawImage(source, 0, 0);
                        applyColorEffect(tintContext, layer);
                        source = tintCanvas;
                    }
                    context.save(); context.globalAlpha = layer.opacity; context.translate(layer.x, layer.y);
                    context.rotate(layer.rotation * Math.PI / 180); context.scale(layer.flipX ? -1 : 1, 1);
                    drawLayerArtwork(context,source,layer); context.restore();
                }
                if(detail<512) {
                    detailCanvas.width=detailCanvas.height=detail;detailCanvas.getContext('2d').drawImage(canvas,0,0,detail,detail);
                    context.clearRect(0,0,512,512);context.drawImage(detailCanvas,0,0,512,512);
                }
                if (typeof document !== 'undefined' && typeof window !== 'undefined') await new Promise(resolve => setTimeout(resolve, 0));
                const bytes = await encode(canvas, quality); total += bytes.length + 24;
                if (total > 500 * 1024) break;
                output.push({ bytes, duration: times[frameIndex + 1] - clock });
            }
            if (total <= 500 * 1024) {
                const result = animation(output);
                if (result.length <= 500 * 1024) { onProgress?.({percent:100,completed:frameCount,total:frameCount,attempt:attemptIndex+1,attempts:attempts.length}); return dataUrl(result); }
            }
        }
        throw new Error('This animation still exceeds 500 KB after reducing quality and detail. Try simpler layers or a shorter selection.');
    } finally { for (const image of stills.values()) image.close?.(); for (const reader of readers.values()) reader.dispose?.(); }
}

async function inspectAnimation(source) {
    if (source.startsWith('data:image/gif;base64,')) return inspect(source);
    const reader = await openAnimation(source);
    try {
        const thumbnails = [];
        for (let index = 0; index < 6; index++) {
            const frame = reader.at((reader.duration - 1) * index / 5), canvas = makeCanvas(96, 96);
            const scale = Math.min(96 / frame.width, 96 / frame.height);
            canvas.getContext('2d').drawImage(frame, (96 - frame.width * scale) / 2, (96 - frame.height * scale) / 2, frame.width * scale, frame.height * scale);
            const blob = canvas.convertToBlob ? await canvas.convertToBlob({ type: 'image/png' }) : await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
            const bytes = new Uint8Array(await blob.arrayBuffer());
            let binary = ''; for (let i = 0; i < bytes.length; i += 32768) binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
            thumbnails.push('data:image/png;base64,' + btoa(binary));
        }
        return { duration: reader.duration / 1000, frameCount: reader.frameCount, thumbnails };
    } finally { reader.dispose?.(); }
}

async function editAnimation(source, startSeconds, endSeconds, recipe, working = false, signal, onProgress) {
    const reader = await openAnimation(source), renderer = await createRasterRenderer(recipe);
    try {
        const start = Math.round(startSeconds * 1000), end = Math.round(endSeconds * 1000);
        if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end > reader.duration + 1 || end - start < 16 || end - start > 10000)
            throw new Error('Choose an animation selection between 0.02 and 10 seconds.');
        let clock = 0;
        const times = [];
        for (const delay of reader.frameDurations) {
            const retained = Math.min(end, clock + delay) - Math.max(start, clock);
            if (retained > 0) times.push({ time: Math.max(start, clock), duration: retained });
            clock += delay; if (clock >= end) break;
        }
        for (let index = 0; index < times.length && times.length > 1;) {
            if (times[index].duration >= 8) { index++; continue; }
            const item = times.splice(index, 1)[0];
            times[index > 0 ? index - 1 : 0].duration += item.duration;
        }
        if (times.length === 1) { const item = times.pop(); const half = Math.floor(item.duration / 2); times.push({ time: item.time, duration: half }, { time: item.time, duration: item.duration - half }); }
        const canvas = makeCanvas(512, 512), context = canvas.getContext('2d');
        let visible = false;
        const limit = working ? 20 * 1024 * 1024 : 500 * 1024;
        const attempts = working ? [{quality:0.9, detail:512}] : [
            {quality:0.8,detail:512},{quality:0.55,detail:512},{quality:0.3,detail:512},{quality:0.1,detail:512},
            {quality:0.2,detail:384},{quality:0.15,detail:256},{quality:0.1,detail:192},{quality:0.08,detail:128}
        ];
        const detailCanvas = makeCanvas(1,1);
        for (const [attemptIndex, {quality, detail}] of attempts.entries()) {
            const output = []; let total = 0, previousPixels;
            for (const [frameIndex, item] of times.entries()) {
                onProgress?.({ percent: Math.floor(frameIndex / times.length * 100), completed: frameIndex, total: times.length, attempt: attemptIndex + 1, attempts: attempts.length });
                if (signal?.aborted) throw new Error('Animation editor closed.');
                const frame = renderer.render(reader.at(item.time));
                if (recipe.layer) {
                    canvas.width = frame.width; canvas.height = frame.height; context.drawImage(frame, 0, 0);
                } else fit(context, frame, frame.width, frame.height);
                if (detail < 512) {
                    detailCanvas.width = Math.max(1, Math.round(canvas.width * detail / 512));
                    detailCanvas.height = Math.max(1, Math.round(canvas.height * detail / 512));
                    detailCanvas.getContext('2d').drawImage(canvas,0,0,detailCanvas.width,detailCanvas.height);
                    context.clearRect(0,0,canvas.width,canvas.height);
                    context.drawImage(detailCanvas,0,0,canvas.width,canvas.height);
                }
                if (!visible) visible = context.getImageData(0, 0, canvas.width, canvas.height).data.some((value, index) => index % 4 === 3 && value > 0);
                const pixels=context.getImageData(0,0,canvas.width,canvas.height).data;
                if(samePixels(previousPixels,pixels)) { output[output.length-1].duration+=item.duration; continue; }
                previousPixels=pixels.slice();
                if (typeof document !== 'undefined') await new Promise(resolve => setTimeout(resolve, 0));
                const bytes = await (working ? encodeWorkingFrame(canvas, quality) : encode(canvas, quality)); total += bytes.length + 24;
                if (signal?.aborted) throw new Error('Animation editor closed.');
                if (total > limit) break;
                output.push({ bytes, duration: item.duration });
            }
            if (total <= limit) {
                if (!visible) throw new Error('The animation is completely transparent. Undo a cutout or increase opacity.');
                const bytes = animation(ensureAnimationFrames(output), canvas.width, canvas.height); if (bytes.length <= limit) { onProgress?.({percent:100,completed:times.length,total:times.length,attempt:attemptIndex+1,attempts:attempts.length}); return dataUrl(bytes); }
            }
        }
        throw new Error(working ? 'This GIF exceeds the 20 MiB import limit. Choose a smaller GIF.' : 'This animation still exceeds 500 KB after reducing quality and detail. Try a simpler image or a shorter selection.');
    } finally { reader.dispose?.(); renderer.dispose(); }
}

// The same engine can run on the main thread on iOS versions without OffscreenCanvas.
export async function process(data) {
    if (data.action === 'compose') return compose(data.composition, data.signal, data.onProgress);
    if (data.action === 'prepare') {
        if (data.source.startsWith('data:image/gif;')) return prepare(data.source, data.signal, data.onProgress);
        const reader = await openAnimation(data.source);
        const recipe = { width: reader.width, height: reader.height, edits: [] }, duration = reader.duration;
        reader.dispose?.(); return editAnimation(data.source, 0, duration / 1000, recipe, false, data.signal, data.onProgress);
    }
    if (data.action === 'inspect') return inspectAnimation(data.source);
    if (data.action === 'stage') return editAnimation(data.source, data.start, data.end, data.composition, true, data.signal, data.onProgress);
    if (data.action === 'edit') return editAnimation(data.source, data.start, data.end, data.composition, false, data.signal, data.onProgress);
    return trim(data.source, data.start, data.end, false, null, data.signal);
}
