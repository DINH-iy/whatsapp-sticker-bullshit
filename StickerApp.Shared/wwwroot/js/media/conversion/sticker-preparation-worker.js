import { configure } from './media-settings.js';
import { fit, encode, dataUrl } from './sticker-converter.js';

self.onmessage = async ({ data }) => {
    let image;
    try {
        configure(data.settings);
        const blob = await (await fetch(data.source)).blob();
        image = await createImageBitmap(blob);
        if (image.width * image.height > 32000000)
            throw new Error('This image is too large to prepare. Open Edit and replace it with a smaller original.');
        const canvas = new OffscreenCanvas(512, 512);
        fit(canvas.getContext('2d'), image, image.width, image.height);
        for (const quality of [0.9, 0.75, 0.55, 0.35, 0.15]) {
            const bytes = await encode(canvas, quality);
            if (bytes.length <= 100 * 1024) {
                self.postMessage({ value: dataUrl(bytes) });
                return;
            }
        }
        throw new Error('This image still exceeds 100 KB. Crop unnecessary detail in Edit; it will be prepared again automatically.');
    } catch (error) {
        self.postMessage({ error: error.message || 'Could not prepare this image.' });
    } finally { image?.close(); }
};
