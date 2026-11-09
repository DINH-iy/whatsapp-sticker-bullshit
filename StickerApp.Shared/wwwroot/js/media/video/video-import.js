import { samePixels, ensureAnimationFrames } from '../shared/frame-dedup.js';
import { createFramePool } from './video-frame-pool.js';
import { effectiveWorkers } from './video-worker-settings.js';
import { MAX_VIDEO_BYTES, MAX_CLIP_SECONDS, MAX_STICKER_BYTES } from './video-limits.js';
import { estimateSizes } from './video-size-estimate.js';
import { beginTiming } from '../../ui/performance.js';
import { readVideoTiming, planFrames } from './video-timing.js';
import { onForegroundChanged, requireForeground, startJob } from '../../ui/foreground.js';
let pickedVideo;
export function createPicker(input, reference) {
    if (!input?.isConnected) return { dispose() {} };
    const choose = () => { if (!input.files?.[0]) return; pickedVideo = input.files[0]; input.value = ''; reference.invokeMethodAsync('Picked').catch(() => { pickedVideo = null; }); };
    input.addEventListener('change', choose);
    return { dispose() { input.removeEventListener('change', choose); } };
}
import { previewControls } from '../animation/animation-preview.js';
function waitFor(media,event,signal) {
    return new Promise((resolve,reject)=>{
        const clean=()=>{clearTimeout(timer);media.removeEventListener(event,done);media.removeEventListener('error',failed);signal?.removeEventListener('abort',abort);};
        const done=()=>{clean();resolve();}, failed=()=>{clean();reject(new Error('This video cannot be decoded on this device. Try an MP4 (H.264) video.'));}, abort=()=>{clean();reject(new DOMException('Cancelled','AbortError'));};
        const timer=setTimeout(()=>{clean();reject(new Error('The video took too long to load or seek. Try another video.'));},20000);
        media.addEventListener(event,done,{once:true});media.addEventListener('error',failed,{once:true});signal?.addEventListener('abort',abort,{once:true});
        if(signal?.aborted)abort();
    });
}
async function seekFrame(video,time,signal) {
    signal?.throwIfAborted();
    time=Math.max(0,Math.min(time,video.duration-0.001));
    if(Math.abs(video.currentTime-time)<0.0001 && video.readyState>=2)return;
    const ready=waitFor(video,'seeked',signal); video.currentTime=time; await ready;
    if(video.readyState<2)await waitFor(video,'loadeddata',signal);
}
export function create(video,input,root,dotnet,options={}) {
    let fileSource;
    let timing, outputWidth=0, outputHeight=0, estimateAbort, estimateWork=Promise.resolve(), cachedEstimate;
    let url,disposed=false,fileAbort,conversionAbort,converting=false,version=0,rangeEnd=0,nextSeek=null,seekWork=Promise.resolve();
    const unwatchForeground=onForegroundChanged(active=>{if(!active){video.pause();fileAbort?.abort();conversionAbort?.abort();estimateAbort?.abort();}else if(url&&fileAbort?.signal.aborted){fileAbort=new AbortController();}});
    const estimateKey=(start,end,fps)=>`${version}:${start}:${end}:${fps}`;
    async function estimate(start,end,fps) {
        requireForeground();
        if(converting||!url||!outputWidth)throw new Error('Wait for the video to finish loading before estimating.');
        const key=estimateKey(start,end,fps);
        if(cachedEstimate?.key===key)return cachedEstimate.value;
        estimateAbort?.abort();
        const abort=estimateAbort=new AbortController(), signal=abort.signal;
        const previous=estimateWork;
        const work=(async()=>{
            await previous.catch(()=>{}); signal.throwIfAborted();
            const frames=planFrames(start,end,fps,timing,outputWidth,outputHeight);
            const finishJob=startJob('Estimating sticker size…');
            const finishTiming=beginTiming('Video: estimate compressed size', `frames=${frames.length}; pixels=${outputWidth}x${outputHeight}`);
            let estimatePool;
            try {
                video.pause();nextSeek=null;await seekWork;signal.throwIfAborted();
                const {fit,encode}=await import('../conversion/sticker-converter.js');
                signal.throwIfAborted();
                estimatePool=createFramePool(Math.min(effectiveWorkers(),2),signal);
                const canvas=document.createElement('canvas'),sticker=document.createElement('canvas');
                canvas.width=outputWidth;canvas.height=outputHeight;sticker.width=sticker.height=512;
                const context=canvas.getContext('2d',{willReadFrequently:true}),stickerContext=sticker.getContext('2d',{willReadFrequently:true});
                const samples=[];
                const count=Math.min(6,frames.length);
                for(let i=0;i<count;i++) {
                    signal.throwIfAborted();
                    const frame=frames[Math.round(i*(frames.length-1)/Math.max(1,count-1))];
                    await seekFrame(video,frame.time,signal);
                    context.drawImage(video,0,0,outputWidth,outputHeight);
                    fit(stickerContext,canvas,outputWidth,outputHeight);
                    let low,high;
                    if(estimatePool){
                        const pixels=stickerContext.getImageData(0,0,512,512).data;
                        [low,high]=await Promise.all([estimatePool.encode(pixels.slice(),512,512,.1),estimatePool.encode(pixels,512,512,.8)]);
                    }else{
                        low=await encode(sticker,.1);signal.throwIfAborted();high=await encode(sticker,.8);
                    }
                    signal.throwIfAborted();samples.push({low:low.length,high:high.length});
                    await new Promise(resolve=>setTimeout(resolve,0));
                }
                const value=estimateSizes(samples,frames.length,end-start);
                cachedEstimate={key,value};return value;
            } finally { estimatePool?.dispose();finishTiming();finishJob(); }
        })();
        estimateWork=work;
        return work;
    }
    const notify=(method,...args)=>disposed?Promise.resolve():dotnet.invokeMethodAsync(method,...args);
    const fail=error=>{if(error.name!=='AbortError')return notify('VideoFailed',error.message);};
    function queueSeek(time){
        nextSeek=time;video.pause();
        seekWork=seekWork.catch(()=>{}).then(async()=>{
            while(nextSeek!==null&&!converting&&!disposed){const time=nextSeek;nextSeek=null;await seekFrame(video,time,fileAbort?.signal);controls.update(video.currentTime);}
        });return seekWork;
    }
    const controls=previewControls(root,{seek:queueSeek,speed:rate=>video.playbackRate=rate,error:fail});
    const update=()=>{controls.update(video.currentTime);if(!converting&&rangeEnd>0&&video.currentTime>=rangeEnd)video.pause();};
    const playback=()=>notify('VideoPlayback',!video.paused).catch(()=>{});
    video.addEventListener('timeupdate',update);video.addEventListener('play',playback);video.addEventListener('pause',playback);
    async function loadFile(file){
        const current=++version;estimateAbort?.abort();cachedEstimate=null;fileAbort?.abort();conversionAbort?.abort();fileAbort=new AbortController();
        video.pause();if(url)URL.revokeObjectURL(url);url=null;
        await notify('VideoLoading');
        try{
            if(!file||file.size===0||file.size>MAX_VIDEO_BYTES)throw new Error('Choose a video between 1 byte and 20 MB.');
            const ready=waitFor(video,'loadeddata',fileAbort.signal);
            url=URL.createObjectURL(file);video.src=url;video.load();await ready;
            if(!Number.isFinite(video.duration)||video.duration<0.1||!video.videoWidth||!video.videoHeight)throw new Error('Choose a video with a readable duration of at least 0.1 seconds.');
            if(current!==version||disposed)return;
            fileSource=file;
            timing=options.timing??await readVideoTiming(file).catch(()=>null);
            const outputScale=Math.min(1,512/Math.max(video.videoWidth,video.videoHeight));
            outputWidth=Math.max(1,Math.round(video.videoWidth*outputScale)); outputHeight=Math.max(1,Math.round(video.videoHeight*outputScale));
            rangeEnd=Math.min(MAX_CLIP_SECONDS,video.duration);
            const thumbs=[],canvas=document.createElement('canvas'),scale=Math.min(1,96/Math.max(video.videoWidth,video.videoHeight));
            canvas.width=Math.max(1,Math.round(video.videoWidth*scale));canvas.height=Math.max(1,Math.round(video.videoHeight*scale));
            if(!options.skipThumbnails)for(let i=0;i<6;i++){await seekFrame(video,video.duration*i/6,fileAbort.signal);canvas.getContext('2d').drawImage(video,0,0,canvas.width,canvas.height);thumbs.push(canvas.toDataURL('image/jpeg',0.65));}
            await seekFrame(video,0,fileAbort.signal);
            if(current===version&&!disposed)await notify('VideoReady',{name:file.name.replace(/\.[^.]+$/,'')||'Video clip',duration:video.duration,thumbnails:thumbs,nativeFps:timing?.fps??0,width:outputWidth,height:outputHeight});
        }catch(error){if(current===version&&!disposed){if(error.name==='AbortError')await notify('VideoFailed','Video loading stopped. Choose the video again when you return to the app.');else await fail(error);}}
    }
    const choose=()=>{if(input.files?.[0])loadFile(input.files[0]).catch(fail);};input.addEventListener('change',choose);
    return {
        // Also used by the browser regression fixture with a synthetic local video.
        loadFile,
        estimate,
        queueReview(session,id,start,end,fps){
            requireForeground();
            const estimate=cachedEstimate?.key===estimateKey(start,end,fps)?cachedEstimate.value:null;
            if(disposed||!fileSource||!estimate||estimate.tooLarge)throw new Error('Check this clip before adding it to the batch.');
            planFrames(start,end,fps,timing,outputWidth,outputHeight);
            session.addVideo(id,fileSource,{start,end,fps,estimate,timing});
            return {id,start,end,fps};
        },
        cancelEstimate(){estimateAbort?.abort();},
        async loadPicked(){const file=pickedVideo;pickedVideo=null;if(file)await loadFile(file);},
        validate(start,end,fps){try{const frames=planFrames(start,end,fps,timing,outputWidth,outputHeight);return {valid:true,frames:frames.length,message:'Clip is within the duration and frame limits.'};}catch(error){return {valid:false,frames:0,message:error.message};}},
        async loadStream(stream,name='Pinterest video.mp4') {
            const bytes=await stream.arrayBuffer();
            if(disposed)return;
            if(bytes.byteLength>MAX_VIDEO_BYTES)throw new Error('Choose a video no larger than 20 MB.');
            await loadFile(new File([bytes],name,{type:'video/mp4'}));
        },
        setRange(start,end){estimateAbort?.abort();rangeEnd=end;if(!converting&&(video.currentTime<start||video.currentTime>end))queueSeek(start).catch(fail);},
        async preview(start,end){estimateAbort?.abort();await estimateWork.catch(()=>{});rangeEnd=end;if(!video.paused){video.pause();return;}await seekWork;if(video.currentTime<start||video.currentTime>=end)await seekFrame(video,start,fileAbort?.signal);await video.play();},
        async convert(start,end,fps=12){
            requireForeground();
            if(converting||!url)throw new Error('Choose a video first.');
            if(!Number.isFinite(start)||!Number.isFinite(end)||start<0||end>video.duration+.001)throw new Error('Choose a clip within the video.');
            const frames=planFrames(start,end,fps,timing,outputWidth,outputHeight);
            const estimated=cachedEstimate?.key===estimateKey(start,end,fps)?cachedEstimate.value:options.estimate;
            if(estimated?.tooLarge)throw new Error('This selection is estimated to exceed 500 KB. Shorten it or choose fewer frames per second.');
            estimateAbort?.abort();await estimateWork.catch(()=>{});requireForeground();
            converting=true;conversionAbort=new AbortController();video.pause();nextSeek=null;
            const signal=conversionAbort.signal;
            const finishJob=startJob('Creating animated sticker…');
            const finishTiming=beginTiming('Video: direct WebP encoding', `frames=${frames.length}; source=${outputWidth}x${outputHeight}`);
            let pool;
            try{
                await seekWork;signal.throwIfAborted();
                const {fit,animation}=await import('../conversion/sticker-converter.js');
                signal.throwIfAborted();
                const canvas=document.createElement('canvas');canvas.width=canvas.height=512;
                const context=canvas.getContext('2d',{willReadFrequently:true});
                const initialQuality=estimated?.quality??.8;
                const qualities=[...new Set([initialQuality,...[.3,.1].filter(quality=>quality<initialQuality)])];
                let actualBytes=0;
                for(let attempt=0;attempt<qualities.length;attempt++){
                    signal.throwIfAborted();requireForeground();
                    const quality=qualities[attempt],output=[],pending=[];
                    let total=64,lastPixels,lastFrame;
                    pool=createFramePool(Math.min(options.workers??effectiveWorkers(),frames.length),signal);
                    const finishWorkers=beginTiming('Video: parallel WebP frames', `workers=${pool?.count??0}; quality=${quality}; attempt=${attempt+1}`);
                    try {
                        const write=async item=>{
                            const frame=await item;signal.throwIfAborted();
                            total+=frame.bytes.length+24;actualBytes=total;
                            if(total>MAX_STICKER_BYTES)throw new Error('sticker-size');
                            output.push(frame);
                        };
                        const fallback=pool?null:await import('../codecs/webp-encoder.js');
                        signal.throwIfAborted();
                        for(let i=0;i<frames.length;i++){
                            signal.throwIfAborted();requireForeground();
                            await seekFrame(video,frames[i].time,signal);
                            fit(context,video,video.videoWidth,video.videoHeight);
                            const pixels=context.getImageData(0,0,512,512).data;
                            const duration=Math.round((frames[i].end-start)*1000)-Math.round((frames[i].time-start)*1000);
                            if(samePixels(lastPixels,pixels))lastFrame.duration+=duration;
                            else {
                                lastPixels=pixels.slice();lastFrame={duration};
                                const frame=lastFrame;
                                const encoding=pool?pool.encode(pixels,512,512,quality):fallback.encode({data:pixels,width:512,height:512},quality);
                                const item=encoding.then(bytes=>{frame.bytes=bytes;return frame;});item.catch(()=>{});pending.push(item);
                                if(pending.length>=(pool?.count??1)+1)await write(pending.shift());
                            }
                            if(i%4===0)await notify('VideoProgress',Math.min(95,Math.round((i+1)/frames.length*95)));
                            await new Promise(resolve=>setTimeout(resolve,0));
                        }
                        for(const item of pending)await write(item);
                        signal.throwIfAborted();requireForeground();
                        const bytes=animation(ensureAnimationFrames(output));actualBytes=bytes.length;
                        if(bytes.length>MAX_STICKER_BYTES)throw new Error('sticker-size');
                        await notify('VideoProgress',100);signal.throwIfAborted();requireForeground();
                        return new Blob([bytes],{type:'image/webp'});
                    } catch(error) {
                        if(error.message!=='sticker-size')throw error;
                    } finally { pool?.dispose();pool=null;finishWorkers(); }
                }
                throw new Error(`This sticker exceeds 500 KB (at least ${Math.ceil(actualBytes/1024)} KB at low quality). Shorten the clip or choose fewer frames per second.`);
            }finally{pool?.dispose();finishTiming();finishJob();converting=false;conversionAbort=null;}
        },
        cancel(){estimateAbort?.abort();conversionAbort?.abort();},
        dispose(){estimateAbort?.abort();unwatchForeground();disposed=true;++version;fileAbort?.abort();conversionAbort?.abort();controls.dispose();input.removeEventListener('change',choose);video.pause();video.removeEventListener('timeupdate',update);video.removeEventListener('play',playback);video.removeEventListener('pause',playback);video.removeAttribute('src');video.load();if(url)URL.revokeObjectURL(url);}
    };
}
