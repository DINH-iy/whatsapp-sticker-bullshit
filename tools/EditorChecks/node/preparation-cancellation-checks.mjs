import assert from 'node:assert/strict';

globalThis.document = { hidden: false, addEventListener() {} };
globalThis.window = { addEventListener() {} };
globalThis.OffscreenCanvas = class {};
globalThis.createImageBitmap = () => {};
const live = new Set();
let terminations = 0;
globalThis.Worker = class {
    constructor() { live.add(this); }
    postMessage() {}
    terminate() { live.delete(this); terminations++; }
};
const { prepare, cancel } = await import('../../../StickerApp.Shared/wwwroot/js/media/conversion/sticker-preparation.js');
const { watchJobs } = await import('../../../StickerApp.Shared/wwwroot/js/ui/foreground.js');
let jobs = [];
const subscription = watchJobs({ invokeMethodAsync: async (_, value) => { jobs = value; } });
for (const animated of [false, true]) {
    const pending = prepare(animated ? 'data:image/gif;base64,fixture' : 'data:image/png;base64,fixture', animated);
    const rejected = assert.rejects(pending, /cancelled/i);
    assert.equal(live.size, 1);
    cancel();
    assert.equal(live.size, 0, 'Cancel must terminate the worker immediately, before awaiting its result');
    await rejected;
    assert.equal(jobs.length, 0);
}
const pending = prepare('data:image/gif;base64,fixture', true);
const rejected = assert.rejects(pending, /cancelled/i);
window.stickerSetForeground(false);
assert.equal(live.size, 0, 'Leaving the app must terminate preparation workers');
await rejected;
assert.equal(jobs.length, 0);
window.stickerSetForeground(true);
const completed = prepare('data:image/png;base64,fixture', false);
[...live][0].onmessage({ data: { value: 'finished' } });
assert.equal(await completed, 'finished');
assert.equal(live.size, 0, 'Successful preparation must also terminate its worker');
assert.equal(terminations >= 4, true);
assert.equal(jobs.length, 0);

// Old WebViews use the main-thread fallback. Aborting during image decoding
// must clear the source and prevent all canvas/encoder work from starting.
delete globalThis.OffscreenCanvas;
let decoding;
globalThis.Image = class {
    set src(value) { if (value === '') decoding?.reject(new Error('Image decode stopped')); }
    decode() { return new Promise((resolve, reject) => { decoding = { resolve, reject }; }); }
};
const fallback = prepare('data:image/png;base64,fixture', false);
const fallbackRejected = assert.rejects(fallback, /decode stopped/);
cancel();
await fallbackRejected;
assert.equal(jobs.length, 0);
assert.equal(live.size, 0);
subscription.dispose();
console.log('PASS cancel/background/success terminate preparation workers; fallback cancellation stops decoding before encoding');
