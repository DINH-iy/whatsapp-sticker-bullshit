function rgb(h,s,l){
    const a=s*Math.min(l,1-l),f=n=>{const k=(n+h/30)%12;return l-a*Math.max(-1,Math.min(k-3,9-k,1));};
    return [f(0),f(8),f(4)];
}
export function luminance(color){return color.reduce((sum,v,i)=>sum+[.2126,.7152,.0722][i]*(v<=.04045?v/12.92:((v+.055)/1.055)**2.4),0);}
function hex(color){return '#'+color.map(v=>Math.round(v*255).toString(16).padStart(2,'0')).join('');}
export function paletteForPosition(y,height,dark){
    const hue=Math.max(0,Math.min(1,y/Math.max(1,height)))*280;
    let light=dark?.68:.35,accent=rgb(hue,.72,light);
    for(let i=0;i<60&&(dark?luminance(accent)<.32:luminance(accent)>.13);i++){light+=dark?.005:-.005;accent=rgb(hue,.72,light);}
    let buttonLight=.34,button=rgb(hue,.66,buttonLight);
    for(let i=0;i<60&&luminance(button)>.16;i++){buttonLight-=.005;button=rgb(hue,.66,buttonLight);}
    return {accent:hex(accent),button:hex(button),hue};
}
