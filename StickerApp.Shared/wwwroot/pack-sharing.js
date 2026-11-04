const settingsKey = 'stickers.packSharing';

function readSettings() {
    try {
        const settings = JSON.parse(localStorage.getItem(settingsKey) || 'null');
        return settings || { serverUrl: '', token: '' };
    } catch {
        return { serverUrl: '', token: '' };
    }
}

export function getSettings() {
    const settings = readSettings();
    // The publishing token stays in the browser/device, not in the Blazor web host.
    return { serverUrl: settings.serverUrl, hasToken: Boolean(settings.token) };
}

export function saveSettings(serverInput, tokenInput) {
    let url;
    try { url = new URL(serverInput.value.trim()); }
    catch { throw new Error('Enter a full sharing server URL.'); }

    const isLocal = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
    if (url.protocol !== 'https:' && !(url.protocol === 'http:' && isLocal)) {
        throw new Error('Use HTTPS for your sharing server. HTTP is allowed only on localhost for development.');
    }
    if (url.username || url.password || url.search || url.hash || url.pathname !== '/') {
        throw new Error('Use the server root URL, without a path, query, or embedded credentials.');
    }

    const previous = readSettings();
    const serverUrl = url.origin;
    const token = tokenInput.value.trim() || (previous.serverUrl === serverUrl ? previous.token : '');
    if (!token || token.length < 32 || token.length > 512) throw new Error('Enter the publishing token configured on your server (at least 32 characters).');

    try { localStorage.setItem(settingsKey, JSON.stringify({ serverUrl, token })); }
    catch { throw new Error('Could not save the sharing settings on this device.'); }
    tokenInput.value = '';
    serverInput.value = serverUrl;
}

export function clearSettings() {
    localStorage.removeItem(settingsKey);
}

export async function download(fileName, streamReference) {
    const bytes = await streamReference.arrayBuffer();
    const blob = new Blob([bytes], { type: 'application/zip' });
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Leave time for the browser's download manager to take ownership of the Blob.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 60000);
}

export async function publish(streamReference) {
    const settings = readSettings();
    if (!settings.serverUrl || !settings.token) {
        throw new Error('Configure your pack sharing server in Settings first. File export works without a server.');
    }

    const bytes = await streamReference.arrayBuffer();
    let response;
    try {
        response = await fetch(`${settings.serverUrl}/api/packs`, {
            method: 'POST',
            headers: { Authorization: `Bearer ${settings.token}`, 'Content-Type': 'application/zip' },
            body: new Blob([bytes], { type: 'application/zip' }),
            credentials: 'omit',
            signal: AbortSignal.timeout(120000)
        });
    } catch {
        throw new Error('Could not reach the sharing server. Check its URL, HTTPS, and network connection.');
    }

    if (response.status === 401) throw new Error('The sharing server rejected the publishing token. Check Settings.');
    if (response.status === 413) throw new Error('The server rejected the pack size. Packs must be at most 100 MB.');
    if (response.status === 507) throw new Error('The sharing server is out of storage. Remove old shared packs or increase its storage limit.');
    if (response.status === 429) throw new Error('The sharing server is busy. Try again shortly.');
    if (!response.ok) throw new Error('The sharing server could not publish this pack. Please try again.');

    const result = await response.json();
    if (!/^[a-f0-9]{64}$/i.test(result.sha256) || result.path !== `/p/${result.sha256}`) {
        throw new Error('The sharing server returned an invalid link.');
    }
    return { url: `${settings.serverUrl}${result.path}`, sha256: result.sha256 };
}

export async function copyText(value) {
    try {
        await navigator.clipboard.writeText(value);
        return true;
    } catch {
        return false;
    }
}
