// Animated WebP is a RIFF container of independently decodable WebP frame chunks.
// Reading those chunks also works on WebViews without WebCodecs ImageDecoder.
const canvasOf = (width, height) => {
    if (typeof OffscreenCanvas !== 'undefined') return new OffscreenCanvas(width, height);
    const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height; return canvas;
};
export async function openAnimation(source) {
    if (source.startsWith('data:image/gif;base64,')) {
        const engine = await import('../codecs/gif-engine.js');
        return engine.createReader(source);
    }
    if (!source.startsWith('data:image/webp;base64,')) throw new Error('Choose a GIF or animated WebP.');
    const binary = atob(source.slice(source.indexOf(',') + 1));
    if (binary.length > 10 * 1024 * 1024) throw new Error('Maximum animation size: 10 MB.');
    const bytes = Uint8Array.from(binary, c => c.charCodeAt(0)), view = new DataView(bytes.buffer);
    const text = offset => String.fromCharCode(...bytes.subarray(offset, offset + 4));
    const uint24 = offset => bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16;
    if (text(0) !== 'RIFF' || text(8) !== 'WEBP') throw new Error('Invalid WebP.');
    let width = 0, height = 0, duration = 0, background = 'rgba(0,0,0,0)';
    const frames = [];
    for (let offset = 12; offset + 8 <= bytes.length;) {
        const kind = text(offset), length = view.getUint32(offset + 4, true), data = offset + 8;
        if (data + length > bytes.length) throw new Error('Incomplete WebP frame.');
        if (kind === 'VP8X' && length >= 10) { width = uint24(data + 4) + 1; height = uint24(data + 7) + 1; }
        if (kind === 'ANIM' && length >= 6) background = `rgba(${bytes[data + 2]},${bytes[data + 1]},${bytes[data]},${bytes[data + 3] / 255})`;
        if (kind === 'ANMF' && length >= 16) {
            const frame = { x: uint24(data) * 2, y: uint24(data + 3) * 2, width: uint24(data + 6) + 1,
                height: uint24(data + 9) + 1, duration: Math.max(8, uint24(data + 12)), flags: bytes[data + 15], chunks: bytes.slice(data + 16, data + length) };
            frames.push(frame); duration += frame.duration;
        }
        offset = data + length + (length & 1);
    }
    if (!width || !height || !frames.length || width * height > 4_000_000 || frames.length > 1000 || width * height * frames.length > 80_000_000)
        throw new Error('This animation is invalid or too large to edit.');
    const decoded = [];
    try {
        for (const frame of frames) {
            if (frame.x + frame.width > width || frame.y + frame.height > height) throw new Error('Invalid WebP frame bounds.');
            // VP8X is needed for frame payloads containing a separate ALPH chunk.
            const alpha = String.fromCharCode(...frame.chunks.subarray(0, 4)) === 'ALPH';
            const headerSize = alpha ? 30 : 12;
            const still = new Uint8Array(headerSize + frame.chunks.length), header = new DataView(still.buffer);
            still.set([82,73,70,70], 0); header.setUint32(4, still.length - 8, true); still.set([87,69,66,80], 8);
            if (alpha) {
                still.set([86,80,56,88,10,0,0,0,16,0,0,0], 12);
                for (let i = 0; i < 3; i++) { still[24 + i] = (frame.width - 1) >> (8 * i); still[27 + i] = (frame.height - 1) >> (8 * i); }
            }
            still.set(frame.chunks, headerSize);
            const blob = new Blob([still], { type: 'image/webp' });
            let image;
            if (typeof createImageBitmap !== 'undefined') image = await createImageBitmap(blob);
            else { image = new Image(); const url = URL.createObjectURL(blob); try { image.src = url; await image.decode(); } finally { URL.revokeObjectURL(url); } }
            decoded.push({ ...frame, image });
        }
    } catch (error) { for (const frame of decoded) frame.image.close?.(); throw error; }
    const canvas = canvasOf(width, height), context = canvas.getContext('2d');
    let index = 0, end = 0, previous, lastTime = -1;
    const clear = frame => { context.clearRect(frame.x, frame.y, frame.width, frame.height); context.fillStyle = background; context.fillRect(frame.x, frame.y, frame.width, frame.height); };
    return { width, height, duration, frameCount: frames.length, frameDurations: frames.map(frame => frame.duration),
        at(time) {
            time = Math.max(0, Math.min(duration - 1, time));
            if (time < lastTime) { index = 0; end = 0; previous = null; }
            if (index === 0) clear({ x: 0, y: 0, width, height });
            lastTime = time;
            while (index < decoded.length && time >= end) {
                if (previous?.flags & 1) clear(previous);
                const frame = decoded[index++];
                if (frame.flags & 2) context.clearRect(frame.x, frame.y, frame.width, frame.height);
                context.drawImage(frame.image, frame.x, frame.y); end += frame.duration; previous = frame;
            }
            return canvas;
        },
        dispose() { for (const frame of decoded) frame.image.close?.(); }
    };
}
