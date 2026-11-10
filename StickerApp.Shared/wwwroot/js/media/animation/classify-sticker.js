import { openAnimation } from './animation-reader.js';
import { samePixels } from '../shared/frame-dedup.js';
import { encode, dataUrl } from '../conversion/sticker-converter.js';

// Compare every composited frame, including partial updates, disposal and blending.
export async function stillFrame(source) {
    const reader = await openAnimation(source);
    try {
        let first, canvas, time = 0;
        for (const duration of reader.frameDurations) {
            canvas = reader.at(time);
            const pixels = canvas.getContext('2d').getImageData(0, 0, reader.width, reader.height).data;
            if (first && !samePixels(first, pixels)) return null;
            first ??= pixels.slice();
            time += duration;
        }
        return first ? dataUrl(await encode(canvas, 1)) : null;
    } finally { reader.dispose?.(); }
}
