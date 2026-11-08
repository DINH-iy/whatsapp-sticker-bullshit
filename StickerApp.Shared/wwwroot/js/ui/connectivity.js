export function watch(reference){
    const update=()=>reference.invokeMethodAsync('SetOnline',navigator.onLine).catch(()=>{});
    window.addEventListener('online',update);window.addEventListener('offline',update);update();
    return {dispose(){window.removeEventListener('online',update);window.removeEventListener('offline',update);}};
}
