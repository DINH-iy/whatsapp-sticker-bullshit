import { leafLayers } from '../layers/layer-groups.js';
// Each visible editor owns a session, so cancelling it cannot stop preparation
// or another editor. The module-level session serves background conversion.
export function createSession() {
    let worker, sequence = 0;
    const pending = new Map();
    function reset(message) {
        worker?.terminate(); worker = undefined;
        for (const entry of pending.values()) { clearTimeout(entry.timer); entry.reject(new Error(message)); }
        pending.clear();
    }
    function settle(id, value, error) {
        const entry = pending.get(id);
        if (!entry) return;
        clearTimeout(entry.timer); pending.delete(id);
        if (error) entry.reject(new Error(error)); else entry.resolve(value);
    }
    function request(action, source, start, end, composition) {
        return new Promise((resolve, reject) => {
            const id = ++sequence;
            const timer = setTimeout(() => reset('Animation processing timed out. Use a smaller animation.'), ['compose', 'edit'].includes(action) ? 180000 : 60000);
            pending.set(id, { resolve, reject, timer });
            const data = { id, action, source, start, end, composition };
            if (typeof OffscreenCanvas === 'undefined') {
                import('../codecs/gif-engine.js').then(engine => pending.has(id) ? engine.process(data) : undefined)
                    .then(value => settle(id, value), error => settle(id, null, error.message));
                return;
            }
            if (!worker) {
                worker = new Worker(new URL('../codecs/gif-worker.js', import.meta.url), { type: 'module' });
                worker.onmessage = ({ data }) => settle(data.id, data.value, data.error);
                worker.onerror = () => reset('Could not process this animation.');
            }
            worker.postMessage(data);
        });
    }
    return {
        editLayer(source, start, end, recipe) { return request('edit', source, start, end, { ...recipe, layer: true }); },
        edit(source, start, end, recipe) { return request('edit', source, start, end, recipe); },
        inspect(source) { return request('inspect', source); },
        trim(source, start, end) { return request('trim', source, start, end); },
        prepare(source) { return request('prepare', source); },
        compose(composition) { return request('compose', null, null, null, {...composition,layers:leafLayers(composition.layers)}); },
        dispose() { reset('Animation editor closed.'); }
    };
}

const shared = createSession();
export const inspect = source => shared.inspect(source);
export const trim = (source, start, end) => shared.trim(source, start, end);
export const prepare = source => shared.prepare(source);
export const compose = composition => shared.compose(composition);
export const dispose = () => shared.dispose();
