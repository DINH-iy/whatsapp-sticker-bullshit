const key='stik-er-in.warn-before-layers';
export function shouldWarn(){try{return localStorage.getItem(key)!=='false';}catch{return true;}}
export function setWarning(enabled){localStorage.setItem(key,String(enabled));}
