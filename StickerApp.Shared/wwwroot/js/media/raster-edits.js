// Replay the same spatial edits for every animation frame; masks come from the
// editor's reference frame, so a traced/erased outline stays fixed during playback.
const makeCanvas = (width, height) => {
    if (typeof OffscreenCanvas !== 'undefined') return new OffscreenCanvas(width, height);
    const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height; return canvas;
};
// Flood-fill the same selected background color on each frame. If a moving
// subject covers the seed, leave that frame alone rather than erasing the subject.
export function eraseConnected(context, point, tolerance, referenceColor = null) {
    const { width, height } = context.canvas;
    const x = Math.min(width - 1, Math.max(0, Math.floor(point.x))), y = Math.min(height - 1, Math.max(0, Math.floor(point.y)));
    const pixels = context.getImageData(0, 0, width, height), data = pixels.data, seed = y * width + x;
    const color = referenceColor ?? Array.from(data.slice(seed * 4, seed * 4 + 4));
    const matches = index => data[index * 4 + 3] > 0 && Math.max(Math.abs(data[index * 4] - color[0]), Math.abs(data[index * 4 + 1] - color[1]), Math.abs(data[index * 4 + 2] - color[2])) <= tolerance;
    if (!matches(seed)) return null;
    const seen = new Uint8Array(width * height), stack = new Int32Array(width * height);
    let count = 1; stack[0] = seed; seen[seed] = 1;
    const push = index => { if (!seen[index]) { seen[index] = 1; stack[count++] = index; } };
    while (count) {
        const index = stack[--count]; if (!matches(index)) continue;
        data[index * 4 + 3] = 0;
        const x = index % width, y = Math.floor(index / width);
        if (x > 0) push(index - 1); if (x < width - 1) push(index + 1);
        if (y > 0) push(index - width); if (y < height - 1) push(index + width);
    }
    context.putImageData(pixels, 0, 0); return { type: 'wand', x, y, tolerance, color };
}

export async function createRasterRenderer(recipe) {
    const masks = new Map();
    for (const edit of recipe.edits ?? []) {
        if (edit.type !== 'mask' || masks.has(edit.mask)) continue;
        const blob = await (await fetch(edit.mask)).blob();
        if (typeof createImageBitmap !== 'undefined') masks.set(edit.mask, await createImageBitmap(blob));
        else { const image = new Image(); image.src = edit.mask; await image.decode(); masks.set(edit.mask, image); }
    }
    const buffers = [makeCanvas(1, 1), makeCanvas(1, 1)];
    return {
        render(source) {
            let canvas = buffers[0], next = buffers[1];
            canvas.width = recipe.width; canvas.height = recipe.height;
            canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);
            for (const edit of recipe.edits ?? []) {
                let width = canvas.width, height = canvas.height;
                let angle = 0, scale = 1;
                if (edit.type === 'crop') { width = edit.width; height = edit.height; }
                if (edit.type === 'resize') { width = edit.width; height = edit.height; }
                if (edit.type === 'adjust') {
                    angle = edit.rotation * Math.PI / 180;
                    width = Math.abs(canvas.width * Math.cos(angle)) + Math.abs(canvas.height * Math.sin(angle));
                    height = Math.abs(canvas.width * Math.sin(angle)) + Math.abs(canvas.height * Math.cos(angle));
                    scale = Math.min(1, (edit.maxSize ?? 4096) / Math.max(width, height));
                    width = Math.max(1, Math.ceil(width * scale - 1e-8)); height = Math.max(1, Math.ceil(height * scale - 1e-8));
                }
                next.width = width; next.height = height;
                const context = next.getContext('2d');
                context.save();
                if (edit.type === 'crop') context.drawImage(canvas, edit.x, edit.y, width, height, 0, 0, width, height);
                else if (edit.type === 'resize') context.drawImage(canvas, 0, 0, width, height);
                else if (edit.type === 'adjust') {
                    context.globalAlpha = edit.opacity;
                    context.translate(width / 2, height / 2); context.rotate(angle); context.scale(scale, scale);
                    context.drawImage(canvas, -canvas.width / 2, -canvas.height / 2);
                } else {
                    if (edit.type === 'flip') { context.translate(width, 0); context.scale(-1, 1); }
                    context.drawImage(canvas, 0, 0);
                }
                context.restore();
                if (edit.type === 'wand') eraseConnected(context, edit, edit.tolerance, edit.color);
                if (edit.type === 'mask') {
                    context.globalCompositeOperation = 'destination-in'; context.drawImage(masks.get(edit.mask), 0, 0, width, height);
                    context.globalCompositeOperation = 'source-over';
                }
                if (edit.type === 'adjust' && edit.tint) {
                    context.globalCompositeOperation = 'source-in'; context.fillStyle = edit.tint; context.fillRect(0, 0, width, height);
                    context.globalCompositeOperation = 'source-over';
                }
                [canvas, next] = [next, canvas];
            }
            return canvas;
        },
        dispose() { for (const mask of masks.values()) mask.close?.(); }
    };
}
