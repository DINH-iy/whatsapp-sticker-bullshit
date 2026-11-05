export function watchLayerOrder(root, reorder, step) {
    let drag;
    const rows = () => [...root.querySelectorAll('[data-layer-row]')];
    const clearMarks = () => rows().forEach(row => row.classList.remove('layer-drop-before', 'layer-drop-after', 'layer-row-dragging'));
    function down(event) {
        const handle = event.target.closest?.('[data-layer-grip]');
        if (!handle || drag || event.button !== 0 || root.getAttribute('aria-busy') === 'true') return;
        event.preventDefault(); event.stopPropagation();
        const row = handle.closest('[data-layer-row]');
        drag = { id: row.dataset.layerRow, pointer: event.pointerId, handle, order: null };
        row.classList.add('layer-row-dragging'); handle.setPointerCapture(event.pointerId);
    }
    function move(event) {
        if (!drag || event.pointerId !== drag.pointer) return;
        const available = rows().filter(row => row.dataset.layerRow !== drag.id);
        let position = available.findIndex(row => event.clientY < row.getBoundingClientRect().top + row.getBoundingClientRect().height / 2);
        if (position < 0) position = available.length;
        clearMarks(); rows().find(row => row.dataset.layerRow === drag.id)?.classList.add('layer-row-dragging');
        const target = available[position] ?? available[available.length - 1];
        target?.classList.add(position < available.length ? 'layer-drop-before' : 'layer-drop-after');
        const order = available.map(row => row.dataset.layerRow); order.splice(position, 0, drag.id);
        drag.order = order.reverse();
        const bounds = root.getBoundingClientRect();
        if (event.clientY < bounds.top + 50) root.scrollTop -= 12;
        else if (event.clientY > bounds.bottom - 170) root.scrollTop += 12;
    }
    function up(event) {
        if (!drag || event.pointerId !== drag.pointer) return;
        const finished = drag; drag = null; clearMarks();
        if (finished.handle.hasPointerCapture(event.pointerId)) finished.handle.releasePointerCapture(event.pointerId);
        if (event.type !== 'pointercancel' && finished.order) reorder(finished.order);
    }
    function key(event) {
        const handle = event.target.closest?.('[data-layer-grip]');
        if (!handle || !['ArrowUp', 'ArrowDown'].includes(event.key) || root.getAttribute('aria-busy') === 'true') return;
        event.preventDefault(); event.stopPropagation();
        step(handle.closest('[data-layer-row]').dataset.layerRow, event.key === 'ArrowUp' ? 1 : -1);
    }
    root.addEventListener('pointerdown', down); root.addEventListener('pointermove', move);
    root.addEventListener('pointerup', up); root.addEventListener('pointercancel', up); root.addEventListener('keydown', key);
    return { dispose() {
        clearMarks(); drag = null;
        root.removeEventListener('pointerdown', down); root.removeEventListener('pointermove', move);
        root.removeEventListener('pointerup', up); root.removeEventListener('pointercancel', up); root.removeEventListener('keydown', key);
    } };
}
