export function applyStickerBorder(context,{thickness=0,color='#ffffff',radius=0}){
    const {width,height}=context.canvas;
    const copy=typeof OffscreenCanvas!=='undefined'?new OffscreenCanvas(width,height):Object.assign(document.createElement('canvas'),{width,height});
    copy.getContext('2d').drawImage(context.canvas,0,0);
    const line=Math.max(0,Math.min(Math.min(width,height)/2,Number(thickness)||0));
    const round=Math.max(0,Math.min(Math.min(width,height)/2,Number(radius)||0));
    context.clearRect(0,0,width,height);context.save();context.beginPath();context.roundRect(0,0,width,height,round);context.clip();context.drawImage(copy,0,0);context.restore();
    if(line){context.save();context.lineWidth=line;context.strokeStyle=/^#[0-9a-f]{6}$/i.test(color)?color:'#ffffff';context.beginPath();context.roundRect(line/2,line/2,width-line,height-line,Math.max(0,round-line/2));context.stroke();context.restore();}
}
