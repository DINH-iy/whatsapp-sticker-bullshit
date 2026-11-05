const root = document.documentElement;
const color = document.querySelector('meta[name="theme-color"]');
function updateColor() {
    const background = getComputedStyle(root).getPropertyValue('--background').trim();
    if (background) color?.setAttribute('content', background);
}
new MutationObserver(updateColor).observe(root, { attributes: true, attributeFilter: ['data-theme', 'data-color'] });
updateColor();
if ('serviceWorker' in navigator && window.isSecureContext) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/service-worker.js', { scope: '/', updateViaCache: 'none' })
            .catch(error => window.dispatchEvent(new ErrorEvent('error', { message: 'Home Screen support could not start: ' + error.message, error })));
    });
}
export function status() {
    return { installed: matchMedia('(display-mode: standalone)').matches || navigator.standalone === true };
}
