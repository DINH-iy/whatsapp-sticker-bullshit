// Exact comparison: never discard visually different frames or their timing.
export function samePixels(left, right) {
    if (!left || !right || left.byteLength !== right.byteLength) return false;
    const a = new Uint32Array(left.buffer, left.byteOffset, left.byteLength / 4);
    const b = new Uint32Array(right.buffer, right.byteOffset, right.byteLength / 4);
    for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
    return true;
}
export function ensureAnimationFrames(frames) {
    if (frames.length !== 1) return frames;
    const frame = frames[0], first = Math.floor(frame.duration / 2);
    return [{ bytes: frame.bytes, duration: first }, { bytes: frame.bytes, duration: frame.duration - first }];
}
