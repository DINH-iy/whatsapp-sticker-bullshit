export function watch(element, reference) {
    let previous, disposed = false;
    const measure = () => {
        if (disposed) return;
        const probe = element.querySelector('.sticker-grid-probe');
        const count = element.classList.contains('library-list-view') ? 1 : getComputedStyle(probe).gridTemplateColumns.split(' ').filter(Boolean).length;
        if (count > 0 && count !== previous) { previous = count; reference.invokeMethodAsync('ColumnsChanged', count).catch(() => {}); }
    };
    const observer = new ResizeObserver(measure); observer.observe(element); measure();
    return { measure, dispose() { disposed = true; observer.disconnect(); } };
}
