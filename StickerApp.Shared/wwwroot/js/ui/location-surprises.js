import { trackRequest } from './foreground.js';
// Country lookup is optional. Only a country code is kept for the current session;
// the app never stores an IP address or asks for GPS access.
const preferenceKey = 'stickers.locationSurprises';
const sessionCountryKey = 'stickers.sessionCountry';
const shownKey = 'stickers.romaniaSurpriseShown';
const settingsEvent = 'stickers:location-settings';
const previewEvent = 'stickers:preview-romania';
let subscriber = null;
let pendingLookup = null;

export function isEnabled() {
    try {
        return localStorage.getItem(preferenceKey) === 'true';
    } catch {
        return false;
    }
}

export function setEnabled(enabled) {
    try {
        localStorage.setItem(preferenceKey, String(enabled));
    } catch {
        throw new Error('Could not save this setting on your device.');
    }
    window.dispatchEvent(new Event(settingsEvent));
}

export function previewRomania() {
    // A preview does not make a location request or mark the real surprise as seen.
    window.dispatchEvent(new Event(previewEvent));
}

async function getCountry() {
    try {
        const saved = JSON.parse(sessionStorage.getItem(sessionCountryKey) || 'null');
        if (saved && Date.now() - saved.checkedAt < 30 * 60 * 1000) return saved.country;
    } catch {
        // A lookup still works when session storage is unavailable.
    }

    pendingLookup?.abort();
    const controller = new AbortController();
    const finishRequest = trackRequest(controller);
    pendingLookup = controller;
    const timeout = setTimeout(() => controller.abort(), 5000);

    try {
        // Direct device request: the browser host's location is irrelevant.
        const response = await fetch('https://api.country.is/', {
            signal: controller.signal,
            credentials: 'omit',
            referrerPolicy: 'no-referrer'
        });
        if (!response.ok) return null;
        const result = await response.json();
        const country = typeof result.country === 'string' ? result.country.toUpperCase() : '';
        if (!/^[A-Z]{2}$/.test(country)) return null;

        try {
            sessionStorage.setItem(sessionCountryKey, JSON.stringify({ country, checkedAt: Date.now() }));
        } catch {
            // Session caching is helpful, but optional.
        }
        return country;
    } catch {
        // An unavailable lookup must never stop someone using their stickers.
        return null;
    } finally {
        finishRequest(); clearTimeout(timeout);
        if (pendingLookup === controller) pendingLookup = null;
    }
}

async function checkCountry() {
    if (!subscriber || !isEnabled()) return;
    try {
        if (sessionStorage.getItem(shownKey) === 'true') return;
    } catch { }

    const country = await getCountry();
    if (country !== 'RO' || !subscriber || !isEnabled()) return;
    try { await subscriber.invokeMethodAsync('ShowRomaniaSurprise'); } catch { return; }
    try { sessionStorage.setItem(shownKey, 'true'); } catch { }
}

async function handleSettingsChange() {
    if (isEnabled()) {
        await checkCountry();
    } else {
        pendingLookup?.abort();
        try { await subscriber?.invokeMethodAsync('HideSurprise'); } catch { }
    }
}

function handleStorage(event) {
    if (event.key === preferenceKey || event.key === null) void handleSettingsChange();
}

function handlePreview() {
    void subscriber?.invokeMethodAsync('ShowRomaniaSurprise').catch(() => {});
}

export function start(dotNetReference) {
    stop();
    subscriber = dotNetReference;
    window.addEventListener(settingsEvent, handleSettingsChange);
    window.addEventListener(previewEvent, handlePreview);
    window.addEventListener('storage', handleStorage);
    void checkCountry();
}

export function stop() {
    pendingLookup?.abort();
    subscriber = null;
    window.removeEventListener(settingsEvent, handleSettingsChange);
    window.removeEventListener(previewEvent, handlePreview);
    window.removeEventListener('storage', handleStorage);
}
