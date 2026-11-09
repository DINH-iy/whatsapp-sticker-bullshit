export function rotationStep(root, shift = false) {
    if (root.querySelector('[data-rotation-snap]')?.checked) {
        const step = Number(root.querySelector('[data-rotation-snap-step]')?.value);
        if ([5, 15, 90].includes(step)) return step;
    }
    return shift ? 15 : 0;
}
export function snapRotation(root, angle, shift = false) {
    const step = rotationStep(root, shift);
    if (step) angle = Math.round(angle / step) * step;
    return ((angle + 180) % 360 + 360) % 360 - 180;
}
