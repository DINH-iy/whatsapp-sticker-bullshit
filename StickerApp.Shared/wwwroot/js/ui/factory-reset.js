const isAppKey=key=>/^(stickers\.|stick-it\.|stik-er-in\.)/.test(key);
export async function clearPreferences(){
    for(const storage of [localStorage,sessionStorage]){
        for(const key of Object.keys(storage))if(isAppKey(key))storage.removeItem(key);
    }
    await new Promise((resolve,reject)=>{
        const request=indexedDB.deleteDatabase('stik-interface-font');
        request.onsuccess=()=>resolve();request.onerror=()=>reject(request.error);
        request.onblocked=()=>reject(new Error('Close other app tabs and retry to remove the cached interface font.'));
    });
}
export function restart(){window.location.replace(new URL('./',document.baseURI).href);}
