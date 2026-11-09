// Opt-in duration measurements only: no polling, persistent observer, or network traffic.
let enabled = false;
export function setEnabled(value) { enabled = !!value; }
export function beginTiming(operation, details = '') {
    if (!enabled || typeof window === 'undefined') return () => {};
    const started = performance.now(); let finished = false;
    return () => {
        if (finished || !enabled) return; finished = true;
        window.dispatchEvent(new CustomEvent('stickers:performance', { detail: { operation, milliseconds: performance.now() - started, details } }));
    };
}
export async function timed(operation, work, details = '') {
    const finish = beginTiming(operation, details);
    try { return await work(); } finally { finish(); }
}
export function watch(reference, active) {
    setEnabled(active);
    const report = event => {
        const { operation, milliseconds, details } = event.detail;
        reference.invokeMethodAsync('ReportTiming', operation, milliseconds, details).catch(() => {});
    };
    window.addEventListener('stickers:performance', report);
    return { dispose() { window.removeEventListener('stickers:performance', report); setEnabled(false); } };
}
