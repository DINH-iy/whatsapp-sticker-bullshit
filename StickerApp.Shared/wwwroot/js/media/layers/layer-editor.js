import { cloneLayers, leafLayers, groupLayers, transformGroup } from './layer-groups.js';
import { editorLabel } from '../shared/editor-language.js';
import { drawLayerArtwork } from '../effects/object-outline.js';
import { editorView } from '../shared/editor-view.js';
import { renderText, defaultText, validateTextFont } from './text-layers.js';
import { rotationGrip } from '../shared/rotation-grip.js';
import { applyColorEffect } from '../effects/color-effect.js';
import { previewControls } from '../animation/animation-preview.js';
import { openGif } from './layer-gifs.js';
import { watchLayerOrder } from './layer-reorder.js';
// Still WebP stickers must not be opened by the animation decoder.
export function isAnimatedSource(source) {
    if(source.startsWith('data:image/gif;base64,'))return true;
    if(!source.startsWith('data:image/webp;base64,'))return false;
    const bytes=atob(source.slice(source.indexOf(',')+1));
    for(let offset=12;offset+8<=bytes.length;) {
        const kind=bytes.slice(offset,offset+4);
        const length=(bytes.charCodeAt(offset+4)|(bytes.charCodeAt(offset+5)<<8)|(bytes.charCodeAt(offset+6)<<16)|(bytes.charCodeAt(offset+7)<<24))>>>0;
        if(offset+8+length>bytes.length)return false;
        if(kind==='ANIM'||kind==='ANMF')return true;
        offset+=8+length+(length&1);
    }
    return false;
}
const size = 512, maxLayers = 10, maxSources = 24 * 1024 * 1024;
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));

export function create(canvas, reference) {
    const context = canvas.getContext('2d');
    const view=editorView(canvas,canvas.closest('.layer-editor-dialog'));
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
        button.setAttribute('aria-label', rotating ? editorLabel(canvas,'Rotate selected layer') : editorLabel(canvas,'Resize layer {0}',editorLabel(canvas,name)));
        button.dataset.tapFeedback = 'off';
        const mark = rotating ? rotationGrip() : document.createElement('span');
        mark.setAttribute('aria-hidden', 'true');
        button.append(mark);
        const handle = { button, name, x, y, rotating };
        button.addEventListener('pointerdown', event => startHandle(event, handle));
        button.addEventListener('keydown', event => handleKey(event, handle));
        overlay.append(button);
        return handle;
    });
    const root = canvas.closest('dialog');
    let editingControl = null, notifyTimer, dialDrag = null, keepProportions = true;
    const proportions = () => active()?.children?.length ? true : root.querySelector('[data-adjustment-scope="layer"] [data-layer-ratio]')?.checked ?? keepProportions;
    const blockCancel = event => event.preventDefault();
    root.addEventListener('cancel', blockCancel);
    canvas.width = canvas.height = size;
    let layers = [], selected = -1, undo = [], redo = [], drag = null, disposed = false;
    let toolCheckpoint = null;
    const groupSelection = new Set(), groupFrames = new Map();
    const syncGroups = () => { for (const layer of layers.filter(item => item.children?.length)) { const before = groupFrames.get(layer.id); if (before) transformGroup(layer, before); groupFrames.set(layer.id, { ...layer }); } };
    const images = new Map(), tints = new Map(), thumbnails = new WeakMap(), gifs = new Map();
    let animationFrame, animationTime = 0, playbackOrigin = performance.now(), exporting = false, paused = false, pausedTime = 0, playbackOffset = 0, playbackRate = 1;
    const elapsed = () => paused ? pausedTime : playbackOffset + (performance.now() - playbackOrigin) * playbackRate;
    const controls = previewControls(root, {
        seek(seconds) {
            const layer = active()?.gifSource ? active() : leafLayers(layers).find(layer => layer.gifSource); if (!layer) return;
            pausedTime = Math.max(0, (clamp(seconds, layer.trimStart, layer.trimEnd - 0.001) - layer.trimStart) * 1000);
            paused = true; cancelAnimationFrame(animationFrame); animationFrame = undefined;
            render(); reference.invokeMethodAsync('PreviewChanged', false).catch(() => {});
        },
        speed(rate) {
            if (![0.1, 0.25, 0.5, 1].includes(rate)) return;
            playbackOffset = elapsed(); playbackOrigin = performance.now(); playbackRate = rate;
        },
        error: () => {}
    });
    function play(now) {
        animationFrame = undefined;
        if (disposed || exporting || paused || !leafLayers(layers).some(layer => layer.gifSource)) return;
        if (now - animationTime >= 50) { animationTime = now; render(); }
        animationFrame = requestAnimationFrame(play);
    }
    function animate() { if (!animationFrame && !disposed && !exporting) animationFrame = requestAnimationFrame(play); }
    const fontRegistry = new Map();
    const sourceSize = layer => layer.children?.length ? layer.children.reduce((sum, child) => sum + sourceSize(child), 0) : layer.source.length + (layer.gifSource?.length ?? 0) + (layer.text?.fontData?.length ?? 0);
    const snapshot = () => ({ layers: cloneLayers(layers), selected });
    const remember = () => {
        undo.push(snapshot()); if (undo.length > 20) undo.shift(); redo = [];
        const retained = new Set(leafLayers([...layers, ...undo.flatMap(item => item.layers)]).map(layer => layer.source));
        for (const source of images.keys()) if (!retained.has(source)) images.delete(source);
        const retainedIds = new Set(leafLayers([...layers, ...undo.flatMap(item => item.layers)]).map(layer => layer.id));
        for (const id of tints.keys()) if (!retainedIds.has(id)) tints.delete(id);
        const retainedAnimations = new Set(leafLayers([...layers, ...undo.flatMap(item => item.layers)]).map(layer => layer.gifSource));
        for (const source of gifs.keys()) if (!retainedAnimations.has(source)) { gifs.get(source).reader.dispose?.(); gifs.delete(source); }
    };
    const active = () => layers[selected];
    function thumbnail(layer) {
        if (layer.children?.length) {
            const preview = document.createElement('canvas'); preview.width = preview.height = 128;
            const ctx = preview.getContext('2d'); ctx.scale(.25,.25);
            drawLayers(ctx, leafLayers([layer])); return preview.toDataURL('image/png');
        }
        const image = images.get(layer.source);
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
    const state = () => ({ layers: layers.map(layer => {
        const {source,gifSource,children,...metadata}=layer;
        return {...metadata, text:layer.text?{...layer.text,fontData:null}:null,
            isGroup:!!children?.length, groupAnimated:!!children?.some(child=>child.gifSource), memberCount:children?.length??0, marked:groupSelection.has(layer.id),
            isGif:!!gifSource, gifThumbnails:gifs.get(gifSource)?.thumbnails??[], thumbnail:thumbnail(layer)};
    }), selected, canUndo:undo.length>0, canRedo:redo.length>0, previewPlaying:!paused,
        markedCount:layers.filter(layer=>groupSelection.has(layer.id)).length });
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
                ? editorLabel(canvas,'Rotate: {0}°. Drag or use arrow keys.',Math.round(layer.rotation))
                : editorLabel(canvas,'Resize: {0} × {1}. Drag or use arrow keys.',Math.round(layer.width),Math.round(layer.height));
            if (!handle.rotating) {
                const angle = Math.atan2(handle.y, handle.x) + layer.rotation * Math.PI / 180;
                const direction = ((Math.round(angle / (Math.PI / 4)) % 4) + 4) % 4;
                handle.button.style.cursor = ['ew-resize', 'nwse-resize', 'ns-resize', 'nesw-resize'][direction];
            }
        }
    }
    function drawLayers(target, artwork) {
        for (const layer of artwork) {
            const image = images.get(layer.source);
            if (!image) continue;
            let source = image;
            if (layer.gifSource && gifs.has(layer.gifSource)) {
                const length = Math.max(20, (layer.trimEnd - layer.trimStart) * 1000);
                source = gifs.get(layer.gifSource).reader.at(layer.trimStart * 1000 + elapsed() % length);
            }
            if (layer.tint || layer.hue && layer.colorStrength !== 0) {
                let cached = tints.get(layer.id);
                if (!cached || cached.color !== layer.tint || cached.source !== layer.source || cached.hue !== layer.hue || cached.strength !== layer.colorStrength || layer.gifSource) {
                    const tinted = document.createElement('canvas');
                    tinted.width = source.width; tinted.height = source.height;
                    const tintContext = tinted.getContext('2d');
                    tintContext.drawImage(source, 0, 0);
                    applyColorEffect(tintContext, layer);
                    cached = { color: layer.tint, hue: layer.hue, strength: layer.colorStrength, source: layer.source, image: tinted }; tints.set(layer.id, cached);
                }
                source = cached.image;
            }
            target.save();
            target.globalAlpha = layer.opacity;
            target.translate(layer.x, layer.y); target.rotate(layer.rotation * Math.PI / 180);
            target.scale(layer.flipX ? -1 : 1, 1);
            drawLayerArtwork(target,source,layer);
            target.restore();
        }
    }
    function render(selection = true) {
        syncGroups();
        context.clearRect(0, 0, size, size);
        drawLayers(context, leafLayers(layers));
        const layer = active();
        const clip = layer?.gifSource ? layer : leafLayers(layers).find(item => item.gifSource);
        if (clip) controls.update(clip.trimStart + elapsed() % Math.max(20, (clip.trimEnd - clip.trimStart) * 1000) / 1000);
        placeHandles(layer, selection);
        if (layer) {
            const dial = root.querySelector('[data-adjustment-scope="layer"] [data-layer-dial]');
            if (dial) {
                dial.setAttribute('aria-valuenow', String(Math.round(layer.rotation)));
                dial.querySelector('[data-layer-dial-needle]').setAttribute('transform', `rotate(${layer.rotation} 100 100)`);
                for (const readout of dial.querySelectorAll('[data-layer-readout]')) readout.textContent = String(Math.round(layer.rotation));
            }
            const rotation = root.querySelector('[data-adjustment-scope="layer"] input[data-layer-property="rotation"]');
            if (rotation && rotation !== editingControl) rotation.value = String(layer.rotation);
        }
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
            keepRatio: proportions(), changed: false };
        event.preventDefault();
        canvas.setPointerCapture(event.pointerId);
        canvas.style.cursor = mode === 'move' ? 'grabbing' : 'crosshair';
        render();
        notify()?.catch(() => {});
    }
    function startHandle(event, handle) {
        if (drag || dialDrag || disposed || event.button !== 0 || !active() || root.getAttribute('aria-busy') === 'true') return;
        event.stopPropagation();
        begin(event, handle.rotating ? 'rotate' : 'resize', handle);
    }
    function down(event) {
        if (drag || dialDrag || disposed || event.button !== 0 || root.getAttribute('aria-busy') === 'true') return;
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
                keepRatio: proportions() }, { x: p.x + dx, y: p.y + dy });
        }
        render(); notify()?.catch(() => {});
    }
    function update(property, value, keepRatio = true, record = true) {
        const layer = active(); if (!layer) return state();
        const ranges = { x: [-2048, 2560], y: [-2048, 2560], width: [1, 2048], height: [1, 2048], rotation: [-180, 180], opacity: [0, 1], hue: [-180, 180], colorStrength: [0, 1], borderWidth: [0, 80] };
        if(layer.children?.length && ['tint','borderColor','borderWidth','hue','colorStrength'].includes(property))throw new Error('Ungroup to edit individual contents.');
        if (property === 'tint' || property === 'borderColor') {
            if ((property === 'borderColor' && value === null) || (value !== null && !/^#[0-9a-f]{6}$/i.test(value))) throw new Error('Choose a valid tint color.');
            if (record) remember(); layer[property] = value;
        } else {
            if (!ranges[property] || !Number.isFinite(value)) throw new Error('Enter a valid layer adjustment.');
            if (record) remember(); value = clamp(value, ...ranges[property]);
            if ((keepRatio || layer.children?.length) && (property === 'width' || property === 'height')) {
                const other = property === 'width' ? 'height' : 'width';
                const ratio = layer[other] / layer[property];
                value = Math.min(value, 2048 / ratio);
                layer[other] = Math.max(1, value * ratio);
            }
            layer[property] = value; if (property === 'hue') layer.tint = null;
        }
        render(); return state();
    }
    function input(event) {
        const control = event.target;
        if (!control.closest('[data-adjustment-scope="layer"]') && !control.dataset?.gifBound) return;
        const property = control.dataset?.layerProperty;
        const clip = active()?.gifSource ? active() : leafLayers(layers).find(item => item.gifSource);
        if (control.dataset?.gifBound && clip) {
            let value = Number(control.value); if (!Number.isFinite(value)) return;
            if (editingControl !== control) { remember(); editingControl = control; }
            const layer = clip;
            if (control.dataset.gifBound === 'start') {
                layer.trimStart = clamp(value, 0, layer.trimEnd - 0.02);
                if (layer.trimEnd - layer.trimStart > 10) layer.trimEnd = layer.trimStart + 10;
            } else {
                layer.trimEnd = clamp(value, layer.trimStart + 0.02, layer.gifDuration);
                if (layer.trimEnd - layer.trimStart > 10) layer.trimStart = layer.trimEnd - 10;
            }
            for (const field of root.querySelectorAll('[data-gif-bound]'))
                field.value = String(field.dataset.gifBound === 'start' ? layer.trimStart : layer.trimEnd);
            const timeline = control.closest('.trim-timeline');
            const left = `${layer.trimStart / layer.gifDuration * 100}%`;
            const right = `${(layer.gifDuration - layer.trimEnd) / layer.gifDuration * 100}%`;
            timeline.querySelector('.timeline-mask.left').style.width = left;
            timeline.querySelector('.timeline-mask.right').style.width = right;
            timeline.querySelector('.timeline-selection').style.left = left;
            timeline.querySelector('.timeline-selection').style.right = right;
            playbackOffset = pausedTime = 0; playbackOrigin = performance.now(); render();
            clearTimeout(notifyTimer); notifyTimer = setTimeout(() => notify()?.catch(() => {}), 60);
            return;
        }
        if (!property || !active()) return;
        let value = ['tint','borderColor'].includes(property) ? control.value : Number(control.value);
        if (control.value === '' || (!['tint','borderColor'].includes(property) && !Number.isFinite(value))) return;
        if (['opacity', 'colorStrength'].includes(property)) value /= 100;
        if (editingControl !== control) { remember(); editingControl = control; }
        update(property, value, proportions(), false);
        // Update linked fields locally; publish metadata once the adjustment ends.
        for (const field of root.querySelectorAll('[data-adjustment-scope="layer"] [data-layer-property]')) {
            if (field === control || ['tint','borderColor'].includes(field.dataset.layerProperty)) continue;
            const prop = field.dataset.layerProperty;
            field.value = String(Math.round(active()[prop] * (['opacity', 'colorStrength'].includes(prop) ? 100 : 1) * 100) / 100);
        }
        clearTimeout(notifyTimer);
        notifyTimer = setTimeout(() => notify()?.catch(() => {}), 60);
        for (const readout of root.querySelectorAll('[data-adjustment-scope="layer"] [data-layer-readout]')) {
            const prop = readout.dataset.layerReadout;
            readout.textContent = String(Math.round(active()[prop] * (['opacity', 'colorStrength'].includes(prop) ? 100 : 1)));
        }
    }
    function change(event) {
        if (!event.target.closest('[data-adjustment-scope="layer"]') && !event.target.dataset?.gifBound) return;
        if (event.target.hasAttribute('data-layer-ratio')) { keepProportions = event.target.checked; return; }
        if (!event.target.dataset?.layerProperty && !event.target.dataset?.gifBound) return;
        editingControl = null;
        clearTimeout(notifyTimer);
        notify()?.catch(() => {});
    }
    const wrapAngle = angle => ((angle + 180) % 360 + 360) % 360 - 180;
    function dialAngle(event, element) {
        const bounds = element.getBoundingClientRect();
        const x = event.clientX - bounds.left - bounds.width / 2;
        const y = event.clientY - bounds.top - bounds.height / 2;
        if (Math.hypot(x, y) < 10) return null;
        let angle = Math.atan2(x, -y) * 180 / Math.PI;
        angle = event.shiftKey ? Math.round(angle / 15) * 15 : Math.round(angle);
        return wrapAngle(angle);
    }
    function dialDown(event) {
        const element = event.target.closest?.('[data-layer-dial]');
        if (!element?.closest('[data-adjustment-scope="layer"]') || !active() || drag || dialDrag || event.button !== 0 || root.getAttribute('aria-busy') === 'true') return;
        const angle = dialAngle(event, element);
        if (angle === null) return;
        event.preventDefault(); event.stopPropagation();
        editingControl = null; remember();
        dialDrag = { pointer: event.pointerId, element };
        element.setPointerCapture(event.pointerId);
        update('rotation', angle, false, false);
    }
    function dialMove(event) {
        if (!dialDrag || event.pointerId !== dialDrag.pointer) return;
        const angle = dialAngle(event, dialDrag.element);
        if (angle !== null) update('rotation', angle, false, false);
    }
    function dialUp(event) {
        if (!dialDrag || event.pointerId !== dialDrag.pointer) return;
        if (dialDrag.element.hasPointerCapture(event.pointerId)) dialDrag.element.releasePointerCapture(event.pointerId);
        dialDrag = null; notify()?.catch(() => {});
    }
    function dialKey(event) {
        if (!event.target.closest?.('[data-adjustment-scope="layer"]') || !event.target.matches?.('[data-layer-dial]') || !active() || root.getAttribute('aria-busy') === 'true') return;
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault(); event.stopPropagation();
        const direction = event.key === 'ArrowRight' || event.key === 'ArrowUp' ? 1 : -1;
        const angle = event.key === 'Home' ? -180 : event.key === 'End' ? 180 : wrapAngle(active().rotation + direction * (event.shiftKey ? 15 : 1));
        update('rotation', angle, false); notify()?.catch(() => {});
    }
    root.addEventListener('pointerdown', dialDown);
    root.addEventListener('pointermove', dialMove);
    root.addEventListener('pointerup', dialUp);
    root.addEventListener('pointercancel', dialUp);
    root.addEventListener('keydown', dialKey);
    function reorder(order) {
        if (order.length !== layers.length || new Set(order).size !== layers.length || order.some(id => !layers.some(layer => layer.id === id))) return;
        if (order.every((id, index) => layers[index].id === id)) return;
        const id = active()?.id; remember();
        const byId = new Map(layers.map(layer => [layer.id, layer]));
        layers = order.map(id => byId.get(id)); selected = layers.findIndex(layer => layer.id === id);
        render(); notify()?.catch(() => {});
    }
    const orderControls = watchLayerOrder(root, reorder, (id, direction) => {
        const order = layers.map(layer => layer.id), from = order.indexOf(id), to = from + direction;
        if (from < 0 || to < 0 || to >= order.length) return;
        [order[from], order[to]] = [order[to], order[from]]; reorder(order);
    });
    const resizeObserver = new ResizeObserver(() => { if (!disposed) render(); });
    resizeObserver.observe(canvas);
    root.addEventListener('input', input);
    root.addEventListener('change', change);
    canvas.addEventListener('pointerdown', down); canvas.addEventListener('pointermove', move);
    canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', up);
    async function textArtwork(options,replaced=null) {
        const data=fontRegistry.get(options.font)?.data ?? options.fontData;
        const artwork=await renderText({...options,fontData:options.font.startsWith('custom-')?data:null});
        if(layers.reduce((sum,item)=>sum+sourceSize(item),0)-(replaced?sourceSize(replaced):0)+artwork.source.length+(data?.length??0)>maxSources)
            throw new Error('Text and embedded fonts exceed the 24 MB composition limit.');
        return artwork;
    }
    function positionText(layer,placement) {
        if(placement==='free')return;
        layer.x=256; layer.y=placement==='top'?16+layer.height/2:placement==='bottom'?496-layer.height/2:256;
    }
    return {
        setFonts(fonts) { for(const font of fonts)fontRegistry.set(font.id,font); },
        validateFont(id,data) { return validateTextFont(data); },
        async addText(placement='random') {
            if(leafLayers(layers).length>=100)throw new Error('A composition supports at most 100 grouped layers.');
            if(layers.length>=maxLayers)throw new Error('A composition supports up to 10 layers.');
            if(placement==='random')placement=Math.random()<0.5?'top':'bottom';
            const content=placement==='top'?'TOP TEXT':placement==='bottom'?'BOTTOM TEXT':'YOUR TEXT';
            const artwork=await textArtwork({...defaultText(),content});const scale=Math.min(1,480/artwork.width,480/artwork.height);
            const layer={id:`text-${Date.now()}-${Math.random().toString(36).slice(2)}`,name:'Text: '+content,source:artwork.source,text:artwork.text,x:256,y:256,width:artwork.width*scale,height:artwork.height*scale,rotation:0,opacity:1,flipX:false,tint:null,hue:0,colorStrength:1,borderWidth:0,borderColor:'#ffffff'};
            remember();positionText(layer,placement);layers.push(layer);images.set(layer.source,artwork.image);selected=layers.length-1;render();return state();
        },
        async updateText(options) {
            const layer=active();if(!layer?.text)throw new Error('Select a text layer.');
            const artwork=await textArtwork({...options,fontData:options.font===layer.text.font?layer.text.fontData:null},layer);
            const scaleX=layer.width/images.get(layer.source).width,scaleY=layer.height/images.get(layer.source).height;
            remember();layer.source=artwork.source;layer.text=artwork.text;layer.name='Text: '+(options.content.trim().slice(0,100)||'Empty');
            layer.width=clamp(artwork.width*scaleX,1,2048);layer.height=clamp(artwork.height*scaleY,1,2048);
            images.set(layer.source,artwork.image);tints.delete(layer.id);render();return state();
        },
        placeText(placement) { if(active()?.text){remember();positionText(active(),placement);render();}return state(); },
        preview(playing) {
            if (playing && paused) { playbackOffset = pausedTime; playbackOrigin = performance.now(); paused = false; animate(); }
            else if (!playing && !paused) { pausedTime = elapsed(); paused = true; cancelAnimationFrame(animationFrame); animationFrame = undefined; render(false); }
        },
        beginTool() { toolCheckpoint = { document: snapshot(), undo: [...undo], redo: [...redo] }; },
        hasToolChanges() { return !!toolCheckpoint && JSON.stringify(layers)!==JSON.stringify(toolCheckpoint.document.layers); },
        commitTool() { toolCheckpoint = null; return state(); },
        cancelTool() {
            if (toolCheckpoint) {
                layers = cloneLayers(toolCheckpoint.document.layers); groupFrames.clear(); selected = toolCheckpoint.document.selected;
                undo = toolCheckpoint.undo; redo = toolCheckpoint.redo; toolCheckpoint = null;
                render();
            }
            return state();
        },
        focusPanel() { render(); const panel = root.querySelector('.layer-tool-panel'); panel?.focus({ preventScroll: true }); },
        rotateBy(degrees) { return active() ? update('rotation', wrapAngle(active().rotation + degrees), false) : state(); },
        async open(composition) {
            layers = []; groupFrames.clear(); groupSelection.clear(); selected = -1; undo = []; redo = []; images.clear(); tints.clear(); for (const gif of gifs.values()) gif.reader.dispose?.(); gifs.clear();
            if (composition) {
                for (const layer of leafLayers(composition.layers)) {
                    const image = await decode(layer.source);
                    if (disposed) return;
                    images.set(layer.source, image);
                    if (layer.gifSource && !gifs.has(layer.gifSource)) gifs.set(layer.gifSource, await openGif(layer.gifSource));
                }
                layers = cloneLayers(composition.layers);
                selected = layers.length - 1;
            }
            playbackOffset = pausedTime = 0; playbackOrigin = performance.now(); paused = false; render(); animate(); return state();
        },
        async add(name, source, fitLimit=400) {
            if (leafLayers(layers).length>=100) throw new Error('A composition supports at most 100 grouped layers.');
            if (layers.length >= maxLayers) throw new Error('A composition supports up to 10 layers.');
            const gif = isAnimatedSource(source) ? await openGif(source) : null;
            const image = gif ? gif.reader.at(0) : await decode(source);
            if (disposed) return state();
            const sourceWidth = image.naturalWidth || image.width, sourceHeight = image.naturalHeight || image.height;
            if (sourceWidth * sourceHeight > 16_000_000) throw new Error('Choose an image smaller than 16 megapixels.');
            const normalized = document.createElement('canvas');
            const scale = Math.min(1, 1024 / Math.max(sourceWidth, sourceHeight));
            normalized.width = Math.max(1, Math.round(sourceWidth * scale));
            normalized.height = Math.max(1, Math.round(sourceHeight * scale));
            normalized.getContext('2d').drawImage(image, 0, 0, normalized.width, normalized.height);
            const png = normalized.toDataURL('image/png');
            if (layers.reduce((sum, layer) => sum + sourceSize(layer), png.length + (gif ? source.length : 0)) > maxSources)
                throw new Error('Layer images exceed 24 MB. Use smaller or fewer images.');
            const fit = Math.min(1, fitLimit / Math.max(normalized.width, normalized.height));
            const id = `layer-${Date.now()}-${Math.random().toString(36).slice(2)}`;
            const layer = { id, name: name.slice(0, 128) || 'Image', source: png, x: 256, y: 256,
                width: Math.max(1, Math.round(normalized.width * fit)), height: Math.max(1, Math.round(normalized.height * fit)),
                rotation: 0, opacity: 1, flipX: false, tint: null, hue: 0, colorStrength: 1, borderWidth:0,borderColor:'#ffffff', gifSource: gif ? source : null,
                gifDuration: gif ? gif.reader.duration / 1000 : 0, trimStart: 0, trimEnd: gif ? Math.min(10, gif.reader.duration / 1000) : 0 };
            const still = await decode(png);
            remember(); layers.push(layer); images.set(png, still); if (gif) gifs.set(source, gif); selected = layers.length - 1;
            playbackOffset = pausedTime = 0; playbackOrigin = performance.now(); paused = false; render(); animate(); return state();
        },
        source() { return active()?.gifSource ?? active()?.source ?? null; },
        poster() { return active()?.source ?? null; },
        async replaceSource(id, source, animationSource = null) {
            const layer = active();
            if (layer?.children?.length) throw new Error('Ungroup to edit individual contents.');
            if (!layer || layer.id !== id) throw new Error('Select a layer to edit.');
            const image = await decode(source);
            if (image.width > 1024 || image.height > 1024) throw new Error('Layer artwork must fit 1024 pixels per side.');
            if (layers.reduce((sum, item) => sum + sourceSize(item), 0) - layer.source.length - (layer.gifSource?.length ?? 0) + source.length + (animationSource?.length ?? 0) > maxSources)
                throw new Error('Layer images exceed 24 MB. Use smaller or fewer images.');
            const animation = animationSource ? await openGif(animationSource) : null;
            remember();
            // Keep the edited artwork's aspect ratio while preserving its displayed scale.
            const oldImage = images.get(layer.source);
            const scaleX = layer.width / oldImage.width, scaleY = layer.height / oldImage.height;
            layer.width = clamp(image.width * scaleX, 1, 2048);
            layer.height = clamp(image.height * scaleY, 1, 2048);
            layer.source = source; layer.text = null; images.set(source, image); tints.delete(layer.id);
            layer.gifSource = animationSource;
            if (animation) {
                gifs.set(animationSource, animation); layer.gifDuration = animation.reader.duration / 1000;
                layer.trimStart = 0; layer.trimEnd = Math.min(10, layer.gifDuration);
            }
            render(); return state();
        },
        mark(index) { if(layers[index]) { const id=layers[index].id; if(groupSelection.has(id))groupSelection.delete(id);else groupSelection.add(id); }return state(); },
        group(name='Group') {
            const items=layers.filter(layer=>groupSelection.has(layer.id)); if(items.length<2)return state();
            const group=groupLayers(items,`group-${Date.now()}-${Math.random().toString(36).slice(2)}`,name);
            remember(); const last=layers.lastIndexOf(items.at(-1));
            layers=layers.flatMap((layer,index)=>index===last?[group]:groupSelection.has(layer.id)?[]:[layer]);
            groupSelection.clear(); selected=layers.indexOf(group);render();return state();
        },
        ungroup() {
            const group=active();if(!group?.children?.length)return state();
            if(layers.length-1+group.children.length>maxLayers)throw new Error('Ungrouping would exceed 10 layers. Remove other layers first.');
            remember(); layers.splice(selected,1,...leafLayers([group]));groupFrames.delete(group.id);groupSelection.clear();render();return state();
        },
        select(index) { editingControl = null; if (layers[index]) selected = index; render(); return state(); },
        update,
        action(action) {
            editingControl = null;
            if (action === 'undo' && undo.length) { redo.push(snapshot()); const saved = undo.pop(); layers = cloneLayers(saved.layers); groupFrames.clear(); selected = saved.selected; }
            else if (action === 'redo' && redo.length) { undo.push(snapshot()); const saved = redo.pop(); layers = cloneLayers(saved.layers); groupFrames.clear(); selected = saved.selected; }
            else if (active()) {
                const layer = active();
                if (action === 'duplicate') {
                    if (layers.length >= maxLayers || leafLayers(layers).length + leafLayers([layer]).length > 100 || layers.reduce((sum, item) => sum + sourceSize(item), sourceSize(layer)) > maxSources)
                        throw new Error('This composition has reached its layer or image-size limit.');
                    remember(); const id = `layer-${Date.now()}-${Math.random().toString(36).slice(2)}`;
                    const copy = structuredClone(layer); copy.id=id;
                    for(const child of copy.children??[])child.id=crypto.randomUUID();
                    if(copy.children?.length)groupFrames.set(id,{...copy});
                    copy.x=clamp(copy.x+12,-2048,2560);copy.y=clamp(copy.y+12,-2048,2560);
                    layers.splice(selected + 1, 0, copy);
                    selected++;
                } else if (action === 'front' && selected < layers.length - 1) {
                    remember(); layers.splice(selected, 1); layers.splice(++selected, 0, layer);
                } else if (action === 'back' && selected > 0) {
                    remember(); layers.splice(selected, 1); layers.splice(--selected, 0, layer);
                } else if (action === 'remove') { remember(); layers.splice(selected, 1); selected = Math.min(selected, layers.length - 1); }
                else if (action === 'rotate') { remember(); layer.rotation = ((layer.rotation + 270) % 360 + 360) % 360 - 180; }
                else if (action === 'flip') { remember(); layer.flipX = !layer.flipX; }
                else if (action === 'center') { remember(); layer.x = layer.y = 256; }
            }
            render(); animate(); return state();
        },
        async export() {
            if (!layers.length) throw new Error('Add at least one layer before saving.');
            exporting = true; cancelAnimationFrame(animationFrame); animationFrame = undefined;
            render(false);
            try {
                if (!leafLayers(layers).some(layer => layer.gifSource) && !context.getImageData(0, 0, size, size).data.some((value, index) => index % 4 === 3 && value > 0))
                    throw new Error('The sticker is completely transparent. Move a visible layer onto the canvas.');
                const document = { version: layers.some(layer=>layer.children?.length)?2:1, layers: cloneLayers(layers) };
                const png = leafLayers(layers).some(layer => layer.gifSource)
                    ? await (await import('../animation/gif-editor.js')).compose(document)
                    : canvas.toDataURL('image/png');
                return { png, document };
            } finally { exporting = false; render(); animate(); }
        },
        dispose() { view.dispose();
            disposed = true; toolCheckpoint = null; controls.dispose();
            cancelAnimationFrame(animationFrame); orderControls.dispose();
            clearTimeout(notifyTimer);
            resizeObserver.disconnect(); overlay.remove();
            root.removeEventListener('pointerdown', dialDown); root.removeEventListener('pointermove', dialMove);
            root.removeEventListener('pointerup', dialUp); root.removeEventListener('pointercancel', dialUp);
            root.removeEventListener('keydown', dialKey);
            root.removeEventListener('input', input); root.removeEventListener('change', change);
            root.removeEventListener('cancel', blockCancel);
            canvas.removeEventListener('pointerdown', down); canvas.removeEventListener('pointermove', move);
            canvas.removeEventListener('pointerup', up); canvas.removeEventListener('pointercancel', up);
            layers = []; undo = []; redo = []; images.clear(); tints.clear(); for (const gif of gifs.values()) gif.reader.dispose?.(); gifs.clear();
        }
    };
}
