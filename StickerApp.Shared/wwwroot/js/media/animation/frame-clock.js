// Frame boundaries are cached per reader. No decoding or pixel comparisons are
// needed to decide whether a canvas needs another animation redraw.
const timelines = new WeakMap();
export function framePosition(reader, seconds, start = 0, end = reader.duration / 1000) {
    let boundaries = timelines.get(reader);
    if (!boundaries) {
        let time = 0; boundaries = reader.frameDurations.map(delay => time += delay);
        timelines.set(reader, boundaries);
    }
    const first = Math.round(start * 1000), length = Math.max(1, Math.round(end * 1000) - first);
    const offset = ((seconds * 1000) % length + length) % length;
    const time = Math.min(reader.duration - .001, first + offset);
    let low = 0, high = boundaries.length - 1;
    while (low < high) { const middle = (low + high) >>> 1; if (time < boundaries[middle]) high = middle; else low = middle + 1; }
    return { index: low, time, delay: Math.max(1, Math.min(boundaries[low] - time, length - offset)) };
}
export function frameLoop(position, draw) {
    let timer, frame, running = false, lastKey;
    const stop = () => { running = false; clearTimeout(timer); cancelAnimationFrame(frame); timer = frame = undefined; };
    const tick = () => {
        frame = undefined;
        if (!running) return;
        const next = position();
        if (!next) { stop(); return; }
        if (next.key !== lastKey) { lastKey = next.key; draw(next); }
        timer = setTimeout(() => { if (running) frame = requestAnimationFrame(tick); }, Math.max(1, next.delay));
    };
    return {
        start() { stop(); running = true; lastKey = undefined; tick(); },
        stop
    };
}
