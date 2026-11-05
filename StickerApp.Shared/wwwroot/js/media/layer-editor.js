const size = 512, maxLayers = 10, maxSources = 8 * 1024 * 1024;
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));

export function create(canvas, reference) {
    const context = canvas.getContext('2d');
    const overlay = document.createElement('div');
    overlay.className = 'layer-handles';
    canvas.parentElement.append(overlay);
    const handleDefinitions = [
        ['top left', -1, -1], ['top', 0, -1], ['top right', 1, -1],
        ['left', -1, 0], ['right', 1, 0],
        ['bottom left', -1, 1], ['bottom', 0, 1], ['bottom right', 1, 1],
        ['rotate', 0, -1]
    ];
    const handles = handleDefinitions.map(([name, x, y]) => {
        const button = document.createElement('button');
        const rotating = name === 'rotate';
        button.type = 'button';
        button.className = `layer-handle${rotating ? ' layer-rotate-handle' : ''}`;
        button.setAttribute('aria-label', rotating ? 'Rotate selected layer' : `Resize layer ${name}`);
        button.dataset.tapFeedback = 'off';
        const mark = document.createElement('span');
        mark.textContent = rotating ? '↻' : '';
        mark.setAttribute('aria-hidden', 'true');
        button.append(mark);
        const handle = { button, name, x, y, rotating };
        button.addEventListener('pointerdown', event => startHandle(event, handle));
        button.addEventListener('keydown', event => handleKey(event, handle));
        overlay.append(button);
        return handle;
    });
    const root = canvas.closest('dialog');
    let editingControl = null, notifyTimer;
    const blockCancel = event => event.preventDefault();
    root.addEventListener('cancel', blockCancel);
    canvas.width = canvas.height = size;
    let layers = [], selected = -1, undo = [], redo = [], drag = null, disposed = false;
    const images = new Map(), tints = new Map(), thumbnails = new WeakMap();
    const snapshot = () => ({ layers: layers.map(layer => ({ ...layer })), selected });
    const remember = () => {
        undo.push(snapshot()); if (undo.length > 20) undo.shift(); redo = [];
        const retained = new Set([...layers, ...undo.flatMap(item => item.layers)].map(layer => layer.id));
        for (const id of images.keys()) if (!retained.has(id)) { images.delete(id); tints.delete(id); }
    };
    const active = () => layers[selected];
    function thumbnail(layer) {
        const image = images.get(layer.id);
        if (!image) return '';
        if (!thumbnails.has(image)) {
            const preview = document.createElement('canvas');
            preview.width = preview.height = 128;
            const scale = Math.min(128 / image.width, 128 / image.height);
            const width = image.width * scale, height = image.height * scale;
            preview.getContext('2d').drawImage(image, (128 - width) / 2, (128 - height) / 2, width, height);
            thumbnails.set(image, preview.toDataURL('image/png'));
        }
        return thumbnails.get(image);
    }
    const state = () => ({ layers: layers.map(({ source, ...layer }) => ({ ...layer, thumbnail: thumbnail(layer) })),
        selected, canUndo: undo.length > 0, canRedo: redo.length > 0 });
    const notify = () => { if (!disposed) return reference.invokeMethodAsync('LayersChanged', state()); };
    const decode = async source => { const image = new Image(); image.src = source; await image.decode(); return image; };
    const point = event => {
        const rect = canvas.getBoundingClientRect();
        return { x: (event.clientX - rect.left) * size / rect.width, y: (event.clientY - rect.top) * size / rect.height };
    };
    const pixelScale = () => size / (canvas.getBoundingClientRect().width || size);
    function world(layer, x, y) {
        const angle = layer.rotation * Math.PI / 180;
        return { x: layer.x + x * Math.cos(angle) - y * Math.sin(angle),
            y: layer.y + x * Math.sin(angle) + y * Math.cos(angle) };
    }
    function handlePoint(layer, handle) {
        return world(layer, handle.x * layer.width / 2,
            handle.y * layer.height / 2 - (handle.rotating ? 32 * pixelScale() : 0));
    }
    function placeHandles(layer, visible) {
        overlay.hidden = !visible || !layer;
        if (overlay.hidden) return;
        const inset = 10 * pixelScale();
        for (const handle of handles) {
            const p = handlePoint(layer, handle);
            handle.button.style.left = `${clamp(p.x, inset, size - inset) / size * 100}%`;
            handle.button.style.top = `${clamp(p.y, inset, size - inset) / size * 100}%`;
            handle.button.title = handle.rotating
                ? `Rotate: ${Math.round(layer.rotation)}°. Drag or use arrow keys.`
                : `Resize: ${Math.round(layer.width)} × ${Math.round(layer.height)}. Drag or use arrow keys.`;
            if (!handle.rotating) {
                const angle = Math.atan2(handle.y, handle.x) + layer.rotation * Math.PI / 180;
                const direction = ((Math.round(angle / (Math.PI / 4)) % 4) + 4) % 4;
                handle.button.style.cursor = ['ew-resize', 'nwse-resize', 'ns-resize', 'nesw-resize'][direction];
            }
        }
    }
    function render(selection = true) {
        context.clearRect(0, 0, size, size);
        for (const layer of layers) {
            const image = images.get(layer.id);
            if (!image) continue;
            let source = image;
            if (layer.tint) {
                let cached = tints.get(layer.id);
                if (!cached || cached.color !== layer.tint) {
                    const tinted = document.createElement('canvas');
                    tinted.width = image.width; tinted.height = image.height;
                    const tintContext = tinted.getContext('2d');
                    tintContext.drawImage(image, 0, 0);
                    tintContext.globalCompositeOperation = 'source-in';
                    tintContext.fillStyle = layer.tint; tintContext.fillRect(0, 0, image.width, image.height);
                    cached = { color: layer.tint, image: tinted }; tints.set(layer.id, cached);
                }
                source = cached.image;
            }
            context.save();
            context.globalAlpha = layer.opacity;
            context.translate(layer.x, layer.y); context.rotate(layer.rotation * Math.PI / 180);
            context.scale(layer.flipX ? -1 : 1, 1);
            context.drawImage(source, -layer.width / 2, -layer.height / 2, layer.width, layer.height);
            context.restore();
        }
        const layer = active();
        placeHandles(layer, selection);
        if (selection && layer) {
            context.save(); context.translate(layer.x, layer.y); context.rotate(layer.rotation * Math.PI / 180);
            context.lineWidth = 2; context.strokeStyle = '#ffffff'; context.setLineDash([6, 4]);
            context.strokeRect(-layer.width / 2, -layer.height / 2, layer.width, layer.height);
            context.lineWidth = 1; context.strokeStyle = '#18191d'; context.lineDashOffset = 6;
            context.strokeRect(-layer.width / 2, -layer.height / 2, layer.width, layer.height); context.restore();
        }
    }
    function hit(layer, p) {
        const angle = -layer.rotation * Math.PI / 180;
        const dx = p.x - layer.x, dy = p.y - layer.y;
        return Math.abs(dx * Math.cos(angle) - dy * Math.sin(angle)) <= layer.width / 2 &&
            Math.abs(dx * Math.sin(angle) + dy * Math.cos(angle)) <= layer.height / 2;
    }
    function begin(event, mode, handle = null) {
        const layer = active(), p = point(event);
        editingControl = null;
        drag = { pointer: event.pointerId, mode, handle, start: { ...layer }, point: p,
            keepRatio: root.querySelector('[data-layer-ratio]').checked, changed: false };
        event.preventDefault();
        canvas.setPointerCapture(event.pointerId);
        canvas.style.cursor = mode === 'move' ? 'grabbing' : 'crosshair';
        render();
        notify()?.catch(() => {});
    }
    function startHandle(event, handle) {
        if (drag || disposed || event.button !== 0 || !active() || root.getAttribute('aria-busy') === 'true') return;
        event.stopPropagation();
        begin(event, handle.rotating ? 'rotate' : 'resize', handle);
    }
    function down(event) {
        if (drag || disposed || event.button !== 0 || root.getAttribute('aria-busy') === 'true') return;
        const p = point(event);
        let index = -1;
        for (let i = layers.length - 1; i >= 0; i--) if (hit(layers[i], p)) { index = i; break; }
        if (index < 0) return;
        selected = index;
        begin(event, 'move');
    }
    function transformDrag(gesture, p, snap = false) {
        const layer = active(), start = gesture.start;
        const dx = p.x - gesture.point.x, dy = p.y - gesture.point.y;
        if (gesture.mode === 'move') {
            layer.x = clamp(start.x + dx, -2048, 2560);
            layer.y = clamp(start.y + dy, -2048, 2560);
        } else if (gesture.mode === 'rotate') {
            const before = Math.atan2(gesture.point.y - start.y, gesture.point.x - start.x);
            const after = Math.atan2(p.y - start.y, p.x - start.x);
            let degrees = start.rotation + (after - before) * 180 / Math.PI;
            if (snap) degrees = Math.round(degrees / 15) * 15;
            layer.rotation = ((degrees + 180) % 360 + 360) % 360 - 180;
        } else {
            // Work in the original rotated coordinate system and keep the opposite
            // corner/edge fixed, rather than resizing around the image's center.
            const angle = start.rotation * Math.PI / 180;
            const localX = dx * Math.cos(angle) + dy * Math.sin(angle);
            const localY = -dx * Math.sin(angle) + dy * Math.cos(angle);
            const { x, y } = gesture.handle;
            let width = start.width + x * localX, height = start.height + y * localY;
            if (gesture.keepRatio) {
                let scale;
                if (x && y) scale = 1 + (x * localX * start.width + y * localY * start.height) / (start.width ** 2 + start.height ** 2);
                else scale = x ? width / start.width : height / start.height;
                scale = clamp(scale, Math.max(1 / start.width, 1 / start.height), Math.min(2048 / start.width, 2048 / start.height));
                width = start.width * scale; height = start.height * scale;
            } else {
                width = clamp(x ? width : start.width, 1, 2048);
                height = clamp(y ? height : start.height, 1, 2048);
            }
            const shiftX = x * (width - start.width) / 2;
            const shiftY = y * (height - start.height) / 2;
            layer.width = width; layer.height = height;
            layer.x = clamp(start.x + shiftX * Math.cos(angle) - shiftY * Math.sin(angle), -2048, 2560);
            layer.y = clamp(start.y + shiftX * Math.sin(angle) + shiftY * Math.cos(angle), -2048, 2560);
        }
    }
    function move(event) {
        if (!drag || event.pointerId !== drag.pointer) return;
        const p = point(event);
        if (!drag.changed) {
            if (Math.hypot(p.x - drag.point.x, p.y - drag.point.y) < 2 * pixelScale()) return;
            remember(); drag.changed = true;
        }
        transformDrag(drag, p, event.shiftKey);
        render();
    }
    function up(event) {
        if (!drag || event.pointerId !== drag.pointer) return;
        if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
        drag = null; canvas.style.cursor = '';
        notify()?.catch(() => {});
    }
    function handleKey(event, handle) {
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || !active() || root.getAttribute('aria-busy') === 'true') return;
        event.preventDefault(); event.stopPropagation();
        const layer = active();
        remember();
        if (handle.rotating) {
            const direction = event.key === 'ArrowRight' || event.key === 'ArrowUp' ? 1 : -1;
            const degrees = layer.rotation + direction * (event.shiftKey ? 15 : 1);
            layer.rotation = ((degrees + 180) % 360 + 360) % 360 - 180;
        } else {
            const p = handlePoint(layer, handle), step = event.shiftKey ? 10 : 2;
            const dx = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
            const dy = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
            transformDrag({ start: { ...layer }, point: p, mode: 'resize', handle,
                keepRatio: root.querySelector('[data-layer-ratio]').checked }, { x: p.x + dx, y: p.y + dy });
        }
        render(); notify()?.catch(() => {});
    }
    function update(property, value, keepRatio = true, record = true) {
        const layer = active(); if (!layer) return state();
        const ranges = { x: [-2048, 2560], y: [-2048, 2560], width: [1, 2048], height: [1, 2048], rotation: [-180, 180], opacity: [0, 1] };
        if (property === 'tint') {
            if (value !== null && !/^#[0-9a-f]{6}$/i.test(value)) throw new Error('Choose a valid tint color.');
            if (record) remember(); layer.tint = value;
        } else {
            if (!ranges[property] || !Number.isFinite(value)) throw new Error('Enter a valid layer adjustment.');
            if (record) remember(); value = clamp(value, ...ranges[property]);
            if (keepRatio && (property === 'width' || property === 'height')) {
                const other = property === 'width' ? 'height' : 'width';
                const ratio = layer[other] / layer[property];
                value = Math.min(value, 2048 / ratio);
                layer[other] = Math.max(1, value * ratio);
            }
            layer[property] = value;
        }
        render(); return state();
    }
    function input(event) {
        const control = event.target;
        const property = control.dataset?.layerProperty;
        if (!property || !active()) return;
        let value = property === 'tint' ? control.value : Number(control.value);
        if (control.value === '' || (property !== 'tint' && !Number.isFinite(value))) return;
        if (property === 'opacity') value /= 100;
        if (editingControl !== control) { remember(); editingControl = control; }
        update(property, value, root.querySelector('[data-layer-ratio]').checked, false);
        // Update linked fields locally; publish metadata once the adjustment ends.
        for (const field of root.querySelectorAll('[data-layer-property]')) {
            if (field === control || field.dataset.layerProperty === 'tint') continue;
            const prop = field.dataset.layerProperty;
            field.value = String(Math.round(active()[prop] * (prop === 'opacity' ? 100 : 1) * 100) / 100);
        }
        clearTimeout(notifyTimer);
        notifyTimer = setTimeout(() => notify()?.catch(() => {}), 60);
        for (const readout of root.querySelectorAll('[data-layer-readout]')) {
            const prop = readout.dataset.layerReadout;
            readout.textContent = String(Math.round(active()[prop] * (prop === 'opacity' ? 100 : 1)));
        }
    }
    function change(event) {
        if (!event.target.dataset?.layerProperty) return;
        editingControl = null;
        clearTimeout(notifyTimer);
        notify()?.catch(() => {});
    }
    const resizeObserver = new ResizeObserver(() => { if (!disposed) render(); });
    resizeObserver.observe(canvas);
    root.addEventListener('input', input);
    root.addEventListener('change', change);
    canvas.addEventListener('pointerdown', down); canvas.addEventListener('pointermove', move);
    canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', up);
    return {
        async open(composition) {
            layers = []; selected = -1; undo = []; redo = []; images.clear(); tints.clear();
            if (composition) {
                for (const layer of composition.layers) {
                    const image = await decode(layer.source);
                    if (disposed) return;
                    layers.push({ ...layer }); images.set(layer.id, image);
                }
                selected = layers.length - 1;
            }
            render(); return state();
        },
        async add(name, source) {
            if (layers.length >= maxLayers) throw new Error('A composition supports up to 10 layers.');
            const image = await decode(source);
            if (disposed) return state();
            if (image.naturalWidth * image.naturalHeight > 16_000_000) throw new Error('Choose an image smaller than 16 megapixels.');
            const normalized = document.createElement('canvas');
            const scale = Math.min(1, 1024 / Math.max(image.naturalWidth, image.naturalHeight));
            normalized.width = Math.max(1, Math.round(image.naturalWidth * scale));
            normalized.height = Math.max(1, Math.round(image.naturalHeight * scale));
            normalized.getContext('2d').drawImage(image, 0, 0, normalized.width, normalized.height);
            const png = normalized.toDataURL('image/png');
            if (layers.reduce((sum, layer) => sum + layer.source.length, png.length) > maxSources)
                throw new Error('Layer images exceed 8 MB. Use smaller or fewer images.');
            const fit = Math.min(1, 400 / Math.max(normalized.width, normalized.height));
            const id = `layer-${Date.now()}-${Math.random().toString(36).slice(2)}`;
            const layer = { id, name: name.slice(0, 128) || 'Image', source: png, x: 256, y: 256,
                width: Math.max(1, Math.round(normalized.width * fit)), height: Math.max(1, Math.round(normalized.height * fit)),
                rotation: 0, opacity: 1, flipX: false, tint: null };
            const still = await decode(png);
            remember(); layers.push(layer); images.set(id, still); selected = layers.length - 1;
            render(); return state();
        },
        select(index) { editingControl = null; if (layers[index]) selected = index; render(); return state(); },
        update,
        action(action) {
            editingControl = null;
            if (action === 'undo' && undo.length) { redo.push(snapshot()); const saved = undo.pop(); layers = saved.layers; selected = saved.selected; }
            else if (action === 'redo' && redo.length) { undo.push(snapshot()); const saved = redo.pop(); layers = saved.layers; selected = saved.selected; }
            else if (active()) {
                const layer = active();
                if (action === 'duplicate') {
                    if (layers.length >= maxLayers || layers.reduce((sum, item) => sum + item.source.length, layer.source.length) > maxSources)
                        throw new Error('This composition has reached its layer or image-size limit.');
                    remember(); const id = `layer-${Date.now()}-${Math.random().toString(36).slice(2)}`;
                    layers.splice(selected + 1, 0, { ...layer, id, x: clamp(layer.x + 12, -2048, 2560), y: clamp(layer.y + 12, -2048, 2560) });
                    images.set(id, images.get(layer.id)); selected++;
                } else if (action === 'front' && selected < layers.length - 1) {
                    remember(); layers.splice(selected, 1); layers.splice(++selected, 0, layer);
                } else if (action === 'back' && selected > 0) {
                    remember(); layers.splice(selected, 1); layers.splice(--selected, 0, layer);
                } else if (action === 'remove') { remember(); layers.splice(selected, 1); selected = Math.min(selected, layers.length - 1); }
                else if (action === 'rotate') { remember(); layer.rotation = ((layer.rotation + 270) % 360 + 360) % 360 - 180; }
                else if (action === 'flip') { remember(); layer.flipX = !layer.flipX; }
                else if (action === 'center') { remember(); layer.x = layer.y = 256; }
            }
            render(); return state();
        },
        export() {
            if (!layers.length) throw new Error('Add at least one layer before saving.');
            render(false);
            try {
                if (!context.getImageData(0, 0, size, size).data.some((value, index) => index % 4 === 3 && value > 0))
                    throw new Error('The sticker is completely transparent. Move a visible layer onto the canvas.');
                return { png: canvas.toDataURL('image/png'), document: { version: 1, layers: layers.map(layer => ({ ...layer })) } };
            } finally { render(); }
        },
        dispose() {
            disposed = true;
            clearTimeout(notifyTimer);
            resizeObserver.disconnect(); overlay.remove();
            root.removeEventListener('input', input); root.removeEventListener('change', change);
            root.removeEventListener('cancel', blockCancel);
            canvas.removeEventListener('pointerdown', down); canvas.removeEventListener('pointermove', move);
            canvas.removeEventListener('pointerup', up); canvas.removeEventListener('pointercancel', up);
            layers = []; undo = []; redo = []; images.clear(); tints.clear();
        }
    };
}
