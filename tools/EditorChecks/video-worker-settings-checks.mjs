import assert from 'node:assert/strict';
import { normalizeWorkers, effectiveWorkers, readWorkers, setWorkers } from '../../StickerApp.Shared/wwwroot/js/media/video/video-worker-settings.js';
const saved=new Map();globalThis.localStorage={getItem:key=>saved.get(key)??null,setItem:(key,value)=>saved.set(key,value)};
assert.equal(readWorkers(),2);
for(const count of [1,2,4]){assert.equal(setWorkers(count),count);assert.equal(readWorkers(),count);}
assert.equal(normalizeWorkers(8),2);assert.equal(normalizeWorkers('bad'),2);
assert.equal(effectiveWorkers(4,2),2);assert.equal(effectiveWorkers(4,8),4);assert.equal(effectiveWorkers(1,8),1);
console.log('PASS worker preference persistence, invalid-setting fallback and hardware concurrency caps');
