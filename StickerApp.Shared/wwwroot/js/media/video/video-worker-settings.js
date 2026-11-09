const key = 'stickers.videoWorkers';
export function normalizeWorkers(value) { value = Number(value); return [1,2,4].includes(value) ? value : 2; }
export function readWorkers() { try { return normalizeWorkers(localStorage.getItem(key)); } catch { return 2; } }
export function setWorkers(value) { value = normalizeWorkers(value); localStorage.setItem(key, String(value)); return value; }
export function effectiveWorkers(requested = readWorkers(), hardware = globalThis.navigator?.hardwareConcurrency ?? 2) {
    return Math.min(normalizeWorkers(requested), Math.max(1, Math.min(4, Math.floor(Number(hardware) || 2))));
}
