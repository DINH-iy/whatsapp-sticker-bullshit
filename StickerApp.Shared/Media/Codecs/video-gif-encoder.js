import { GIFEncoder, quantize, applyPalette } from 'gifenc';
export function quantizeFrame(pixels) {
    const palette = quantize(pixels, 256, {format:'rgb565'});
    return { indexed: applyPalette(pixels,palette,'rgb565'), palette };
}
export function createEncoder(width, height, maxBytes = 20 * 1024 * 1024) {
    const encoder = GIFEncoder();
    function checkSize() { if(encoder.bytesView().length>maxBytes) throw new Error(`This clip exceeds the ${Math.round(maxBytes / 1024 / 1024)} MB video-GIF limit. Shorten it or choose fewer frames per second.`); }
    function addFrame(frame, delay) { encoder.writeFrame(frame.indexed,width,height,{palette:frame.palette,delay,repeat:0,dispose:1}); checkSize(); }
    return {
        add(pixels, delay) { addFrame(quantizeFrame(pixels), delay); },
        addFrame,
        finish(){encoder.finish();checkSize();return encoder.bytes();}
    };
}
