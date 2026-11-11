// Only near-viewport images request source bytes from .NET. Cached posters are
// small, static PNGs; GIF/WebP originals are never kept in this cache.
const memory = new Map(), pending = new Map();
let database, memoryBytes = 0;
const placeholder = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
async function db() {
    if (!globalThis.indexedDB) return null;
    database ??= new Promise(resolve => {
        const request = indexedDB.open('stikerin-thumbnails-v1', 1);
        request.onupgradeneeded = () => request.result.createObjectStore('posters', { keyPath: 'key' });
        request.onsuccess = () => { request.result.onversionchange = () => { request.result.close(); database = undefined; }; resolve(request.result); };
        request.onerror = request.onblocked = () => resolve(null);
    });
    return database;
}
async function read(key) {
    if (memory.has(key)) { const value = memory.get(key); memory.delete(key); memory.set(key, value); return value; }
    try {
        const connection = await db(); if (!connection) return null;
        return await new Promise(resolve => { const request = connection.transaction('posters').objectStore('posters').get(key); request.onsuccess = () => resolve(request.result?.source ?? null); request.onerror = () => resolve(null); });
    } catch { return null; }
}
function retain(key, source) {
    if (memory.has(key)) memoryBytes -= memory.get(key).length * 2;
    memory.set(key, source); memoryBytes += source.length * 2;
    while (memory.size > 128 || memoryBytes > 8 * 1024 * 1024) { const oldest = memory.keys().next().value; memoryBytes -= memory.get(oldest).length * 2; memory.delete(oldest); }
}
async function save(key, source) {
    retain(key, source);
    try {
        const connection = await db(); if (!connection) return;
        const transaction = connection.transaction('posters', 'readwrite'), store = transaction.objectStore('posters');
        const complete = new Promise(resolve => { transaction.oncomplete = transaction.onerror = transaction.onabort = resolve; });
        store.put({ key, source, size: source.length * 2, time: Date.now() });
        const request = store.getAll();
        request.onsuccess = () => {
            const records = request.result.sort((a, b) => b.time - a.time);
            let bytes = 0;
            for (const [index, record] of records.entries()) { bytes += record.size; if (index >= 256 || bytes > 16 * 1024 * 1024) store.delete(record.key); }
        };
        await complete;
    } catch { /* Cache failure must not prevent browsing or editing. */ }
}
async function poster(source, animated) {
    let image, reader;
    try {
        if (animated) { const { openAnimationPoster } = await import('../animation/animation-reader.js'); reader = await openAnimationPoster(source); image = reader.at(0); }
        else { image = new Image(); image.src = source; await image.decode(); }
        const canvas = document.createElement('canvas'); canvas.width = canvas.height = 192;
        const width = image.naturalWidth || image.width, height = image.naturalHeight || image.height;
        const scale = Math.min(192 / width, 192 / height);
        canvas.getContext('2d').drawImage(image, (192 - width * scale) / 2, (192 - height * scale) / 2, width * scale, height * scale);
        return canvas.toDataURL('image/png');
    } finally { reader?.dispose?.(); if (image instanceof HTMLImageElement) image.src = ''; }
}
// Bound concurrent original reads/decoders even when a large grid first appears.
let lanes = 0; const queue = [];
async function limited(action) {
    if (lanes >= 2) await new Promise(resolve => queue.push(resolve));
    lanes++;
    try { return await action(); } finally { lanes--; queue.shift()?.(); }
}
export function watch(image, reference, key, animated) {
    let disposed = false, loading = false, near = false;
    image.dataset.thumbnailState = 'waiting';
    // Preserve the poster when reattaching the same item; a different key must
    // never briefly show the previous item's artwork.
    if (image.dataset.thumbnailBoundKey !== key || !image.getAttribute('src')) image.src = placeholder;
    image.dataset.thumbnailBoundKey = key;
    const load = async () => {
        if (disposed || loading || !near || document.hidden) return;
        loading = true;
        try {
            let source = await read(key), hit = !!source;
            if (!source) {
                if (!pending.has(key)) {
                    const work = limited(async () => {
                        if (disposed || !near || document.hidden) return null;
                        const source = await reference.invokeMethodAsync('LoadThumbnailSource');
                        if (disposed || document.hidden) return null;
                        const result = await poster(source, animated); await save(key, result); return result;
                    });
                    pending.set(key, work); work.finally(() => { if (pending.get(key) === work) pending.delete(key); }).catch(() => {});
                }
                source = await pending.get(key);
            }
            if (source && !disposed) { retain(key, source); image.src = source; image.dataset.thumbnailState = 'ready'; image.dataset.thumbnailCache = hit ? 'hit' : 'miss'; }
        } catch { if (!disposed) image.dataset.thumbnailState = 'error'; }
        finally { loading = false; if (!disposed && near && image.dataset.thumbnailState === "waiting" && !document.hidden) requestAnimationFrame(load); }
    };
    const observer = new IntersectionObserver(entries => {
        near = entries.some(entry => entry.isIntersecting);
        if (near && image.dataset.thumbnailState !== 'ready') load();
    }, { rootMargin: '100px' });
    const visible = () => { if (!document.hidden && near && image.dataset.thumbnailState !== 'ready') load(); };
    observer.observe(image); document.addEventListener('visibilitychange', visible);
    return { dispose() { disposed = true; observer.disconnect(); document.removeEventListener('visibilitychange', visible); } };
}
