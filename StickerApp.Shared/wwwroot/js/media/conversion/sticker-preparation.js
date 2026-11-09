import { prepare as prepareOnMain } from './sticker-converter.js';
import { requireForeground, onForegroundChanged, startJob } from '../../ui/foreground.js';
let active;
onForegroundChanged(value => { if (!value) cancel(); });
export function cancel() {
    if (!active) return;
    active.controller.abort(); active.worker?.terminate(); clearTimeout(active.timer);
    active.reject?.(new Error('Preparation cancelled.')); active.finish(); active = undefined;
}
export async function prepare(source, animated) {
    requireForeground();
    if (animated && !source.startsWith('data:image/gif;')) throw new Error('This animated WebP needs its original GIF to resize or compress it. Open Edit and replace it with that GIF.');
    const job = { controller: new AbortController(), finish: startJob('Preparing sticker…') };
    active = job;
    try {
        if (animated && typeof OffscreenCanvas === 'undefined') {
            const engine = await import('../codecs/gif-engine.js');
            job.controller.signal.throwIfAborted();
            return await engine.process({ action: 'prepare', source, signal: job.controller.signal });
        }
        if (!animated && (typeof OffscreenCanvas === 'undefined' || typeof createImageBitmap === 'undefined')) {
            const result = await prepareOnMain(source, false);
            job.controller.signal.throwIfAborted(); return result;
        }
        return await new Promise((resolve, reject) => {
            job.reject = reject;
            const worker = job.worker = new Worker(new URL(animated ? '../codecs/gif-worker.js' : './sticker-preparation-worker.js', import.meta.url), { type: 'module' });
            job.timer = setTimeout(() => reject(new Error('Preparation took too long. Try a smaller image or trim the GIF.')), 90000);
            worker.onmessage = ({ data }) => data.error ? reject(new Error(data.error)) : resolve(data.value);
            worker.onerror = () => reject(new Error('Could not prepare this image. Open Edit to adjust or replace it.'));
            worker.postMessage({ id: 1, action: 'prepare', source });
        });
    } finally {
        job.worker?.terminate(); clearTimeout(job.timer); job.finish(); if (active === job) active = undefined;
    }
}
