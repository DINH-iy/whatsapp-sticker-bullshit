// Probe representative frames before committing to a complete compression pass.
// Three normalized frames (at most 12 MiB) and 4 MiB of encoded probes are the
// entire cache; full decoded animations are never retained by this search.
import { samePixels } from '../shared/frame-dedup.js';
const canvasOf = (width, height) => {
    if (typeof OffscreenCanvas !== 'undefined') return new OffscreenCanvas(width, height);
    const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height; return canvas;
};
export const stickerAttempts = [
    { quality: .8, detail: 512 }, { quality: .55, detail: 512 }, { quality: .3, detail: 512 }, { quality: .1, detail: 512 },
    { quality: .2, detail: 384 }, { quality: .15, detail: 256 }, { quality: .1, detail: 192 }, { quality: .08, detail: 128 }
];
export function compressionSearch({ count, attempts, limit, render, encode, signal, progress }) {
    const indices = count >= 6 && attempts.length > 1 ? [...new Set([0, Math.floor((count - 1) / 2), count - 1])] : [];
    const sources = new Map(), encoded = new Map();
    let bytesRetained = 0, encodedFrames = 0, selected = 0;
    const canvas = canvasOf(1, 1), reduced = canvasOf(1, 1);
    function frame(index, detail) {
        signal?.throwIfAborted();
        let source = sources.get(index);
        if (!source) {
            source = render(index);
            if (indices.includes(index) && source.width * source.height <= 1024 * 1024) {
                const copy = canvasOf(source.width, source.height); copy.getContext('2d').drawImage(source, 0, 0);
                sources.set(index, copy); source = copy;
            }
        }
        canvas.width = source.width; canvas.height = source.height;
        if (detail < 512) {
            reduced.width = Math.max(1, Math.round(source.width * detail / 512)); reduced.height = Math.max(1, Math.round(source.height * detail / 512));
            reduced.getContext('2d').drawImage(source, 0, 0, reduced.width, reduced.height);
            canvas.getContext('2d').drawImage(reduced, 0, 0, canvas.width, canvas.height);
        } else canvas.getContext('2d').drawImage(source, 0, 0);
        return canvas;
    }
    const report = (attempt, completed, stage) => progress?.({ percent: Math.floor(completed / (stage === 'sampling' ? indices.length : count) * 100), completed, total: stage === 'sampling' ? indices.length : count, attempt: attempt + 1, attempts: attempts.length, stage, encodedFrames, skippedAttempts: selected });
    async function bytes(index, attempt, artwork) {
        const key = `${attempt}:${index}`;
        if (encoded.has(key)) return encoded.get(key);
        signal?.throwIfAborted();
        // Yield for cancellation, controls and foreground events on old WebViews.
        if (typeof document !== 'undefined') await new Promise(resolve => setTimeout(resolve, 0));
        signal?.throwIfAborted();
        const output = await encode(artwork ?? frame(index, attempts[attempt].detail), attempts[attempt].quality);
        encodedFrames++; signal?.throwIfAborted();
        if (indices.includes(index) && bytesRetained + output.length <= 4 * 1024 * 1024) { encoded.set(key, output); bytesRetained += output.length; }
        return output;
    }
    return {
        frame, bytes, report,
        async start() {
            if (count < 6 || attempts.length === 1) return 0;
            for (let attempt = 0; attempt < attempts.length - 1; attempt++) {
                let total = 0;
                report(attempt, 0, 'sampling');
                for (const [sample, index] of indices.entries()) {
                    total += (await bytes(index, attempt)).length + 24;
                    report(attempt, sample + 1, 'sampling');
                }
                if (attempt === 0 && sources.size === indices.length) {
                    const pixels = [...sources.values()].map(source => source.getContext('2d').getImageData(0, 0, source.width, source.height).data);
                    if (pixels.slice(1).every(value => samePixels(pixels[0], value))) return 0;
                }
                // Deliberately conservative: estimates only select a starting
                // pass. Exact final size and complete frame timing still win.
                if (total / indices.length * count * 1.1 <= limit) { selected = attempt; return attempt; }
            }
            selected = attempts.length - 1; return selected;
        },
        dispose() { for (const source of sources.values()) source.width = source.height = 1; sources.clear(); encoded.clear(); canvas.width = canvas.height = reduced.width = reduced.height = 1; }
    };
}
