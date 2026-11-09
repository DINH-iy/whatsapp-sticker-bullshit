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

// Native <dialog> makes the background inert but does not stop scroll chaining
// in every WebView. Lock the document until the last nested dialog is gone.
let scrollLock = null;
function syncScrollLock() {
    const hasDialog = document.querySelector('dialog[open]');
    const body = document.body, html = document.documentElement;
    if (hasDialog && !scrollLock) {
        scrollLock = { x: window.scrollX, y: window.scrollY, position: body.style.position, top: body.style.top,
            left: body.style.left, width: body.style.width, overflow: body.style.overflow, htmlOverflow: html.style.overflow };
        Object.assign(body.style, { position: 'fixed', top: `${-scrollLock.y}px`, left: `${-scrollLock.x}px`, width: '100%', overflow: 'hidden' });
        html.style.overflow = 'hidden';
    } else if (!hasDialog && scrollLock) {
        const saved = scrollLock; scrollLock = null;
        Object.assign(body.style, { position: saved.position, top: saved.top, left: saved.left, width: saved.width, overflow: saved.overflow });
        html.style.overflow = saved.htmlOverflow; window.scrollTo(saved.x, saved.y);
    }
}
const scrollLockObserver = new MutationObserver(syncScrollLock);
scrollLockObserver.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['open'] });
syncScrollLock();

// Keep elapsed feedback ticking independently of rendering and stop on removal.
export function watchElapsed(element, label) {
    const output = element.querySelector('[data-operation-elapsed]');
    if (!output) return;
    const started = performance.now();
    const update = () => { output.textContent = label.replace('{0}', String(Math.floor((performance.now() - started) / 1000))); };
    const timer = setInterval(update, 1000);
    const observer = new MutationObserver(() => {
        if (element.isConnected && (element.tagName !== 'DIALOG' || element.open)) return;
        clearInterval(timer); observer.disconnect();
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['open'] });
    update();
}
