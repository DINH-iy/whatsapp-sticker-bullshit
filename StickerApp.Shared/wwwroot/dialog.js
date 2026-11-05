// WKWebView can keep its layout viewport tall while the keyboard covers it.
function updateViewport() {
    const viewport = window.visualViewport;
    document.documentElement.style.setProperty('--dialog-viewport-height', `${viewport?.height ?? window.innerHeight}px`);
    document.documentElement.style.setProperty('--dialog-viewport-top', `${viewport?.offsetTop ?? 0}px`);
}
updateViewport();
window.visualViewport?.addEventListener('resize', updateViewport);
window.visualViewport?.addEventListener('scroll', updateViewport);
window.addEventListener('resize', updateViewport);

export function open(dialog) {
    if (dialog.open) return;
    updateViewport();
    if (typeof dialog.showModal === 'function') { dialog.showModal(); return; }
    // iOS 15.0–15.3 predates native HTML dialog support.
    const previous = document.activeElement;
    const backdrop = document.createElement('div');
    backdrop.className = 'dialog-fallback-backdrop';
    document.body.append(backdrop);
    dialog.dataset.fallbackModal = 'true';
    dialog.setAttribute('role', 'dialog'); dialog.setAttribute('aria-modal', 'true'); dialog.setAttribute('open', '');
    const focusable = () => [...dialog.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), a[href], [tabindex="0"]')].filter(el => el.getClientRects().length);
    const trap = event => {
        if (event.key !== 'Tab') return;
        const items = focusable(), first = items[0], last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    dialog.addEventListener('keydown', trap);
    focusable()[0]?.focus();
    const observer = new MutationObserver(() => {
        if (dialog.isConnected && dialog.hasAttribute('open')) return;
        backdrop.remove(); dialog.removeEventListener('keydown', trap); observer.disconnect();
        if (previous?.isConnected) previous.focus();
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['open'] });
}
