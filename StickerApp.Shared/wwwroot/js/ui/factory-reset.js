const isAppKey=key=>/^(stickers\.|stick-it\.|stik-er-in\.)/.test(key);
export async function clearPreferences(){
    for(const storage of [localStorage,sessionStorage]){
        for(const key of Object.keys(storage))if(isAppKey(key))storage.removeItem(key);
    }
    await new Promise((resolve,reject)=>{
        const request=indexedDB.deleteDatabase('stik-interface-font');
        const timer=setTimeout(()=>reject(new Error('Clearing the cached interface font timed out. Close other app tabs and retry.')),15000);
        const finish=error=>{clearTimeout(timer);if(error)reject(error);else resolve();};
        request.onsuccess=()=>finish();request.onerror=()=>finish(request.error??new Error('Could not clear the cached interface font.'));
        request.onblocked=()=>finish(new Error('Close other app tabs and retry to remove the cached interface font.'));
    });
}
export function restart(){window.location.replace(new URL('./',document.baseURI).href);}

// Runs locally, even if a browser-host connection stops responding. No fake
// percentage: only C# file-deletion updates drive the progress bar.
export function watchProgress(root, labels) {
    const started=performance.now();let lastUpdate=started;
    const elapsed=root.querySelector('[data-reset-elapsed]');
    const stalled=root.querySelector('[data-reset-stalled]');
    const format=(template,value)=>template.replace('{0}',String(value));
    const observer=new MutationObserver(()=>{lastUpdate=performance.now();stalled.hidden=true;});
    observer.observe(root,{attributes:true,attributeFilter:['data-reset-update']});
    let timer;
    const dispose=()=>{clearInterval(timer);observer.disconnect();};
    const tick=()=>{
        if(!root.isConnected){dispose();return;}
        const now=performance.now(),quiet=Math.floor((now-lastUpdate)/1000);
        elapsed.textContent=format(labels.elapsed,Math.floor((now-started)/1000));
        stalled.hidden=quiet<20;
        if(quiet>=20)stalled.textContent=format(labels.stalled,quiet);
    };
    timer=setInterval(tick,1000);tick();return {dispose};
}
