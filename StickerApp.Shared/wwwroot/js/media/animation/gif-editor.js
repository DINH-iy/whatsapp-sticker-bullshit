import { read as mediaSettings } from '../conversion/media-settings.js';
import { conversionKey, cachedConversion, cacheConversion } from '../conversion/result-cache.js';
import { beginTiming } from '../../ui/performance.js';
import { requireForeground, onForegroundChanged, startJob } from '../../ui/foreground.js';
import { leafLayers } from '../layers/layer-groups.js';
// Each visible editor owns a session, so cancelling it cannot stop preparation
// or another editor. The module-level session serves background conversion.
export function createSession() {
    let worker, sequence = 0;
    const pending = new Map();
    function reset(message) {
        worker?.terminate(); worker = undefined;
        for (const entry of pending.values()) { clearTimeout(entry.timer); entry.finish(); entry.finishTiming(); entry.controller.abort(); entry.reject(new Error(message)); }
        pending.clear();
    }
    const unwatch = onForegroundChanged(active => { if (!active) reset('Processing stopped because the app is in the background. Try again when you return.'); });
    function settle(id, value, error) {
        const entry = pending.get(id);
        if (!entry) return;
        clearTimeout(entry.timer); entry.finish(); entry.finishTiming(); pending.delete(id);
        if (error) entry.reject(new Error(error)); else { cacheConversion(entry.cacheKey, value); entry.resolve(value); }
    }
    function request(action, source, start, end, composition, reference) {
        requireForeground();
        return new Promise((resolve, reject) => {
            const id = ++sequence;
            const controller = new AbortController();
            const timer = setTimeout(() => reset('Animation processing timed out. Use a smaller animation.'), ['compose', 'edit', 'stage'].includes(action) ? 180000 : 60000);
            pending.set(id, { resolve, reject, timer, controller, finishTiming: beginTiming('Animation worker: ' + action), finish: startJob(action === 'inspect' ? 'Reading animation…' : 'Processing animation…') });
            const progress = value => {
                const entry=pending.get(id); if(!entry)return;
                if(entry.lastProgress === `${value.stage}:${value.attempt}:${value.percent}`)return;
                entry.lastProgress=`${value.stage}:${value.attempt}:${value.percent}`;
                entry.finish.progress(value);
                reference?.invokeMethodAsync('AnimationProgress',value).catch(()=>{});
            };
            pending.get(id).progress=progress;
            const data = { id, action, source, start, end, composition, settings: mediaSettings() };
            const dispatch = async () => {
                if (['prepare', 'edit', 'compose'].includes(action)) {
                    const key = await conversionKey(source, { action, start, end, composition, settings: data.settings });
                    if (!pending.has(id)) return;
                    pending.get(id).cacheKey = key;
                    const cached = cachedConversion(key);
                    if (cached) { settle(id, cached); return; }
                }
                if (!pending.has(id)) return;
                if (typeof OffscreenCanvas === 'undefined') {
                    import('../codecs/gif-engine.js').then(engine => pending.has(id) ? engine.process({ ...data, signal: controller.signal, onProgress: progress }) : undefined)
                        .then(value => settle(id, value), error => settle(id, null, error.message));
                    return;
                }
                if (!worker) {
                    worker = new Worker(new URL('../codecs/gif-worker.js', import.meta.url), { type: 'module' });
                    worker.onmessage = ({ data }) => data.progress ? pending.get(data.id)?.progress(data.progress) : settle(data.id, data.value, data.error);
                    worker.onerror = () => reset('Could not process this animation.');
                }
                worker.postMessage(data);
            };
            dispatch().catch(error => settle(id, null, error.message));
        });
    }
    return {
        editLayer(source, start, end, recipe) { return request('edit', source, start, end, { ...recipe, layer: true }); },
        edit(source, start, end, recipe, reference) { return request('edit', source, start, end, recipe, reference); },
        stage(source, start, end, recipe) { return request('stage', source, start, end, recipe); },
        inspect(source) { return request('inspect', source); },
        trim(source, start, end) { return request('trim', source, start, end); },
        prepare(source) { return request('prepare', source); },
        compose(composition, reference) { return request('compose', null, null, null, {...composition,layers:leafLayers(composition.layers)}, reference); },
        dispose() { reset('Animation editor closed.'); },
        release() { unwatch(); reset('Animation editor closed.'); }
    };
}

const shared = createSession();
export const inspect = source => shared.inspect(source);
export const trim = (source, start, end) => shared.trim(source, start, end);
export const prepare = source => shared.prepare(source);
export const compose = (composition, reference) => shared.compose(composition, reference);
export const dispose = () => shared.dispose();
