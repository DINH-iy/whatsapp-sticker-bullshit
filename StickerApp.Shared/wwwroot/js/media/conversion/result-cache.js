// Successful outputs only. Keys contain digests, never retained source strings.
const entries = new Map();
const budget = 4 * 1024 * 1024;
let retained = 0;
export async function conversionKey(source, settings) {
    if (!globalThis.crypto?.subtle) return null;
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(source + '\n' + JSON.stringify(settings)));
    return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, '0')).join('');
}
export function cachedConversion(key) {
    const entry = entries.get(key);
    if (!entry) return null;
    entries.delete(key); retained -= entry.size;
    if (Date.now() - entry.time > 5 * 60_000) return null;
    entries.set(key, entry); retained += entry.size;
    return entry.value;
}
export function cacheConversion(key, value) {
    if (!key || typeof value !== 'string' || !value.startsWith('data:image/webp;base64,')) return;
    const size = value.length * 2;
    if (size > budget) return;
    const previous = entries.get(key);
    if (previous) { retained -= previous.size; entries.delete(key); }
    entries.set(key, { value, size, time: Date.now() }); retained += size;
    while (retained > budget || entries.size > 8) { const oldest = entries.keys().next().value; retained -= entries.get(oldest).size; entries.delete(oldest); }
}
