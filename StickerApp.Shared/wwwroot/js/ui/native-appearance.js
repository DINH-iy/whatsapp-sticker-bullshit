export function watch(reference) {
    let previous;
    const apply = () => {
        const root = document.documentElement;
        const background = getComputedStyle(root).getPropertyValue('--background').trim();
        const dark = root.dataset.theme === 'dark';
        const signature = `${background}:${dark}`;
        if (signature === previous) return;
        previous = signature;
        reference.invokeMethodAsync('ApplyAppearance', background, dark).catch(() => {});
    };
    const observer = new MutationObserver(apply);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-color'] });
    apply();
    return { dispose() { observer.disconnect(); } };
}
