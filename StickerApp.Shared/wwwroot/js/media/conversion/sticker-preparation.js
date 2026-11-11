import { read as mediaSettings } from './media-settings.js';
import { conversionKey, cachedConversion, cacheConversion } from './result-cache.js';
import { prepare as prepareOnMain } from './sticker-converter.js';
import { requireForeground, onForegroundChanged, startJob, startJobBatch } from '../../ui/foreground.js';
let active, batch;
export function beginBatch(names) { batch?.finish(); batch = startJobBatch(names); }
export function endBatch() { batch?.finish(); batch = undefined; }
onForegroundChanged(value => { if (!value) cancel(); });
export function cancel() {
    if (!active) return;
    active.controller.abort(); active.worker?.terminate(); clearTimeout(active.timer);
    active.reject?.(new Error('Preparation cancelled.')); active.finish(); active = undefined;
}
export async function prepare(source, animated, reference, batchIndex) {
    requireForeground();
    const job = { controller: new AbortController(), finish: batch && Number.isInteger(batchIndex) ? batch.job(batchIndex) : startJob('Preparing sticker…') };
    active = job;
    let lastProgress;
    const progress = value => {
        if (active !== job || job.controller.signal.aborted) return;
        const key = `${value.stage}:${value.attempt}:${value.percent}`;
        if(lastProgress===key)return;lastProgress=key;
        job.finish.progress(value);
        reference?.invokeMethodAsync('PreparationProgress', value).catch(() => {});
    };
    try {
        const settings = mediaSettings();
        const key = await conversionKey(source, { action: 'prepare', animated, settings });
        job.controller.signal.throwIfAborted();
        const cached = cachedConversion(key);
        if (cached) return cached;
        const remember = value => { job.controller.signal.throwIfAborted(); cacheConversion(key, value); return value; };
        if (animated && typeof OffscreenCanvas === 'undefined') {
            const engine = await import('../codecs/gif-engine.js');
            job.controller.signal.throwIfAborted();
            return remember(await engine.process({ action: 'prepare', source, signal: job.controller.signal, onProgress: progress }));
        }
        if (!animated && (typeof OffscreenCanvas === 'undefined' || typeof createImageBitmap === 'undefined')) {
            const result = await prepareOnMain(source, false, job.controller.signal);
            job.controller.signal.throwIfAborted(); return remember(result);
        }
        return remember(await new Promise((resolve, reject) => {
            job.reject = reject;
            const worker = job.worker = new Worker(new URL(animated ? '../codecs/gif-worker.js' : './sticker-preparation-worker.js', import.meta.url), { type: 'module' });
            job.timer = setTimeout(() => reject(new Error('Preparation took too long. Try a smaller image or trim the GIF.')), 180000);
            worker.onmessage = ({ data }) => data.progress ? progress(data.progress) : data.error ? reject(new Error(data.error)) : resolve(data.value);
            worker.onerror = () => reject(new Error('Could not prepare this image. Open Edit to adjust or replace it.'));
            worker.postMessage({ id: 1, action: 'prepare', source, settings });
        }));
    } finally {
        job.worker?.terminate(); clearTimeout(job.timer); job.finish(); if (active === job) active = undefined;
    }
}
