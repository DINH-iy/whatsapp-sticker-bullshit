let worker;
let sequence = 0;
const pending = new Map();

function reset(message) {
    worker?.terminate();
    worker = undefined;
    for (const request of pending.values()) { clearTimeout(request.timer); request.reject(new Error(message)); }
    pending.clear();
}
function request(action, source, start, end) {
    if (!worker) {
        worker = new Worker(new URL('./gif-worker.js', import.meta.url), { type: 'module' });
        worker.onmessage = ({ data }) => {
            const entry = pending.get(data.id);
            if (!entry) return;
            clearTimeout(entry.timer); pending.delete(data.id);
            if (data.error) entry.reject(new Error(data.error)); else entry.resolve(data.value);
        };
        worker.onerror = () => reset('Could not process this GIF.');
    }
    return new Promise((resolve, reject) => {
        const id = ++sequence;
        const timer = setTimeout(() => reset('GIF processing timed out. Use a smaller GIF.'), 60000);
        pending.set(id, { resolve, reject, timer });
        worker.postMessage({ id, action, source, start, end });
    });
}
export function inspect(source) { return request('inspect', source); }
export function trim(source, start, end) { return request('trim', source, start, end); }
export function dispose() { reset('GIF editor closed.'); }
