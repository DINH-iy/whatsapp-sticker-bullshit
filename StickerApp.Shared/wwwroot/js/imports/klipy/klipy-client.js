import { trackRequest } from '../../ui/foreground.js';
// KLIPY requires API calls and media loads to come directly from the user's device.
// Keep the API key here: it never needs to pass through the Blazor server or library files.
const apiBaseUrl = 'https://api.klipy.com/api/v1/';
const apiKeyStorageKey = 'stickers.klipyApiKey';
let activeSearch = null;

function readApiKey() {
    try {
        return localStorage.getItem(apiKeyStorageKey) || '';
    } catch {
        throw new Error('Device storage is unavailable. Enable local storage to configure GIF search.');
    }
}

export function hasApiKey() {
    return readApiKey().length > 0;
}

export function saveApiKey(input) {
    const apiKey = input.value.trim();
    if (!apiKey) throw new Error('Paste your KLIPY API key first.');

    try {
        localStorage.setItem(apiKeyStorageKey, apiKey);
    } catch {
        throw new Error('Could not save the API key on this device.');
    }

    input.value = '';
}

export function removeApiKey() {
    try {
        localStorage.removeItem(apiKeyStorageKey);
    } catch {
        throw new Error('Could not remove the API key from this device.');
    }
}

export function cancelSearch() {
    activeSearch?.abort();
}

function mediaUrl(value) {
    // Preserve provider URLs exactly, while rejecting unexpected schemes or hosts.
    if (!value) return '';
    try {
        const url = new URL(value);
        const isKlipyMedia = /^static\d*\.klipy\.com$/i.test(url.hostname);
        return url.protocol === 'https:' && !url.port && !url.username && !url.password && isKlipyMedia ? value : '';
    } catch {
        return '';
    }
}

function mapGif(item) {
    const formats = item.file || {};
    const preview = formats.sm?.gif || formats.xs?.gif || formats.md?.gif || formats.hd?.gif;
    const fullSize = formats.hd?.gif || formats.md?.gif || preview;

    return {
        slug: item.slug || '',
        title: item.title || 'Untitled GIF',
        previewUrl: mediaUrl(preview?.url),
        shareUrl: mediaUrl(fullSize?.url)
    };
}

export async function search(query, page = 1) {
    const apiKey = readApiKey();
    if (!apiKey) throw new Error('Add your KLIPY API key in Settings to enable GIF search.');

    cancelSearch();
    const controller = new AbortController();
    const finishRequest = trackRequest(controller);
    activeSearch = controller;
    const timeout = setTimeout(() => controller.abort(), 15000);
    if (!query.trim()) throw new Error('Enter a search term.');
    const url = new URL(`${apiBaseUrl}${encodeURIComponent(apiKey)}/gifs/search`);
    url.searchParams.set('page', String(page));
    url.searchParams.set('per_page', '24');
    url.searchParams.set('content_filter', 'medium');
    url.searchParams.set('format_filter', 'gif');
    url.searchParams.set('q', query.trim());

    try {
        const response = await fetch(url, {
            signal: controller.signal,
            cache: 'no-store',
            credentials: 'omit'
        });

        if (response.status === 401 || response.status === 403) {
            throw new Error('KLIPY rejected the API key. Check your key and app access in the KLIPY Partner Panel.');
        }
        if (response.status === 429) {
            throw new Error('KLIPY’s request limit has been reached. Please try again later.');
        }
        if (!response.ok) throw new Error('KLIPY is unavailable right now. Please try again.');

        const payload = await response.json();
        if (!payload.result || !Array.isArray(payload.data?.data)) {
            throw new Error('KLIPY returned an unexpected response. Please try again later.');
        }

        // Keep every result in the provider's original order. Do not cache the response.
        return {
            items: payload.data.data.map(mapGif),
            hasNextPage: payload.data.has_next === true
        };
    } catch (error) {
        if (error.name === 'AbortError') throw new Error('GIF search was interrupted or timed out. Please try again.');
        if (error instanceof TypeError) throw new Error('Could not reach KLIPY. Check your connection and API access.');
        // Do not expose request URLs in errors: the API key is part of the path.
        if (error instanceof SyntaxError) throw new Error('KLIPY returned an unreadable response. Please try again.');
        throw error;
    } finally {
        finishRequest(); clearTimeout(timeout);
        if (activeSearch === controller) activeSearch = null;
    }
}

export async function copyLink(url) {
    try {
        await navigator.clipboard.writeText(url);
        return true;
    } catch {
        // The page also shows a selectable link when clipboard permission is unavailable.
        return false;
    }
}

export async function registerShare(slug, query) {
    const apiKey = readApiKey();
    if (!apiKey || !slug) return;

    // Sharing should still succeed when optional provider reporting is unavailable.
    try {
        const controller = new AbortController(), finish = trackRequest(controller);
        const timer = setTimeout(() => controller.abort(),10000);
        try { await fetch(`${apiBaseUrl}${encodeURIComponent(apiKey)}/gifs/share/${encodeURIComponent(slug)}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ q: query }),
            credentials: 'omit',
            signal: controller.signal
        }); } finally { clearTimeout(timer); finish(); }
    } catch {
        // No retry queue: do not retain a history of the user's GIF selections.
    }
}

// Each visible search owns its import, so closing one search cannot cancel
// another window's download. Files travel directly from KLIPY to the device.
export function createImportSession() {
    let active = null, disposed = false;
    return {
        async download(url, maxBytes = 20 * 1024 * 1024) {
            if (disposed) throw new Error('This GIF search has closed.');
            if (active) throw new Error('Wait for the current GIF import to finish.');
            if (!mediaUrl(url)) throw new Error('This GIF does not have a supported KLIPY download URL.');
            maxBytes = Number.isFinite(maxBytes) ? Math.max(5 * 1024 * 1024, Math.min(100 * 1024 * 1024, maxBytes)) : 20 * 1024 * 1024;
            const controller = new AbortController();
            const finishRequest = trackRequest(controller, 'Downloading GIF…'); active = controller;
            const timeout = setTimeout(() => controller.abort(), 45000);
            let reader;
            try {
                const response = await fetch(url, { signal: controller.signal, credentials: 'omit', cache: 'no-store', redirect: 'error' });
                if (!response.ok) throw new Error('KLIPY could not deliver this GIF. Try another result.');
                const length = Number(response.headers.get('content-length'));
                if (length > maxBytes) throw new Error(`This GIF exceeds the ${Math.floor(maxBytes / (1024 * 1024))} MiB import limit. Change GIF import limit in Settings → Editing or choose a smaller GIF.`);
                if (!response.body) throw new Error('The GIF download was empty. Try another result.');
                reader = response.body.getReader();
                let bytes = 0; const chunks = [];
                for (;;) {
                    const { done, value } = await reader.read();
                    if (done) break;
                    bytes += value.byteLength;
                    if (bytes > maxBytes) throw new Error(`This GIF exceeds the ${Math.floor(maxBytes / (1024 * 1024))} MiB import limit. Change GIF import limit in Settings → Editing or choose a smaller GIF.`);
                    chunks.push(value);
                }
                if (controller.signal.aborted || disposed) throw new DOMException('Cancelled', 'AbortError');
                const blob = new Blob(chunks, { type: 'image/gif' });
                const header = new Uint8Array(await blob.slice(0, 6).arrayBuffer());
                const signature = String.fromCharCode(...header);
                if (signature !== 'GIF87a' && signature !== 'GIF89a') throw new Error('KLIPY returned an invalid GIF file. Try another result.');
                if (controller.signal.aborted || disposed) throw new DOMException('Cancelled', 'AbortError');
                return blob;
            } catch (error) {
                if (error.name === 'AbortError') throw new Error('GIF import was cancelled or timed out.');
                if (error instanceof TypeError) throw new Error('Could not download this GIF directly. Check your connection; KLIPY may be blocking downloads from this browser.');
                throw error;
            } finally {
                finishRequest(); clearTimeout(timeout);
                if (reader) { try { await reader.cancel(); } catch { } reader.releaseLock(); }
                controller.abort();
                if (active === controller) active = null;
            }
        },
        cancel() { active?.abort(); },
        dispose() { disposed = true; active?.abort(); }
    };
}
