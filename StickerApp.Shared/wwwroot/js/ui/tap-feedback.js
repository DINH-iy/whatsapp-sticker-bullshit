export function watch(reference) {
    let lastTap = -Infinity;
    const onClick = event => {
        if (!event.isTrusted || !(event.target instanceof Element)) return;
        const control = event.target.closest('button, a[href], input[type="checkbox"], input[type="radio"], summary, label.file-button');
        if (!control || control.matches(':disabled, [aria-disabled="true"]') ||
            control.closest('[data-tap-feedback="off"]') ||
            control.matches('.toolbar-menu-dismiss, .navigation-dismiss')) return;
        const now = performance.now();
        if (now - lastTap < 150) return;
        lastTap = now;
        reference.invokeMethodAsync('Tap').catch(() => {});
    };
    document.addEventListener('click', onClick, true);
    return { dispose() { document.removeEventListener('click', onClick, true); } };
}
