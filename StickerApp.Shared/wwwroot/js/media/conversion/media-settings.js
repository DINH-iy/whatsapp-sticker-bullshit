// Snapshot settings into worker messages; workers have no localStorage.
let current;
export function normalize(value = {}) {
    const limit = Number(value.gifMiB), quality = Number(value.quality);
    return { gifMiB: Number.isFinite(limit) && limit >= 5 && limit <= 100 ? Math.round(limit) : 20,
        quality: [.55, .75, .9].includes(quality) ? quality : .9 };
}
export function read() {
    if (typeof localStorage === 'undefined') return current ?? normalize();
    try { return normalize(JSON.parse(localStorage.getItem('stickers.mediaSettings') || '{}')); } catch { return current ?? normalize(); }
}
export function write(value) { current = normalize(value); localStorage.setItem('stickers.mediaSettings', JSON.stringify(current)); return current; }
export function configure(value) { current = normalize(value); }
export function gifLimitBytes() { return read().gifMiB * 1024 * 1024; }
export function qualityCeiling() { return read().quality; }

export function syncGifLimit(bytes) {
    const value = { ...read(), gifMiB: bytes / (1024 * 1024) };
    try { write(value); } catch { current = normalize(value); }
}
