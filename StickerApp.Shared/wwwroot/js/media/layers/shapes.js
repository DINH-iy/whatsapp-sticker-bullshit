// Shapes use the normal image-layer pipeline: transforms, cutouts, history and backups.
export function createShape(kind,color='#80d4b0',options={}){
    if(!['star','circle','square','triangle','cube','heart','arrow'].includes(kind))throw new Error('Unknown shape.');
    if(!/^#[0-9a-f]{6}$/i.test(color))throw new Error('Choose a valid shape color.');
    const canvas=document.createElement('canvas');canvas.width=canvas.height=512;
    const c=canvas.getContext('2d');c.fillStyle=color;
    const width=Number(options.thickness??0),stroke=options.outlineColor??color,filled=options.filled!==false,rounding=Number(options.rounding??0);
    if(!Number.isFinite(width)||width<0||width>64||!Number.isFinite(rounding)||rounding<0||rounding>100||!/^#[0-9a-f]{6}$/i.test(stroke))throw new Error('Choose a valid shape outline.');
    c.strokeStyle=stroke;c.lineWidth=Math.max(1,width);c.lineJoin=rounding>0?'round':'miter';c.miterLimit=2;
    const paint=()=>{if(filled)c.fill();if(width>0)c.stroke();};
    const polygon=points=>{
        c.beginPath();
        if(rounding>0){
            const corners=points.map((point,i)=>{
                const previous=points[(i+points.length-1)%points.length],next=points[(i+1)%points.length];
                const a=Math.hypot(previous[0]-point[0],previous[1]-point[1]),b=Math.hypot(next[0]-point[0],next[1]-point[1]),distance=Math.min(rounding,a/2,b/2);
                return {point,enter:[point[0]+(previous[0]-point[0])*distance/a,point[1]+(previous[1]-point[1])*distance/a],exit:[point[0]+(next[0]-point[0])*distance/b,point[1]+(next[1]-point[1])*distance/b]};
            });
            c.moveTo(...corners[0].enter);for(const corner of corners){c.lineTo(...corner.enter);c.quadraticCurveTo(...corner.point,...corner.exit);}
        }else points.forEach(([x,y],i)=>i?c.lineTo(x,y):c.moveTo(x,y));
        c.closePath();paint();
    };
    if(kind==='circle'){c.beginPath();c.arc(256,256,224,0,Math.PI*2);paint();}
    if(kind==='square'){c.beginPath();c.roundRect(32,32,448,448,rounding);paint();}
    if(kind==='triangle')polygon([[256,32],[480,480],[32,480]]);
    if(kind==='star')polygon(Array.from({length:10},(_,i)=>{const angle=-Math.PI/2+i*Math.PI/5,r=i%2?94:224;return [256+Math.cos(angle)*r,256+Math.sin(angle)*r];}));
    if(kind==='arrow')polygon([[32,176],[288,176],[288,48],[480,256],[288,464],[288,336],[32,336]]);
    if(kind==='heart'){c.beginPath();c.moveTo(256,468);c.bezierCurveTo(218,430,24,294,32,168);c.bezierCurveTo(40,36,190,12,256,128);c.bezierCurveTo(322,12,472,36,480,168);c.bezierCurveTo(488,294,294,430,256,468);paint();}
    if(kind==='cube'){
        polygon([[256,32],[464,144],[256,256],[48,144]]);
        polygon([[48,144],[256,256],[256,480],[48,368]]);
        polygon([[256,256],[464,144],[464,368],[256,480]]);
        if(filled){c.fillStyle='#ffffff';c.globalAlpha=.22;polygon([[256,32],[464,144],[256,256],[48,144]]);
        c.fillStyle='#000000';c.globalAlpha=.22;polygon([[256,256],[464,144],[464,368],[256,480]]);c.globalAlpha=1;}
    }
    return canvas.toDataURL('image/png');
}
