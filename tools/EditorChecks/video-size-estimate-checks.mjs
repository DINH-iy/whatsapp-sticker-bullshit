import assert from 'node:assert/strict';
import { estimateSizes } from '../../StickerApp.Shared/wwwroot/js/media/video/video-size-estimate.js';
const small = estimateSizes(Array(6).fill({low:100, high:300}),30,1);
assert.equal(small.tooLarge,false);assert.ok(small.stickerHigh<500*1024);
// Direct encoding estimates only the final sticker.
const largeGif = estimateSizes(Array(6).fill({low:100,high:500}),100,3);
assert.ok(!("gifLow" in largeGif));assert.equal(largeGif.tooLarge,false);
const noisy = estimateSizes(Array(6).fill({low:12000,high:30000}),100,3);
assert.equal(noisy.tooLarge,true);assert.ok(noisy.suggestedSeconds<3);
const shorter = estimateSizes(Array(6).fill({low:12000,high:30000}),20,1);
assert.ok(shorter.stickerLow<noisy.stickerLow);
assert.throws(()=>estimateSizes([],100,3));
assert.ok(small.quality>=.1&&small.quality<=.8);
assert.ok(noisy.quality<=small.quality);
console.log('PASS direct WebP estimates, quality selection, noisy-clip warning, shorter-clip reduction and invalid samples');
