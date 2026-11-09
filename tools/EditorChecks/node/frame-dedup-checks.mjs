import assert from 'node:assert/strict';
import { samePixels, ensureAnimationFrames } from '../../../StickerApp.Shared/wwwroot/js/media/shared/frame-dedup.js';
assert.equal(samePixels(undefined,new Uint8ClampedArray(4)),false);
assert.equal(samePixels(new Uint8ClampedArray([1,2,3,4]),new Uint8ClampedArray([1,2,3,4])),true);
assert.equal(samePixels(new Uint8ClampedArray([1,2,3,4]),new Uint8ClampedArray([1,2,3,5])),false);
const bytes=new Uint8Array([1,2]),result=ensureAnimationFrames([{bytes,duration:101}]);
assert.equal(result.length,2);assert.equal(result[0].duration+result[1].duration,101);assert.equal(result[0].bytes,bytes);
console.log('PASS exact RGBA duplicate detection and duration-preserving minimum animation frames');
