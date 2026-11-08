// Reuse the layer editor's touch targets for raster artwork. The original
// pixels are retained during dragging so repeated moves never compound blur.
export function createResizeHandles(canvas, root, changed, syncPreview = () => {}) {
    const wrap = canvas.parentElement, overlay = document.createElement('div');
    overlay.className = 'image-resize-handles'; overlay.hidden = true; wrap.append(overlay);
    let base = null, drag = null, maxSize = 4096, width = 0, height = 0, displayScale = 1;
    const ratioLocked = () => root.querySelector('[data-image-resize-ratio]')?.checked !== false;
    const handles = [];
    const copy = () => { const result = document.createElement('canvas'); result.width = canvas.width; result.height = canvas.height; result.getContext('2d').drawImage(canvas, 0, 0); return result; };
    function place() {
        if (!base) return;
        const rect = canvas.getBoundingClientRect(), bounds = wrap.getBoundingClientRect();
        const inset = 18;
        const left = Math.max(inset, rect.left - bounds.left), top = Math.max(inset, rect.top - bounds.top);
        const right = Math.min(bounds.width - inset, rect.right - bounds.left), bottom = Math.min(bounds.height - inset, rect.bottom - bounds.top);
        Object.assign(overlay.style, { left: `${left}px`, top: `${top}px`, width: `${Math.max(1, right - left)}px`, height: `${Math.max(1, bottom - top)}px` });
    }
    function resize(w, h, lock, preferWidth = true) {
        if (!base) return;
        if (lock) {
            const ratio = base.width / base.height;
            if (preferWidth) h = w / ratio; else w = h * ratio;
            const fit = Math.min(1, maxSize / Math.max(w, h)); w *= fit; h *= fit;
        }
        width = Math.max(1, Math.min(maxSize, Math.round(w))); height = Math.max(1, Math.min(maxSize, Math.round(h)));
        canvas.width = width; canvas.height = height;
        canvas.getContext('2d').drawImage(base, 0, 0, width, height);
        // Uniform resizing changes the displayed handle rectangle too.
        syncPreview();
        const readout = root.querySelector('[data-image-resize-size]'); if (readout) readout.textContent = `${width} × ${height}`;
        place();
    }
    const directions = [['top left', -1, -1], ['top', 0, -1], ['top right', 1, -1], ['left', -1, 0], ['right', 1, 0], ['bottom left', -1, 1], ['bottom', 0, 1], ['bottom right', 1, 1]];
    for (const [name, x, y] of directions) {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'layer-handle image-resize-handle';
        button.setAttribute('aria-label', `Resize image ${name}`); button.dataset.tapFeedback = 'off';
        button.style.left = `${(x + 1) * 50}%`; button.style.top = `${(y + 1) * 50}%`;
        button.style.cursor = x && y ? x === y ? 'nwse-resize' : 'nesw-resize' : x ? 'ew-resize' : 'ns-resize';
        button.append(document.createElement('span')); overlay.append(button); handles.push(button);
        button.addEventListener('pointerdown', event => {
            if (!base || drag || event.button !== 0 || root.getAttribute('aria-busy') === 'true') return;
            event.preventDefault(); event.stopPropagation();
            displayScale = canvas.getBoundingClientRect().width / width || 1;
            drag = { pointer: event.pointerId, x: event.clientX, y: event.clientY, width, height, handleX: x, handleY: y };
            button.setPointerCapture(event.pointerId);
        });
        button.addEventListener('pointermove', event => {
            if (!drag || drag.pointer !== event.pointerId) return;
            const w = drag.width + (event.clientX - drag.x) * drag.handleX / displayScale;
            const h = drag.height + (event.clientY - drag.y) * drag.handleY / displayScale;
            resize(w, h, ratioLocked(), !!drag.handleX && (!drag.handleY || Math.abs(event.clientX - drag.x) >= Math.abs(event.clientY - drag.y)));
        });
        const end = event => {
            if (!drag || drag.pointer !== event.pointerId) return;
            if (button.hasPointerCapture(event.pointerId)) button.releasePointerCapture(event.pointerId);
            drag = null; changed(width, height);
        };
        button.addEventListener('pointerup', end); button.addEventListener('pointercancel', end);
        button.addEventListener('keydown', event => {
            if (!base || !['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)) return;
            event.preventDefault(); event.stopPropagation();
            const step = event.shiftKey ? 10 : 1;
            const dx = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
            const dy = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
            resize(width + dx * x, height + dy * y, ratioLocked(), !!(dx && x)); changed(width, height);
        });
    }
    const observer = new ResizeObserver(place); observer.observe(canvas);
    function end() {
        if (!base) return null;
        const result = { width, height, source: base };
        base = null; drag = null; overlay.hidden = true; syncPreview();
        for (const field of root.querySelectorAll('[data-animation-seek]')) field.disabled = false;
        canvas.width = result.source.width; canvas.height = result.source.height;
        canvas.getContext('2d').drawImage(result.source, 0, 0); syncPreview();
        return result;
    }
    return {
        get active() { return !!base; },
        begin(maximum) {
            end(); maxSize = maximum; base = copy(); width = base.width; height = base.height;
            displayScale = canvas.getBoundingClientRect().width / width || 1;
            overlay.hidden = false;
            for (const field of root.querySelectorAll('[data-animation-seek]')) field.disabled = true;
            place();
        },
        end,
        dispose() { end(); observer.disconnect(); overlay.remove(); }
    };
}
