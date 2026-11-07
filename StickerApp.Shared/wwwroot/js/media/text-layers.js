const loaded = new Map();
const families = {meme:'StikMeme',sans:'sans-serif',serif:'serif',mono:'monospace'};
export const defaultText = () => ({content:'YOUR TEXT',font:'meme',fontSize:52,maxWidth:460,lineHeight:1.15,align:'center',color:'#ffffff',gradient:false,gradientColor:'#ff80c0',gradientAngle:90,outlineColor:'#000000',outlineWidth:3,bold:false,italic:false,underline:false,strike:false,uppercase:false,shadow:false,shadowColor:'#000000',background:false,backgroundColor:'#000000'});
export async function validateTextFont(data) {
    try { await new FontFace('StikFontCheck',`url("${data}")`).load(); }
    catch { throw new Error('This font cannot be read. Try another TTF, OTF, WOFF, or WOFF2 file.'); }
}
export async function loadTextFont(id,data) {
    if(families[id] && id!=='meme')return;
    if(!loaded.has(id)){
        const family=id==='meme'?'StikMeme':id;
        const source=id==='meme'?new URL('../../fonts/Anton-Regular.ttf',import.meta.url).href:data;
        if(!source)throw new Error('This custom font is unavailable. Upload it again.');
        const promise=(async()=>{const face=new FontFace(family,`url("${source}")`);await face.load();document.fonts.add(face);})();
        loaded.set(id,promise);
        try{await promise;}catch{loaded.delete(id);throw new Error('This font cannot be read. Try another TTF, OTF, WOFF, or WOFF2 file.');}
    }
    await loaded.get(id);
}
function linesFor(context,text,maxWidth) {
    const lines=[];
    const words=typeof Intl.Segmenter==='function'?new Intl.Segmenter(undefined,{granularity:'word'}):null;
    const graphemes=typeof Intl.Segmenter==='function'?new Intl.Segmenter(undefined,{granularity:'grapheme'}):null;
    for(const paragraph of text.split('\n')){
        let line='';
        for(const item of words?[...words.segment(paragraph)].map(s=>s.segment):Array.from(paragraph)){
            if(line&&context.measureText(line+item).width>maxWidth){lines.push(line.trimEnd());line='';}
            if(context.measureText(item).width>maxWidth){
                for(const character of graphemes?[...graphemes.segment(item)].map(s=>s.segment):Array.from(item)){
                    if(line&&context.measureText(line+character).width>maxWidth){lines.push(line);line='';}line+=character;
                }
            }else line+=line?item:item.trimStart();
        }
        lines.push(line);
    }
    return lines;
}
export async function renderText(options) {
    const text={...defaultText(),...options};
    if(typeof text.content!=='string'||text.content.length>1000||text.fontSize<8||text.fontSize>256||text.maxWidth<32||text.maxWidth>1000)throw new Error('Text must be at most 1,000 characters, at 8–256 px, with a 32–1,000 px wrap width.');
    await loadTextFont(text.font,text.fontData);
    const canvas=document.createElement('canvas'),context=canvas.getContext('2d');
    const family=families[text.font]??text.font;
    const font=`${text.italic?'italic ':''}${text.bold?'700':'400'} ${text.fontSize}px ${['sans-serif','serif','monospace'].includes(family)?family:`"${family}"`}, "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif`;
    context.font=font;
    const lines=linesFor(context,text.uppercase?text.content.toLocaleUpperCase():text.content,text.maxWidth);
    const padding=Math.ceil(text.outlineWidth+ (text.shadow?14:6)+(text.italic?text.fontSize*.22:0));
    const metrics=lines.map(line=>context.measureText(line));
    const contentWidth=Math.max(1,...metrics.map(m=>m.width));
    const ascent=Math.max(text.fontSize,...metrics.map(m=>m.actualBoundingBoxAscent||0));
    const descent=Math.max(text.fontSize*.25,...metrics.map(m=>m.actualBoundingBoxDescent||0));
    const lineHeight=text.fontSize*text.lineHeight;
    canvas.width=Math.min(1024,Math.ceil(contentWidth+padding*2));canvas.height=Math.max(1,Math.ceil(ascent+descent+lineHeight*(lines.length-1)+padding*2));
    if(canvas.height>1024)throw new Error('This text is too tall. Use fewer lines, a wider text box, or a smaller font.');
    context.font=font;context.textBaseline='alphabetic';context.textAlign=text.align;context.lineJoin='round';
    if(text.background){context.fillStyle=text.backgroundColor;context.fillRect(0,0,canvas.width,canvas.height);}
    let fill=text.color;
    if(text.gradient){
        const angle=text.gradientAngle*Math.PI/180,dx=Math.cos(angle)*canvas.width/2,dy=Math.sin(angle)*canvas.height/2;
        fill=context.createLinearGradient(canvas.width/2-dx,canvas.height/2-dy,canvas.width/2+dx,canvas.height/2+dy);fill.addColorStop(0,text.color);fill.addColorStop(1,text.gradientColor);
    }
    const x=text.align==='left'?padding:text.align==='right'?canvas.width-padding:canvas.width/2;
    if(text.shadow){context.shadowColor=text.shadowColor;context.shadowBlur=8;context.shadowOffsetX=3;context.shadowOffsetY=3;}
    for(let i=0;i<lines.length;i++){
        const line=lines[i],y=padding+ascent+(i*lineHeight);
        if(text.outlineWidth>0){context.strokeStyle=text.outlineColor;context.lineWidth=text.outlineWidth*2;context.strokeText(line,x,y);}
        context.fillStyle=fill;context.fillText(line,x,y);
        const width=context.measureText(line).width,left=text.align==='left'?x:text.align==='right'?x-width:x-width/2;
        context.lineWidth=Math.max(1,text.fontSize/18);context.strokeStyle=fill;
        for(const position of [text.underline?y+text.fontSize*.1:null,text.strike?y-text.fontSize*.3:null]){
            if(position===null)continue;context.beginPath();context.moveTo(left,position);context.lineTo(left+width,position);context.stroke();
        }
    }
    return {source:canvas.toDataURL('image/png'),image:canvas,width:canvas.width,height:canvas.height,text};
}
