import { parseDiscordExport } from './discord-parser.js';
export function watch(input,reference){
    let worker,disposed=false,version=0;
    const change=async()=>{
        worker?.terminate();const current=++version,file=input.files?.[0];input.value='';if(!file)return;
        await reference.invokeMethodAsync('DiscordExportReading');
        try{
            let result;
            if(typeof Worker!=='undefined')result=await new Promise((resolve,reject)=>{
                worker=new Worker(new URL('./discord-export-worker.js',import.meta.url),{type:'module'});
                worker.onmessage=({data})=>data.error?reject(new Error(data.error)):resolve(data.result);
                worker.onerror=()=>reject(new Error('This browser could not read the ZIP. Try account/user.json instead.'));
                worker.postMessage({file});
            });else result=await parseDiscordExport(file);
            if(!disposed&&current===version)await reference.invokeMethodAsync('DiscordExportLoaded',result);
        }catch(error){if(!disposed&&current===version)await reference.invokeMethodAsync('DiscordExportFailed',error.message??'The export could not be read.');}
        finally{if(current===version){worker?.terminate();worker=null;}}
    };
    input.addEventListener('change',change);
    return {cancel(){++version;worker?.terminate();worker=null;},dispose(){disposed=true;++version;worker?.terminate();input.removeEventListener('change',change);}};
}
