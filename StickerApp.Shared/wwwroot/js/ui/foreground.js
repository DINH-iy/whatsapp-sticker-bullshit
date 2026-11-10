// Native hosts explicitly signal backgrounding; browsers supply visibilitychange.
let nativeActive = true;
const listeners = new Set();
export const isForeground = () => nativeActive && !document.hidden;
const publish = () => { for (const listener of listeners) listener(isForeground()); };
window.stickerSetForeground = value => { nativeActive = value; window.stickerForeground = value; publish(); };
document.addEventListener('visibilitychange', publish);
window.addEventListener('pagehide', () => { nativeActive = false; publish(); });
window.addEventListener('pageshow', () => { nativeActive = true; publish(); });
export function onForegroundChanged(listener) { listeners.add(listener); return () => listeners.delete(listener); }
export function watch(reference) {
    const update = active => reference.invokeMethodAsync('SetForeground', active).catch(() => {});
    const dispose = onForegroundChanged(update); update(isForeground());
    return { dispose };
}
export function requireForeground() { if (!isForeground()) throw new Error('Processing paused. Return to the app and try again.'); }
const jobs = new Map(), observers = new Set();
let sequence = 0;
const announce = () => { const snapshot = [...jobs.values()]; for (const observer of observers) observer(snapshot); };
export function startJob(name) {
    requireForeground();
    const id = ++sequence; jobs.set(id, { id, name }); announce();
    const finish = () => { jobs.delete(id); announce(); };
    finish.progress = value => { if (jobs.has(id)) { jobs.set(id, { id, name, ...value }); announce(); } };
    return finish;
}
export function watchJobs(reference) {
    const update = jobs => reference.invokeMethodAsync('JobsChanged', jobs).catch(() => {});
    observers.add(update); update([...jobs.values()]);
    return { dispose() { observers.delete(update); } };
}
export function trackRequest(controller, name = 'Network request…') {
    requireForeground();
    const finish = startJob(name);
    const unwatch = onForegroundChanged(active => { if (!active) controller.abort(); });
    return () => { unwatch(); finish(); };
}
