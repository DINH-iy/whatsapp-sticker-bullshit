import { GIFEncoder, quantize, applyPalette } from 'gifenc';
export function createEncoder(width, height) {
    const encoder = GIFEncoder();
    return {
        add(pixels, delay) {
            const palette = quantize(pixels, 256, {format:'rgb565'});
            encoder.writeFrame(applyPalette(pixels,palette,'rgb565'),width,height,{palette,delay,repeat:0,dispose:1});
            if(encoder.bytesView().length>20*1024*1024) throw new Error('This GIF exceeds 20 MiB. Choose a shorter clip or fewer frames per second.');
        },
        finish(){encoder.finish();return encoder.bytes();}
    };
}
