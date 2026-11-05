export function watch(reference) {
    let count = 0, since = Date.now();
    const report = reason => {
        if (Date.now() - since > 60000) { count = 0; since = Date.now(); }
        if (++count > 20) return;
        const message = String(reason?.message ?? reason ?? 'Unknown JavaScript error').slice(0, 24000);
        const stack = typeof reason?.stack === 'string' ? reason.stack.slice(0, 24000) : '';
        reference.invokeMethodAsync('ReportError', message, stack).catch(() => {});
    };
    const error = event => report(event.error ?? event.message);
    const rejection = event => report(event.reason);
    window.addEventListener('error', error);
    window.addEventListener('unhandledrejection', rejection);
    return { dispose() { window.removeEventListener('error', error); window.removeEventListener('unhandledrejection', rejection); } };
}
