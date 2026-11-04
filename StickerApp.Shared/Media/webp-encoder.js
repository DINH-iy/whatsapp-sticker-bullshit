import createEncoder from '@jsquash/webp/codec/enc/webp_enc.js';
import { defaultOptions } from '@jsquash/webp/meta.js';

// Ship the encoder with the app so Safari/iPhone conversion also works offline.
let encoder;
export async function encode(pixels, quality) {
    encoder ??= createEncoder({
        noInitialRun: true,
        locateFile: () => new URL('./webp_enc.wasm', import.meta.url).href
    });
    const module = await encoder;
    const bytes = module.encode(pixels.data, pixels.width, pixels.height, {
        ...defaultOptions, quality: Math.round(quality * 100), method: 3
    });
    if (!bytes) throw new Error('Could not encode this sticker. Try a smaller image.');
    return new Uint8Array(bytes);
}
