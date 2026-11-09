import { timed } from '../../ui/performance.js';
import { onForegroundChanged } from '../../ui/foreground.js';
import { snapRotation } from '../shared/rotation-snap.js';
import { editorView, cutoutLoupe } from '../shared/editor-view.js';
import { applyStickerBorder } from '../effects/sticker-border.js';
import { applyColorEffect } from '../effects/color-effect.js';
import { createTransformHandles } from './image-transform.js';
import { createResizeHandles } from './image-resize.js';
import { previewControls } from '../animation/animation-preview.js';
import { openAnimation } from '../animation/animation-reader.js';
import { createRasterRenderer, eraseConnected } from '../effects/raster-edits.js';
// Pointer gestures stay in the WebView so drawing remains responsive offline.
export function create(canvas, dotnet) {
    const context = canvas.getContext('2d', { willReadFrequently: true });
    let tool = 'crop', points = [], start, snapshot, history = [], redo = [], original, busy = false, disposed = false;
    let adjustmentBase = null, adjustment = { rotation: 0, opacity: 1, tint: null, hue: 0, colorStrength: 1 }, editingControl = null, maxSize = 4096;
    let toolCheckpoint = null, pendingControl = null;
    let operations = [], initialWidth = 0, initialHeight = 0, cutoutMask = null, paintLayer = null, animationReader = null, playback = null;
    const unwatchForeground = onForegroundChanged(active => { if (!active) { stopPreview(); ++seekVersion; dotnet.invokeMethodAsync('PreviewChanged', false).catch(() => {}); } });
    const adjustmentEdit = () => ({ type: 'adjust', ...adjustment, maxSize });
    const hasAdjustment = () => adjustmentBase && (adjustment.rotation !== 0 || adjustment.opacity !== 1 || adjustment.tint !== null || adjustment.hue !== 0 && adjustment.colorStrength > 0);
    const recipe = () => ({ width: initialWidth, height: initialHeight, edits: [...operations, ...(hasAdjustment() ? [adjustmentEdit()] : [])] });
    const stopPreview = () => {
        if (!playback) return;
        cancelAnimationFrame(playback.frame); playback.renderer.dispose();
        canvas.width = playback.still.width; canvas.height = playback.still.height;
        context.drawImage(playback.still, 0, 0); playback = null;
        if (!disposed) dotnet.invokeMethodAsync('PreviewChanged', false).catch(() => {});
    };
    let previewRate = 1, previewTime = 0, seekVersion = 0;
    const root = canvas.closest('.image-customizer');
    const view = editorView(canvas,root), loupe=cutoutLoupe(canvas,root);
    const syncPreview = () => {
        if (!canvas.parentElement.classList.contains('customizer-canvas-wrap')) return;
        canvas.style.width = `${canvas.width / 512 * 100}%`;
        canvas.style.height = `${canvas.height / 512 * 100}%`;
    };
    const canvasObserver = new MutationObserver(syncPreview);
    canvasObserver.observe(canvas, { attributes: true, attributeFilter: ['width', 'height'] });
    const legacyResizer = createResizeHandles(canvas, root, (w, h) => dotnet.invokeMethodAsync('ResizeChanged', w, h).catch(() => {}), syncPreview);
    const transformer = createTransformHandles(canvas, root, (w,h) => dotnet.invokeMethodAsync('ResizeChanged',w,h).catch(()=>{}), syncPreview);
    let resizer = legacyResizer;
    const copyCanvas = source => {
        const copy = document.createElement('canvas'); copy.width = source.width; copy.height = source.height;
        copy.getContext('2d').drawImage(source, 0, 0); return copy;
    };
    const state = () => ({ source: canvas.toDataURL('image/png'), base: adjustmentBase?.toDataURL('image/png'), adjustment: { ...adjustment }, operations: [...operations] });
    const publishAdjustments = () => disposed ? Promise.resolve() : dotnet.invokeMethodAsync('AdjustmentsChanged', adjustment.rotation, adjustment.opacity, adjustment.tint, adjustment.hue, adjustment.colorStrength);
    const commitAdjustments = () => {
        if (hasAdjustment()) operations.push(adjustmentEdit());
        adjustmentBase = null; adjustment = { rotation: 0, opacity: 1, tint: null, hue: 0, colorStrength: 1 }; editingControl = null;
        return publishAdjustments();
    };
    let tolerance = 36, brush = 40, brushColor = "#ff4d6d", brushType = "pen";
    const notify = () => disposed ? Promise.resolve() : dotnet.invokeMethodAsync('ImageChanged', canvas.toDataURL('image/png'), canvas.width, canvas.height, recipe());
    const remember = () => { history.push(state()); redo = []; if (history.length > 8) history.shift(); };
    const load = async (source, limit = 0) => {
        let image = source;
        if (typeof source === 'string') { image = new Image(); image.src = source; await image.decode(); }
        const scale = limit ? Math.min(1, limit / Math.max(image.width, image.height)) : 1;
        canvas.width = Math.max(1, Math.round(image.width * scale)); canvas.height = Math.max(1, Math.round(image.height * scale));
        context.drawImage(image, 0, 0, canvas.width, canvas.height);
        points = []; snapshot = null;
    };
    const clearSelection = () => { if (snapshot) context.putImageData(snapshot, 0, 0); snapshot = null; points = []; };
    const position = event => {
        const rect = canvas.getBoundingClientRect();
        return { x: Math.max(0, Math.min(canvas.width, (event.clientX - rect.left) * canvas.width / rect.width)),
                 y: Math.max(0, Math.min(canvas.height, (event.clientY - rect.top) * canvas.height / rect.height)) };
    };
    const path = (target = context) => {
        target.beginPath();
        points.forEach((p, i) => i ? target.lineTo(p.x, p.y) : target.moveTo(p.x, p.y));
        target.closePath();
    };
    const preview = () => {
        context.putImageData(snapshot, 0, 0);
        context.save(); context.strokeStyle = '#80d4b0'; context.lineWidth = Math.max(2, canvas.width / canvas.clientWidth * 2);
        context.setLineDash([context.lineWidth * 3, context.lineWidth * 2]); path(); context.stroke(); context.restore();
    };
    const makeMask = () => {
        const mask = document.createElement('canvas'); mask.width = canvas.width; mask.height = canvas.height;
        const ctx = mask.getContext('2d'); ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, mask.width, mask.height); return mask;
    };
    const erase = (from, to) => {
        for (const target of [context, cutoutMask.getContext('2d')]) {
            target.save(); target.globalCompositeOperation = 'destination-out';
            target.lineWidth = brush * canvas.width / canvas.clientWidth; target.lineCap = 'round';
            target.beginPath(); target.moveTo(from.x, from.y); target.lineTo(to.x + .01, to.y + .01); target.stroke(); target.restore();
        }
    };
    const paint = (from,to) => {
        for(const target of [context,paintLayer.getContext('2d')]) {
            target.save();target.globalCompositeOperation='source-over';target.strokeStyle=brushColor;
            const width=brush*canvas.width/canvas.clientWidth;
            target.lineWidth=width;target.lineCap=brushType==='marker'?'square':'round';target.lineJoin='round';
            if(brushType==='pencil'){target.globalAlpha=.45;target.lineWidth=width*.55;}
            if(brushType==='brush'){target.shadowColor=brushColor;target.shadowBlur=width*.25;target.lineWidth=width*.7;}
            if(brushType==='marker')target.globalAlpha=.25;
            target.beginPath();target.moveTo(from.x,from.y);target.lineTo(to.x+.01,to.y+.01);target.stroke();target.restore();
        }
    };
    const wand = point => {
        const edit = eraseConnected(context, point, tolerance);
        if (edit) operations.push(edit);
    };
    const down = event => {
        if (disposed || busy || start || !tool || tool === 'adjust' || tool === 'resize' || tool === 'border') return;
        stopPreview(); event.preventDefault(); clearSelection(); canvas.setPointerCapture(event.pointerId);
        start = position(event); loupe.show(event,start);
        if (tool === 'erase' || tool === 'wand' || tool === 'draw') remember();
        if (tool === 'erase') cutoutMask = makeMask();
        if(tool==='draw'){paintLayer=document.createElement('canvas');paintLayer.width=canvas.width;paintLayer.height=canvas.height;}
        if (tool === 'erase') erase(start, start);
        else if(tool==='draw')paint(start,start);
        else if (tool === 'wand') wand(start);
        else { snapshot = context.getImageData(0, 0, canvas.width, canvas.height); points = [start]; }
    };
    const move = event => {
        if (!start || busy) return;
        const p = position(event); loupe.show(event,p);
        if (tool === 'erase') { erase(start, p); start = p; }
        else if(tool==='draw'){paint(start,p);start=p;}
        else if (tool === 'crop') { points = [start, { x: p.x, y: start.y }, p, { x: start.x, y: p.y }]; preview(); }
        else if (tool === 'lasso') { points.push(p); preview(); }
    };
    const up = async event => {
        if (!start) return;
        start = null; loupe.hide();
        if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
        if (tool === 'erase') { operations.push({ type: 'mask', mask: cutoutMask.toDataURL('image/png') }); cutoutMask = null; }
        if(tool==='draw'){operations.push({type:'paint',source:paintLayer.toDataURL('image/png')});paintLayer=null;}
        if (tool === 'erase' || tool === 'wand' || tool === 'draw') { busy = true; try { await notify(); } finally { busy = false; } }
    };
    const restore = async saved => {
        stopPreview(); await load(saved.source); operations = [...saved.operations];
        adjustment = { ...saved.adjustment }; adjustmentBase = null;
        if (saved.base) {
            const image = new Image(); image.src = saved.base; await image.decode();
            adjustmentBase = copyCanvas(image);
        }
        editingControl = null;
        if (tool !== 'adjust') await commitAdjustments(); else await publishAdjustments();
        await notify();
    };
    const beginAdjustments = () => {
        stopPreview(); clearSelection(); tool = 'adjust';
        if (!adjustmentBase) adjustmentBase = copyCanvas(canvas);
    };
    const renderAdjustments = () => {
        if (!adjustmentBase) return;
        const base = adjustmentBase, angle = adjustment.rotation * Math.PI / 180;
        const w = Math.abs(base.width * Math.cos(angle)) + Math.abs(base.height * Math.sin(angle));
        const h = Math.abs(base.width * Math.sin(angle)) + Math.abs(base.height * Math.cos(angle));
        const scale = Math.min(1, maxSize / Math.max(w, h));
        canvas.width = Math.max(1, Math.ceil(w * scale - 1e-8)); canvas.height = Math.max(1, Math.ceil(h * scale - 1e-8));
        const source = base;
        context.save(); context.globalAlpha = adjustment.opacity;
        context.translate(canvas.width / 2, canvas.height / 2); context.rotate(angle); context.scale(scale, scale);
        context.drawImage(source, -base.width / 2, -base.height / 2); context.restore();
        applyColorEffect(context, adjustment);
        syncPreview();
        for (const readout of root.querySelectorAll('[data-layer-readout]')) {
            const property = readout.dataset.layerReadout;
            readout.textContent = String(Math.round(adjustment[property] * (['opacity', 'colorStrength'].includes(property) ? 100 : 1)));
        }
        for(const field of root.querySelectorAll('[data-adjustment-scope="image"] [data-layer-property]')) {
            const property=field.dataset.layerProperty;
            if(field===editingControl || !['rotation','opacity','hue','colorStrength'].includes(property))continue;
            field.value=String(Math.round(adjustment[property]*(['opacity','colorStrength'].includes(property)?100:1)));
        }
        const dial = root.querySelector('[data-layer-dial]');
        dial?.setAttribute('aria-valuenow', String(adjustment.rotation));
        dial?.querySelector('[data-layer-dial-needle]')?.setAttribute('transform', `rotate(${adjustment.rotation} 100 100)`);
    };
    const adjust = async (property, value, record = true, publish = true) => {
        if (property === 'tint' ? value !== null && !/^#[0-9a-f]{6}$/i.test(value) : !Number.isFinite(value))
            throw new Error('Choose a valid image adjustment.');
        beginAdjustments(); if (record) remember();
        if (property === 'hue') adjustment.tint = null;
        adjustment[property] = property === 'hue' ? Math.max(-180, Math.min(180, value)) : property === 'rotation' ? snapRotation(root, value)
            : ['opacity', 'colorStrength'].includes(property) ? Math.max(0, Math.min(1, value)) : value;
        renderAdjustments();
        if (publish) { await publishAdjustments(); await notify(); }
    };
    async function seekPreview(seconds) {
        if (!animationReader || disposed || busy || resizer.active) return;
        stopPreview(); clearSelection();
        const version = ++seekVersion;
        await commitAdjustments();
        const renderer = await createRasterRenderer(recipe());
        try {
            if (version !== seekVersion || disposed) return;
            previewTime = Math.max(0, Math.min(animationReader.duration / 1000 - 0.001, seconds));
            const frame = renderer.render(animationReader.at(previewTime * 1000));
            canvas.width = frame.width; canvas.height = frame.height; context.drawImage(frame, 0, 0);
            controls.update(previewTime);
        } finally { renderer.dispose(); }
    }
    const controls = previewControls(root, {
        seek: seekPreview,
        speed(rate) {
            if (![0.1, 0.25, 0.5, 1].includes(rate)) return;
            if (playback) { playback.offset = (previewTime - playback.start) * 1000; playback.origin = performance.now(); }
            previewRate = rate;
        },
        error: error => dotnet.invokeMethodAsync('PreviewFailed', error.message).catch(() => {})
    });
    const input = event => {
        const control = event.target;
        if (!control.closest('[data-adjustment-scope="image"]')) return;
        if (busy) { pendingControl = control; return; }
        const property = control.dataset.layerProperty;
        if (!property) return;
        const value = property === 'tint' ? control.value : Number(control.value);
        if (control.value === '' || (property !== 'tint' && !Number.isFinite(value))) return;
        if (property === 'width' || property === 'height') {
            if (root.querySelector('[data-layer-ratio]')?.checked) {
                const other = property === 'width' ? 'height' : 'width';
                root.querySelector(`[data-layer-property="${other}"]`).value = String(Math.max(1, Math.round(value * canvas[other] / canvas[property])));
            }
            return;
        }
        if (editingControl !== control) { beginAdjustments(); remember(); editingControl = control; }
        // No interop roundtrip for live slider previews; publish when the gesture ends.
        adjust(property, ['opacity', 'colorStrength'].includes(property) ? value / 100 : value, false, false);
    };
    const change = async event => {
        if (!event.target.closest('[data-adjustment-scope="image"]') || !event.target.dataset.layerProperty) return;
        if (busy) { pendingControl = event.target; return; }
        if (['width', 'height'].includes(event.target.dataset.layerProperty)) return;
        editingControl = null; busy = true;
        try { await publishAdjustments(); await notify(); } finally { busy = false; await flushPendingControl(); }
    };
    async function flushPendingControl() {
        const control = pendingControl; pendingControl = null;
        if (!control?.isConnected || disposed) return;
        input({ target: control }); await change({ target: control });
    }
    // The numeric slider and presets remain available alongside the desktop dial.
    let dialDrag;
    const dialAngle = (event, element) => {
        const rect = element.getBoundingClientRect();
        return Math.round(Math.atan2(event.clientX - rect.left - rect.width / 2, -(event.clientY - rect.top - rect.height / 2)) * 180 / Math.PI);
    };
    const dialDown = event => {
        const dial = event.target.closest?.('[data-layer-dial]');
        if (!dial || !dial.closest('[data-adjustment-scope="image"]') || busy) return;
        event.preventDefault(); beginAdjustments(); remember();
        dialDrag = { element: dial, pointer: event.pointerId }; dial.setPointerCapture(event.pointerId);
        adjust('rotation', dialAngle(event, dial), false, false);
    };
    const dialMove = event => {
        if (dialDrag?.pointer === event.pointerId) adjust('rotation', dialAngle(event, dialDrag.element), false, false);
    };
    const dialUp = async event => {
        if (dialDrag?.pointer !== event.pointerId) return;
        if (dialDrag.element.hasPointerCapture(event.pointerId)) dialDrag.element.releasePointerCapture(event.pointerId);
        dialDrag = null; await publishAdjustments(); await notify();
    };
    const dialKey = async event => {
        if (!event.target.matches?.('[data-layer-dial]') || !event.target.closest('[data-adjustment-scope="image"]') || busy) return;
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault(); event.stopPropagation();
        const direction = ['ArrowRight', 'ArrowUp'].includes(event.key) ? 1 : -1;
        await adjust('rotation', event.key === 'Home' ? -180 : event.key === 'End' ? 180 : adjustment.rotation + direction * (event.shiftKey ? 15 : 1));
    };
    root.addEventListener('input', input); root.addEventListener('change', change);
    root.addEventListener('pointerdown', dialDown); root.addEventListener('pointermove', dialMove);
    root.addEventListener('pointerup', dialUp); root.addEventListener('pointercancel', dialUp); root.addEventListener('keydown', dialKey);
    canvas.addEventListener('pointerdown', down); canvas.addEventListener('pointermove', move);
    canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', up);
    const controller = {
        async open(source, initialLimit = 2048, maximumSize = 4096, animated = false) {
            resizer.end(); stopPreview(); ++seekVersion; previewTime = 0; animationReader?.dispose?.(); animationReader = null;
            if (animated) animationReader = await openAnimation(source);
            if (disposed) { animationReader?.dispose?.(); animationReader = null; return { width: 0, height: 0, recipe: {} }; }
            maxSize = maximumSize; await timed('Image editor: first frame and canvas', () => load(animationReader ? animationReader.at(0) : source, Math.min(512, initialLimit))); history = []; redo = [];
            adjustmentBase = null; operations = []; toolCheckpoint = null; initialWidth = canvas.width; initialHeight = canvas.height;
            original = canvas.toDataURL('image/png'); await commitAdjustments();
            return { width: canvas.width, height: canvas.height, recipe: recipe() };
        },
        async preview(start = 0, end = null) {
            if (playback) { stopPreview(); return; }
            if (resizer.active) await controller.resizeAdjustment();
            if (!animationReader) return;
            clearSelection();
            const renderer = await createRasterRenderer(recipe()), still = copyCanvas(canvas);
            ++seekVersion;
            const last = end ?? animationReader.duration / 1000;
            const offset = previewTime >= start && previewTime < last ? (previewTime - start) * 1000 : 0;
            playback = { renderer, still, start, end, offset, origin: performance.now() };
            const draw = now => {
                if (!playback || disposed) return;
                const { start, end, origin, offset } = playback;
                const duration = Math.max(20, (end ?? animationReader.duration / 1000) * 1000 - start * 1000);
                previewTime = start + (offset + (now - origin) * previewRate) % duration / 1000;
                const frame = renderer.render(animationReader.at(previewTime * 1000));
                controls.update(previewTime);
                canvas.width = frame.width; canvas.height = frame.height; context.drawImage(frame, 0, 0);
                playback.frame = requestAnimationFrame(draw);
            };
            playback.frame = requestAnimationFrame(draw); await dotnet.invokeMethodAsync('PreviewChanged', true);
        },
        setPreviewRange(start, end) {
            if (playback) { playback.start = start; playback.end = end; playback.offset = 0; playback.origin = performance.now(); }
        },
        seek: seekPreview,
        beginAdjustments, adjust,
        adjustBy(degrees) { return adjust('rotation', adjustment.rotation + degrees); },
        beginTransform() { stopPreview(); clearSelection(); tool = 'resize'; resizer = transformer; resizer.begin(); },
        beginResize() { resizer = legacyResizer; stopPreview(); clearSelection(); tool = 'resize'; resizer.begin(Math.min(512, maxSize)); },
        async resizeAdjustment() {
            const resized = resizer.end(); if (!resized) return;
            const { width, height, source } = resized;
            remember(); await commitAdjustments();
            if (resized.edit) {
                const renderer = await createRasterRenderer({width:source.width,height:source.height,edits:[resized.edit]});
                try { const result=renderer.render(source); canvas.width=result.width; canvas.height=result.height; context.drawImage(result,0,0); } finally { renderer.dispose(); }
                operations.push(resized.edit); syncPreview(); await notify(); return;
            }
            canvas.width = width; canvas.height = height;
            context.drawImage(source, 0, 0, width, height);
            operations.push({ type: 'resize', width, height }); await notify();
        },
        async border(thickness,color,radius) {
            if(!toolCheckpoint)return;
            if(!operations.some(edit=>edit.type==='border') || operations.length===toolCheckpoint.image.operations.length)remember();
            await load(toolCheckpoint.image.source);
            const edit={type:'border',thickness:Math.max(0,Math.min(80,thickness)),color,radius:Math.max(0,Math.min(256,radius))};
            applyStickerBorder(context,edit);operations=[...toolCheckpoint.image.operations,edit];await notify();
        },
        async beginTool(value) {
            stopPreview(); clearSelection(); ++seekVersion;
            if (resizer.active) await controller.resizeAdjustment();
            await commitAdjustments();
            // Switching tools keeps the previous tool's visible edits. Cancel
            // only rolls back work done since the current tool was selected.
            toolCheckpoint = { image: state(), undo: [...history], redo: [...redo] };
            tool = value;
        },
        hasToolChanges() {
            if (!toolCheckpoint) return false;
            // A pending crop/trace is work too, but brush settings, frame seeking
            // and entering the fixed transform workspace are preview-only.
            const selection = points.length >= 3 && Math.max(...points.map(p=>p.x))-Math.min(...points.map(p=>p.x)) >= 2 && Math.max(...points.map(p=>p.y))-Math.min(...points.map(p=>p.y)) >= 2;
            const edits = items => items.filter(edit=>!(edit.type==='border' && edit.thickness===0 && !edit.radius));
            return selection || !!resizer.hasChanges || !!hasAdjustment() || JSON.stringify(edits(operations))!==JSON.stringify(edits(toolCheckpoint.image.operations));
        },
        async applyTool(resize = false) {
            if (tool === 'crop' || tool === 'lasso') await controller.applySelection();
            else if (resize) await controller.resizeAdjustment();
            else { stopPreview(); clearSelection(); await commitAdjustments(); await notify(); }
            toolCheckpoint = null; tool = '';
        },
        async cancelTool() {
            stopPreview(); clearSelection(); ++seekVersion; resizer.end();
            if (toolCheckpoint) {
                const saved = toolCheckpoint; toolCheckpoint = null;
                tool = 'adjust'; await restore(saved.image); history = saved.undo; redo = saved.redo;
            }
            tool = '';
        },
        async setTool(value) { stopPreview(); clearSelection(); await commitAdjustments(); tool = value; },
        settings(size, threshold, color, tip="pen") { brush = Math.max(.5,Math.min(256,Number(size)||40)); brushType=["pen","pencil","brush","marker"].includes(tip)?tip:"pen"; tolerance = threshold; if(/^#[0-9a-f]{6}$/i.test(color??''))brushColor=color; },
        async applySelection() {
            if (!snapshot || points.length < 3) throw new Error('Draw a selection on the image first.');
            const minX = Math.floor(Math.min(...points.map(p => p.x))), minY = Math.floor(Math.min(...points.map(p => p.y)));
            const w = Math.ceil(Math.max(...points.map(p => p.x))) - minX, h = Math.ceil(Math.max(...points.map(p => p.y))) - minY;
            if (w < 2 || h < 2) throw new Error('Select a larger area.');
            context.putImageData(snapshot, 0, 0); remember();
            const output = document.createElement('canvas'); output.width = w; output.height = h;
            if (tool === 'lasso') {
                context.save(); context.globalCompositeOperation = 'destination-in'; path(); context.fill(); context.restore();
                const mask = document.createElement('canvas'); mask.width = canvas.width; mask.height = canvas.height;
                const ctx = mask.getContext('2d'); ctx.fillStyle = '#fff'; path(ctx); ctx.fill();
                operations.push({ type: 'mask', mask: mask.toDataURL('image/png') });
            }
            operations.push({ type: 'crop', x: minX, y: minY, width: w, height: h });
            output.getContext('2d').drawImage(canvas, minX, minY, w, h, 0, 0, w, h);
            canvas.width = w; canvas.height = h; context.drawImage(output, 0, 0); snapshot = null; points = [];
            await notify();
        },
        async transform(action, width, height) {
            if (resizer.active) await controller.resizeAdjustment();
            stopPreview(); clearSelection();
            if (action === 'resize' && (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 || width > 4096 || height > 4096))
                throw new Error('Choose dimensions between 1 and 4096 pixels.');
            remember(); await commitAdjustments();
            const copy = document.createElement('canvas'); copy.width = canvas.width; copy.height = canvas.height; copy.getContext('2d').drawImage(canvas, 0, 0);
            canvas.width = action === 'rotate' ? copy.height : action === 'resize' ? width : copy.width;
            canvas.height = action === 'rotate' ? copy.width : action === 'resize' ? height : copy.height;
            context.save();
            if (action === 'rotate') { context.translate(canvas.width, 0); context.rotate(Math.PI / 2); }
            operations.push(action === 'rotate' ? { type: 'adjust', rotation: 90, opacity: 1, tint: null, maxSize } : action === 'resize' ? { type: 'resize', width, height } : { type: 'flip' });
            if (action === 'flip') { context.translate(canvas.width, 0); context.scale(-1, 1); }
            context.drawImage(copy, 0, 0, action === 'resize' ? width : copy.width, action === 'resize' ? height : copy.height); context.restore();
            await notify();
        },
        async undo() { resizer.end(); stopPreview(); clearSelection(); if (history.length) { redo.push(state()); await restore(history.pop()); } },
        async redo() { resizer.end(); stopPreview(); clearSelection(); if (redo.length) { history.push(state()); await restore(redo.pop()); } },
        async reset() { resizer.end(); stopPreview(); clearSelection(); remember(); await load(original); adjustmentBase = null; operations = []; await commitAdjustments(); await notify(); },
        dispose() {
            unwatchForeground(); disposed = true; view.dispose(); loupe.dispose(); ++seekVersion; legacyResizer.dispose(); transformer.dispose(); canvasObserver.disconnect(); controls.dispose(); stopPreview(); animationReader?.dispose?.(); animationReader = null;
            root.removeEventListener('input', input); root.removeEventListener('change', change);
            root.removeEventListener('pointerdown', dialDown); root.removeEventListener('pointermove', dialMove);
            root.removeEventListener('pointerup', dialUp); root.removeEventListener('pointercancel', dialUp); root.removeEventListener('keydown', dialKey);
            adjustmentBase = null; redo = [];
            canvas.removeEventListener('pointerdown', down); canvas.removeEventListener('pointermove', move); canvas.removeEventListener('pointerup', up); canvas.removeEventListener('pointercancel', up); history = []; toolCheckpoint = null; snapshot = original = null; }
    };
    return controller;
}
