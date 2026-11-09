import { encode } from '../codecs/webp-encoder.js';
self.onmessage = async ({ data }) => {
    try {
        const bytes = await encode({ data: data.pixels, width: data.width, height: data.height }, data.quality);
        self.postMessage({ id: data.id, bytes }, [bytes.buffer]);
    } catch (error) { self.postMessage({ id: data.id, error: error.message || 'Could not compress this video frame.' }); }
};
