// Transfer pixel buffers rather than copy them. Callers bound the in-flight queue.
export function createFramePool(count, signal) {
    if (typeof Worker === 'undefined') return null;
    const workers = [], queue = [], pending = new Map();
    let sequence = 0, disposed = false, failure;
    function stop(error = new DOMException('Cancelled', 'AbortError')) {
        if (disposed) return; disposed = true; failure = error;
        signal?.removeEventListener('abort', aborted);
        for (const worker of workers) worker.terminate();
        for (const job of pending.values()) { clearTimeout(job.timer); job.reject(error); }
        pending.clear(); queue.length = 0;
    }
    const aborted = () => stop(new DOMException('Cancelled', 'AbortError'));
    function assign(worker) {
        if (disposed || worker.busy || !queue.length) return;
        const job = queue.shift(); worker.busy = true;
        worker.postMessage({ id: job.id, pixels: job.pixels, width: job.width, height: job.height, quality: job.quality }, [job.pixels.buffer]);
    }
    try {
        for (let i = 0; i < count; i++) {
            const worker = new Worker(new URL('./video-frame-worker.js', import.meta.url), { type: 'module' });
            worker.onmessage = ({ data }) => {
                const job = pending.get(data.id); if (!job) return;
                if (data.error) { stop(new Error(data.error)); return; }
                pending.delete(data.id); clearTimeout(job.timer); worker.busy = false;
                job.resolve(data.bytes); assign(worker);
            };
            worker.onerror = () => stop(new Error('A video conversion worker failed. Try one worker in Settings → Editing.'));
            workers.push(worker);
        }
    } catch { stop(); return null; }
    signal?.addEventListener('abort', aborted, { once: true });
    if (signal?.aborted) aborted();
    return {
        count: workers.length,
        encode(pixels, width, height, quality) {
            if (disposed) return Promise.reject(failure);
            const id = ++sequence;
            const promise = new Promise((resolve, reject) => {
                const timer = setTimeout(() => stop(new Error('Frame compression timed out. Try a shorter clip or fewer workers.')), 30000);
                const job = { id, pixels, width, height, quality, resolve, reject, timer }; pending.set(id, job); queue.push(job);
                for (const worker of workers) assign(worker);
            });
            // Later frames can fail before the caller reaches them in display order.
            promise.catch(() => {}); return promise;
        },
        dispose: stop
    };
}
