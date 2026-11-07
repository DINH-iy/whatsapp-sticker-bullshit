import { previewControls } from './animation-preview.js';
const MAX_VIDEO_BYTES = 100 * 1024 * 1024;
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
export function create(video,input,root,dotnet) {
    let url,disposed=false,fileAbort,conversionAbort,converting=false,version=0,rangeEnd=0,nextSeek=null,seekWork=Promise.resolve();
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
        const current=++version;fileAbort?.abort();conversionAbort?.abort();fileAbort=new AbortController();
        video.pause();if(url)URL.revokeObjectURL(url);url=null;
        await notify('VideoLoading');
        try{
            if(!file||file.size===0||file.size>MAX_VIDEO_BYTES)throw new Error('Choose a video between 1 byte and 100 MB.');
            const ready=waitFor(video,'loadeddata',fileAbort.signal);
            url=URL.createObjectURL(file);video.src=url;video.load();await ready;
            if(!Number.isFinite(video.duration)||video.duration<0.1||!video.videoWidth||!video.videoHeight)throw new Error('Choose a video with a readable duration of at least 0.1 seconds.');
            if(current!==version||disposed)return;
            rangeEnd=Math.min(10,video.duration);
            const thumbs=[],canvas=document.createElement('canvas'),scale=Math.min(1,96/Math.max(video.videoWidth,video.videoHeight));
            canvas.width=Math.max(1,Math.round(video.videoWidth*scale));canvas.height=Math.max(1,Math.round(video.videoHeight*scale));
            for(let i=0;i<6;i++){await seekFrame(video,video.duration*i/6,fileAbort.signal);canvas.getContext('2d').drawImage(video,0,0,canvas.width,canvas.height);thumbs.push(canvas.toDataURL('image/jpeg',0.65));}
            await seekFrame(video,0,fileAbort.signal);
            if(current===version&&!disposed)await notify('VideoReady',{name:file.name.replace(/\.[^.]+$/,'')||'Video clip',duration:video.duration,thumbnails:thumbs});
        }catch(error){if(current===version)await fail(error);}
    }
    const choose=()=>{if(input.files?.[0])loadFile(input.files[0]).catch(fail);};input.addEventListener('change',choose);
    return {
        // Also used by the browser regression fixture with a synthetic local video.
        loadFile,
        async loadStream(stream,name='Pinterest video.mp4') {
            const bytes=await stream.arrayBuffer();
            if(disposed)return;
            if(bytes.byteLength>MAX_VIDEO_BYTES)throw new Error('Choose a video smaller than 100 MB.');
            await loadFile(new File([bytes],name,{type:'video/mp4'}));
        },
        setRange(start,end){rangeEnd=end;if(!converting&&(video.currentTime<start||video.currentTime>end))queueSeek(start).catch(fail);},
        async preview(start,end){rangeEnd=end;if(!video.paused){video.pause();return;}await seekWork;if(video.currentTime<start||video.currentTime>=end)await seekFrame(video,start,fileAbort?.signal);await video.play();},
        async convert(start,end,fps=12){
            if(converting||!url)throw new Error('Choose a video first.');
            if(!Number.isFinite(start)||!Number.isFinite(end)||start<0||end>video.duration+0.001||end-start<0.1||end-start>10.001)throw new Error('Choose a clip between 0.1 and 10 seconds, within the video.');
            if(![8,12,20].includes(fps))throw new Error('Choose 8, 12, or 20 frames per second.');
            converting=true;conversionAbort=new AbortController();video.pause();nextSeek=null;
            const signal=conversionAbort.signal;
            try{
                await seekWork;signal.throwIfAborted();
                const {createEncoder}=await import('./video-gif-encoder.js');signal.throwIfAborted();
                const scale=Math.min(1,512/Math.max(video.videoWidth,video.videoHeight)),canvas=document.createElement('canvas');
                canvas.width=Math.max(1,Math.round(video.videoWidth*scale));canvas.height=Math.max(1,Math.round(video.videoHeight*scale));
                const context=canvas.getContext('2d',{willReadFrequently:true}),encoder=createEncoder(canvas.width,canvas.height);
                const count=Math.max(2,Math.ceil((end-start)*fps)),total=Math.round((end-start)*100),delay=total/count;
                for(let i=0;i<count;i++){
                    signal.throwIfAborted();await seekFrame(video,start+(end-start)*i/count,signal);
                    context.drawImage(video,0,0,canvas.width,canvas.height);
                    encoder.add(context.getImageData(0,0,canvas.width,canvas.height).data,(Math.round((i+1)*delay)-Math.round(i*delay))*10);
                    if(i%4===0||i===count-1)await notify('VideoProgress',Math.round((i+1)/count*100));
                    await new Promise(resolve=>setTimeout(resolve,0));
                }
                signal.throwIfAborted();return new Blob([encoder.finish()],{type:'image/gif'});
            }finally{converting=false;conversionAbort=null;}
        },
        cancel(){conversionAbort?.abort();},
        dispose(){disposed=true;++version;fileAbort?.abort();conversionAbort?.abort();controls.dispose();input.removeEventListener('change',choose);video.pause();video.removeEventListener('timeupdate',update);video.removeEventListener('play',playback);video.removeEventListener('pause',playback);video.removeAttribute('src');video.load();if(url)URL.revokeObjectURL(url);}
    };
}
