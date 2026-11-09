import assert from 'node:assert/strict';
import {normalize,read,save,reset} from '../../../StickerApp.Shared/wwwroot/js/ui/editor-layout-preview.js';
function storage(){return {getItem(key){return this[key]??null;},setItem(key,value){this[key]=String(value);},removeItem(key){delete this[key];}};}
globalThis.localStorage=storage();globalThis.sessionStorage=storage();
assert.deepEqual(normalize().categories,['cutout','draw','text','adjust','layers']);
assert.deepEqual(normalize({categories:['layers','layers','invalid','text']}).categories,['layers','text','cutout','draw','adjust']);
assert.equal(normalize({controlsPosition:'invalid'}).controlsPosition,'bottom');
save({controlsPosition:'left',toolsPosition:'top',buttonSize:'compact',showTimeline:false,categories:['text']});
assert.equal(read().controlsPosition,'left');assert.equal(read().toolsPosition,'top');assert.equal(read().showTimeline,false);
assert.equal(reset().controlsPosition,'bottom');
localStorage.setItem('stik-er-in.editor-layout-preview.v1','broken');assert.equal(read().controlsPosition,'bottom');
localStorage.setItem('stickers.theme','dark');localStorage.setItem('stick-it.quick-statistics.v1','{}');localStorage.setItem('stik-er-in.warn-before-layers','false');localStorage.setItem('unrelated-service','keep');
sessionStorage.setItem('stickers.sessionCountry','test');
let deletedFont=false;globalThis.indexedDB={deleteDatabase(name){assert.equal(name,'stik-interface-font');const request={};queueMicrotask(()=>{deletedFont=true;request.onsuccess();});return request;}};
localStorage.setItem('stik-er-in.editor-layout-preview.v1',JSON.stringify({controlsPosition:'right',categories:['layers']}));
assert.equal(read().controlsPosition,'right');assert.equal(read().categories[0],'layers');reset();
const {clearPreferences}=await import('../../../StickerApp.Shared/wwwroot/js/ui/factory-reset.js');await clearPreferences();
assert.equal(localStorage.getItem('stickers.theme'),null);assert.equal(localStorage.getItem('stick-it.quick-statistics.v1'),null);assert.equal(sessionStorage.getItem('stickers.sessionCountry'),null);assert.equal(localStorage.getItem('unrelated-service'),'keep');assert.equal(deletedFont,true);
console.log('PASS layout validation, persistence, corruption fallback and scoped preference/font reset');
