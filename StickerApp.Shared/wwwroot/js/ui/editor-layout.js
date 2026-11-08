const key='stik-er-in.editor-layout.v1',legacyKey='stik-er-in.editor-layout-preview.v1';
const categories=['cutout','draw','text','adjust','layers'];
export function normalize(value={}){
    const order=Array.isArray(value?.categories)?value.categories:[];
    return {toolsPosition:value?.toolsPosition==='top'?'top':'bottom',
        controlsPosition:['left','right'].includes(value?.controlsPosition)?value.controlsPosition:'bottom',
        buttonSize:value?.buttonSize==='compact'?'compact':'comfortable',showTimeline:value?.showTimeline!==false,
        categories:[...new Set(order.filter(id=>categories.includes(id))),...categories.filter(id=>!order.includes(id))]};
}
export function read(){try{return normalize(JSON.parse(localStorage.getItem(key)||localStorage.getItem(legacyKey)||'{}'));}catch{return normalize();}}
export function save(value){const result=normalize(value);localStorage.setItem(key,JSON.stringify(result));return result;}
export function reset(){localStorage.removeItem(key);localStorage.removeItem(legacyKey);return normalize();}
