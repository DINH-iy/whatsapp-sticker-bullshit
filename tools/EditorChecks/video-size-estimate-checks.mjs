import assert from 'node:assert/strict';
import { estimateSizes } from '../../StickerApp.Shared/wwwroot/js/media/video/video-size-estimate.js';
const small = estimateSizes(Array(6).fill({low:100, high:300}),30,12000,1);
assert.equal(small.tooLarge,false);assert.ok(small.stickerHigh<500*1024);
// Raw GIF size is NOT the final sticker size limit.
const largeGif = estimateSizes(Array(6).fill({low:100,high:500}),300,240000,10);
assert.ok(largeGif.gifHigh>500*1024);assert.equal(largeGif.tooLarge,false);
const noisy = estimateSizes(Array(6).fill({low:12000,high:30000}),300,240000,10);
assert.equal(noisy.tooLarge,true);assert.ok(noisy.suggestedSeconds<10);
const shorter = estimateSizes(Array(6).fill({low:12000,high:30000}),20,240000,1);
assert.ok(shorter.stickerLow<noisy.stickerLow);
assert.throws(()=>estimateSizes([],300,100,10));
console.log('PASS sampled GIF/sticker estimates, distinct final limit, noisy-clip warning, shorter-clip reduction and invalid samples');
