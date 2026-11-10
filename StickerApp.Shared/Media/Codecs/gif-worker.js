import { process } from './gif-engine.js';
self.onmessage = async ({ data }) => {
    try { self.postMessage({ id: data.id, value: await process({ ...data, onProgress: progress => self.postMessage({ id: data.id, progress }) }) }); }
    catch (error) { self.postMessage({ id: data.id, error: error.message || 'Could not edit this GIF.' }); }
};
