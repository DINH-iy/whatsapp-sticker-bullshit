// Pointer gestures stay in the WebView so drawing remains responsive offline.
export function create(canvas, dotnet) {
    const context = canvas.getContext('2d', { willReadFrequently: true });
    let tool = 'crop', points = [], start, snapshot, history = [], original, busy = false;
    let tolerance = 36, brush = 30;
    const notify = () => dotnet.invokeMethodAsync('ImageChanged', canvas.toDataURL('image/png'), canvas.width, canvas.height);
    const remember = () => { history.push(canvas.toDataURL('image/png')); if (history.length > 8) history.shift(); };
    const load = async (source, limit = false) => {
        const image = new Image(); image.src = source; await image.decode();
        const scale = limit ? Math.min(1, 2048 / Math.max(image.width, image.height)) : 1;
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
    const path = () => {
        context.beginPath();
        points.forEach((p, i) => i ? context.lineTo(p.x, p.y) : context.moveTo(p.x, p.y));
        context.closePath();
    };
    const preview = () => {
        context.putImageData(snapshot, 0, 0);
        context.save(); context.strokeStyle = '#80d4b0'; context.lineWidth = Math.max(2, canvas.width / canvas.clientWidth * 2);
        context.setLineDash([context.lineWidth * 3, context.lineWidth * 2]); path(); context.stroke(); context.restore();
    };
    const erase = (from, to) => {
        context.save(); context.globalCompositeOperation = 'destination-out';
        context.lineWidth = brush * canvas.width / canvas.clientWidth; context.lineCap = 'round';
        context.beginPath(); context.moveTo(from.x, from.y); context.lineTo(to.x + .01, to.y + .01); context.stroke(); context.restore();
    };
    const wand = point => {
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height), data = pixels.data;
        const w = canvas.width, h = canvas.height, seed = Math.min(h - 1, Math.floor(point.y)) * w + Math.min(w - 1, Math.floor(point.x));
        const target = Array.from(data.slice(seed * 4, seed * 4 + 4));
        if (!target[3]) return;
        const seen = new Uint8Array(w * h), stack = [seed]; seen[seed] = 1;
        while (stack.length) {
            const i = stack.pop(), o = i * 4;
            if (!data[o + 3] || Math.max(Math.abs(data[o] - target[0]), Math.abs(data[o + 1] - target[1]), Math.abs(data[o + 2] - target[2])) > tolerance) continue;
            data[o + 3] = 0;
            const x = i % w, y = Math.floor(i / w);
            for (const next of [x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1])
                if (next >= 0 && !seen[next]) { seen[next] = 1; stack.push(next); }
        }
        context.putImageData(pixels, 0, 0);
    };
    const down = event => {
        if (busy || start) return;
        event.preventDefault(); clearSelection(); canvas.setPointerCapture(event.pointerId);
        start = position(event);
        if (tool === 'erase' || tool === 'wand') remember();
        if (tool === 'erase') erase(start, start);
        else if (tool === 'wand') wand(start);
        else { snapshot = context.getImageData(0, 0, canvas.width, canvas.height); points = [start]; }
    };
    const move = event => {
        if (!start || busy) return;
        const p = position(event);
        if (tool === 'erase') { erase(start, p); start = p; }
        else if (tool === 'crop') { points = [start, { x: p.x, y: start.y }, p, { x: start.x, y: p.y }]; preview(); }
        else if (tool === 'lasso') { points.push(p); preview(); }
    };
    const up = async event => {
        if (!start) return;
        start = null;
        if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
        if (tool === 'erase' || tool === 'wand') { busy = true; try { await notify(); } finally { busy = false; } }
    };
    canvas.addEventListener('pointerdown', down); canvas.addEventListener('pointermove', move);
    canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', up);
    return {
        async open(source) { await load(source, true); history = []; original = canvas.toDataURL('image/png'); return { width: canvas.width, height: canvas.height }; },
        setTool(value) { clearSelection(); tool = value; },
        settings(size, threshold) { brush = size; tolerance = threshold; },
        async applySelection() {
            if (!snapshot || points.length < 3) throw new Error('Draw a selection on the image first.');
            const minX = Math.floor(Math.min(...points.map(p => p.x))), minY = Math.floor(Math.min(...points.map(p => p.y)));
            const w = Math.ceil(Math.max(...points.map(p => p.x))) - minX, h = Math.ceil(Math.max(...points.map(p => p.y))) - minY;
            if (w < 2 || h < 2) throw new Error('Select a larger area.');
            context.putImageData(snapshot, 0, 0); remember();
            const output = document.createElement('canvas'); output.width = w; output.height = h;
            if (tool === 'lasso') {
                context.save(); context.globalCompositeOperation = 'destination-in'; path(); context.fill(); context.restore();
            }
            output.getContext('2d').drawImage(canvas, minX, minY, w, h, 0, 0, w, h);
            canvas.width = w; canvas.height = h; context.drawImage(output, 0, 0); snapshot = null; points = [];
            await notify();
        },
        async transform(action, width, height) {
            clearSelection();
            if (action === 'resize' && (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 || width > 4096 || height > 4096))
                throw new Error('Choose dimensions between 1 and 4096 pixels.');
            remember();
            const copy = document.createElement('canvas'); copy.width = canvas.width; copy.height = canvas.height; copy.getContext('2d').drawImage(canvas, 0, 0);
            canvas.width = action === 'rotate' ? copy.height : action === 'resize' ? width : copy.width;
            canvas.height = action === 'rotate' ? copy.width : action === 'resize' ? height : copy.height;
            context.save();
            if (action === 'rotate') { context.translate(canvas.width, 0); context.rotate(Math.PI / 2); }
            if (action === 'flip') { context.translate(canvas.width, 0); context.scale(-1, 1); }
            context.drawImage(copy, 0, 0, action === 'resize' ? width : copy.width, action === 'resize' ? height : copy.height); context.restore();
            await notify();
        },
        async undo() { clearSelection(); if (history.length) { await load(history.pop()); await notify(); } },
        async reset() { clearSelection(); remember(); await load(original); await notify(); },
        dispose() { canvas.removeEventListener('pointerdown', down); canvas.removeEventListener('pointermove', move); canvas.removeEventListener('pointerup', up); canvas.removeEventListener('pointercancel', up); history = []; snapshot = original = null; }
    };
}
