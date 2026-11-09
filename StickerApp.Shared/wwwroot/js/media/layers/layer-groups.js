// Members retain canvas coordinates. Group transforms are baked into them, so
// ungrouping restores editable artwork without rasterizing text or animation.
export const cloneLayers = layers => structuredClone(layers);
export const leafLayers = layers => layers.flatMap(layer => layer.children?.length
    ? layer.children.map(child => ({ ...child, opacity: child.opacity * layer.opacity })) : [layer]);
export function groupLayers(items, id, name) {
    const children = cloneLayers(leafLayers(items));
    if(children.length>10)throw new Error('A group supports up to 10 layers.');
    const corners = children.flatMap(layer => {
        const angle = layer.rotation * Math.PI / 180, c = Math.cos(angle), s = Math.sin(angle);
        return [-1, 1].flatMap(x => [-1, 1].map(y => ({
            x: layer.x + x * layer.width / 2 * c - y * layer.height / 2 * s,
            y: layer.y + x * layer.width / 2 * s + y * layer.height / 2 * c
        })));
    });
    const left = Math.min(...corners.map(p => p.x)), right = Math.max(...corners.map(p => p.x));
    const top = Math.min(...corners.map(p => p.y)), bottom = Math.max(...corners.map(p => p.y));
    if (right - left > 2048 || bottom - top > 2048) throw new Error('Move the selected layers closer together before grouping.');
    return { id, name, children, source: '', x: (left + right) / 2, y: (top + bottom) / 2,
        width: Math.max(1, right - left), height: Math.max(1, bottom - top), rotation: 0,
        opacity: 1, flipX: false, tint: null, hue: 0, colorStrength: 1, borderWidth: 0, borderColor: '#ffffff' };
}
export function transformGroup(group, before) {
    if (['x','y','width','height','rotation','flipX'].every(key => group[key] === before[key])) return;
    const scale = group.width / before.width;
    const angle = (group.rotation - before.rotation) * Math.PI / 180;
    const c = Math.cos(angle), s = Math.sin(angle), flipped = group.flipX !== before.flipX;
    const base = before.rotation * Math.PI / 180;
    for (const child of group.children) {
        let dx = child.x - before.x, dy = child.y - before.y;
        if (flipped) {
            const x = dx * Math.cos(base) + dy * Math.sin(base);
            const y = -dx * Math.sin(base) + dy * Math.cos(base);
            dx = -x * Math.cos(base) - y * Math.sin(base);
            dy = -x * Math.sin(base) + y * Math.cos(base);
            child.rotation = 2 * before.rotation - child.rotation;
            child.flipX = !child.flipX;
        }
        child.x = group.x + (dx * c - dy * s) * scale;
        child.y = group.y + (dx * s + dy * c) * scale;
        child.width *= scale; child.height *= scale;
        child.rotation = ((child.rotation + angle * 180 / Math.PI + 180) % 360 + 360) % 360 - 180;
    }
}
