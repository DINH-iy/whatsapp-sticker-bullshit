import { prepare as prepareOnMain } from './sticker-converter.js';

let active;
export function cancel() {
    if (!active) return;
    active.worker.terminate();
    clearTimeout(active.timer);
    active.reject(new Error('Preparation cancelled.'));
    active = undefined;
}

export function prepare(source, animated) {
    if (animated && !source.startsWith('data:image/gif;'))
        return Promise.reject(new Error('This animated WebP needs its original GIF to resize or compress it. Open Edit and replace it with that GIF.'));
    if (!animated && (typeof OffscreenCanvas === 'undefined' || typeof createImageBitmap === 'undefined'))
        return prepareOnMain(source, false);
    return new Promise((resolve, reject) => {
        const worker = new Worker(new URL(animated ? './gif-worker.js' : './sticker-preparation-worker.js', import.meta.url), { type: 'module' });
        const finish = (value, error) => {
            worker.terminate();
            clearTimeout(job.timer);
            if (active === job) active = undefined;
            if (error) reject(new Error(error)); else resolve(value);
        };
        const job = { worker, reject, timer: setTimeout(() => finish(null, 'Preparation took too long. Try a smaller image or trim the GIF.'), 90000) };
        active = job;
        worker.onmessage = ({ data }) => finish(data.value, data.error);
        worker.onerror = () => finish(null, 'Could not prepare this image. Open Edit to adjust or replace it.');
        worker.postMessage({ id: 1, action: 'prepare', source });
    });
}
