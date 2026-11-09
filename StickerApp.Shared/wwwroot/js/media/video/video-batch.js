import { create as createVideo } from './video-import.js';
import { effectiveWorkers } from './video-worker-settings.js';
import { requireForeground, onForegroundChanged, startJob } from '../../ui/foreground.js';

// Reviews retain source Blobs, not decoded videos or frame buffers. Conversion
// has at most two decoders and shares the user's total worker allowance.
export function create(reference) {
    const entries=new Map(), results=new Map(), cleanups=new Set();
    let disposed=false, running=false, abort;
    const notify=(...args)=>disposed?Promise.resolve():reference.invokeMethodAsync('BatchProgress',...args).catch(()=>{});
    function cancel(){abort?.abort();for(const cleanup of [...cleanups])cleanup();}
    const unwatch=onForegroundChanged(active=>{if(!active)cancel();});
    async function video(entry, workers, signal) {
        const root=document.createElement('div'), video=document.createElement('video'), input=document.createElement('input');
        root.hidden=true;root.append(video,input);document.body.append(root);video.muted=true;video.playsInline=true;
        const controller=createVideo(video,input,root,{invokeMethodAsync:async()=>{}},{...entry.plan,workers,skipThumbnails:true});
        let cleaned=false;
        const cleanup=()=>{if(cleaned)return;cleaned=true;controller.dispose();root.remove();cleanups.delete(cleanup);};
        cleanups.add(cleanup);
        try {
            signal.throwIfAborted();await controller.loadFile(entry.file);signal.throwIfAborted();
            return await controller.convert(entry.plan.start,entry.plan.end,entry.plan.fps);
        } finally {cleanup();}
    }
    async function gif(entry, signal) {
        const finish=startJob('Preparing imported GIF…');
        try {
            // FileReader avoids spreading large binary buffers on the JS stack.
            const data=await new Promise((resolve,reject)=>{
                const reader=new FileReader();
                const stop=()=>reader.abort();signal.addEventListener('abort',stop,{once:true});
                reader.onload=()=>resolve(reader.result);reader.onerror=()=>reject(new Error('Could not read this GIF.'));
                reader.onabort=()=>reject(new DOMException('Cancelled','AbortError'));
                reader.onloadend=()=>signal.removeEventListener('abort',stop);
                if(signal.aborted){reject(new DOMException('Cancelled','AbortError'));return;}
                reader.readAsDataURL(entry.file);
            });
            signal.throwIfAborted();
            let output;
            if(typeof OffscreenCanvas==='undefined'||typeof Worker==='undefined') {
                const engine=await import('../codecs/gif-engine.js');signal.throwIfAborted();
                output=await engine.process({action:'prepare',source:data,signal});
            } else {
                output=await new Promise((resolve,reject)=>{
                    const worker=new Worker(new URL('../codecs/gif-worker.js',import.meta.url),{type:'module'});
                    let timer;
                    const cleanup=()=>{worker.terminate();clearTimeout(timer);signal.removeEventListener('abort',stop);cleanups.delete(stop);};
                    const stop=()=>{cleanup();reject(new DOMException('Cancelled','AbortError'));};
                    cleanups.add(stop);signal.addEventListener('abort',stop,{once:true});
                    timer=setTimeout(()=>{cleanup();reject(new Error('GIF conversion timed out. Choose a shorter animation.'));},90000);
                    worker.onmessage=({data})=>{cleanup();data.error?reject(new Error(data.error)):resolve(data.value);};
                    worker.onerror=()=>{cleanup();reject(new Error('GIF conversion failed.'));};
                    if(signal.aborted){stop();return;}
                    worker.postMessage({id:1,action:'prepare',source:data});
                });
            }
            signal.throwIfAborted();
            return await (await fetch(output,{signal})).blob();
        } finally {finish();}
    }
    return {
        addVideo(id,file,plan){if(disposed||running)throw new Error('This import is no longer accepting clips.');entries.set(id,{file,plan,video:true});},
        async addGif(id,stream,name){
            const bytes=await stream.arrayBuffer();
            if(disposed)throw new DOMException('Cancelled','AbortError');
            if(bytes.byteLength>20*1024*1024)throw new Error('This GIF exceeds 20 MB.');
            entries.set(id,{file:new File([bytes],name,{type:'image/gif'}),video:false});
        },
        async convert(){
            requireForeground();if(disposed||running)throw new Error('This batch is not ready.');
            running=true;abort=new AbortController();const signal=abort.signal;results.clear();
            const queue=[...entries],budget=effectiveWorkers(),lanes=Math.min(2,budget,queue.length),workers=Math.max(1,Math.floor(budget/Math.max(1,lanes)));
            const finish=startJob('Converting imported animations…');let cursor=0,completed=0;
            const work=async()=>{
                while(cursor<queue.length){
                    signal.throwIfAborted();const [id,entry]=queue[cursor++];
                    try {results.set(id,await(entry.video?video(entry,workers,signal):gif(entry,signal)));}
                    catch(error){if(signal.aborted)throw error;throw new Error(`${entry.file.name}: ${error.message}`);}
                    signal.throwIfAborted();await notify(++completed,queue.length);
                }
            };
            const tasks=Array.from({length:lanes},work);
            try {await Promise.all(tasks);signal.throwIfAborted();return [...results.keys()];}
            catch(error){cancel();await Promise.allSettled(tasks);results.clear();throw error;}
            finally {running=false;finish();}
        },
        result(id){const result=results.get(id);if(!result)throw new Error('This animation has not finished converting.');return result;},
        cancel,
        dispose(){disposed=true;cancel();unwatch();entries.clear();results.clear();}
    };
}
