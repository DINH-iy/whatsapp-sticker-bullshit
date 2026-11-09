import { timed, beginTiming } from '../../ui/performance.js';
import { openAnimation } from '../animation/animation-reader.js';
export async function openGif(source) {
    const reader = await timed('Layer animation: decode', () => openAnimation(source));
    const finish = beginTiming('Layer animation: timeline thumbnails', `frames=${reader.frameCount}; pixels=${reader.width}x${reader.height}`);
    try {
        const thumbnails = [];
        for (let index = 0; index < 6; index++) {
            const frame = reader.at(Math.floor((reader.duration - 1) * index / 5));
            const canvas = document.createElement('canvas'); canvas.width = canvas.height = 96;
            const scale = Math.min(96 / frame.width, 96 / frame.height);
            canvas.getContext('2d').drawImage(frame, (96 - frame.width * scale) / 2, (96 - frame.height * scale) / 2, frame.width * scale, frame.height * scale);
            thumbnails.push(canvas.toDataURL('image/png'));
            await new Promise(resolve => setTimeout(resolve, 0));
        }
        return { reader, thumbnails };
    } catch(error) { reader.dispose?.(); throw error; } finally { finish(); }
}
