import {luminance} from './rainbow-palette.js';
const hex=rgb=>'#'+rgb.map(v=>Math.round(v*255).toString(16).padStart(2,'0')).join('');
export function customPalette(value,dark){
    const alpha=value.length===9?parseInt(value.slice(7,9),16)/255:1;
    const surface=dark?[.094,.102,.118]:[1,1,1];
    const base=[1,3,5].map((offset,index)=>parseInt(value.slice(offset,offset+2),16)/255*alpha+surface[index]*(1-alpha));
    let accent=[...base],button=[...base];
    for(let i=0;i<150&&(dark?luminance(accent)<.32:luminance(accent)>.13);i++)accent=accent.map(v=>dark?v+(1-v)*.025:v*.975);
    for(let i=0;i<150&&luminance(button)>.16;i++)button=button.map(v=>v*.975);
    return {accent:hex(accent),button:hex(button)};
}
export function apply(){
    const root=document.documentElement;
    if(root.dataset.color!=='custom'){root.style.removeProperty('--accent');root.style.removeProperty('--button-background');return;}
    const colors=customPalette(root.dataset.customColor||'#32875f',root.dataset.theme==='dark');
    root.style.setProperty('--accent',colors.accent);root.style.setProperty('--button-background',colors.button);
}
