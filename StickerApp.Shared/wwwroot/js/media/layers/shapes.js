// Shapes use the normal image-layer pipeline: transforms, cutouts, history and backups.
export function createShape(kind,color='#80d4b0'){
    if(!['star','circle','square','triangle','cube','heart','arrow'].includes(kind))throw new Error('Unknown shape.');
    if(!/^#[0-9a-f]{6}$/i.test(color))throw new Error('Choose a valid shape color.');
    const canvas=document.createElement('canvas');canvas.width=canvas.height=512;
    const c=canvas.getContext('2d');c.fillStyle=color;
    const polygon=points=>{c.beginPath();points.forEach(([x,y],i)=>i?c.lineTo(x,y):c.moveTo(x,y));c.closePath();c.fill();};
    if(kind==='circle'){c.beginPath();c.arc(256,256,224,0,Math.PI*2);c.fill();}
    if(kind==='square')c.fillRect(32,32,448,448);
    if(kind==='triangle')polygon([[256,32],[480,480],[32,480]]);
    if(kind==='star')polygon(Array.from({length:10},(_,i)=>{const angle=-Math.PI/2+i*Math.PI/5,r=i%2?94:224;return [256+Math.cos(angle)*r,256+Math.sin(angle)*r];}));
    if(kind==='arrow')polygon([[32,176],[288,176],[288,48],[480,256],[288,464],[288,336],[32,336]]);
    if(kind==='heart'){c.beginPath();c.moveTo(256,468);c.bezierCurveTo(218,430,24,294,32,168);c.bezierCurveTo(40,36,190,12,256,128);c.bezierCurveTo(322,12,472,36,480,168);c.bezierCurveTo(488,294,294,430,256,468);c.fill();}
    if(kind==='cube'){
        polygon([[256,32],[464,144],[256,256],[48,144]]);
        polygon([[48,144],[256,256],[256,480],[48,368]]);
        polygon([[256,256],[464,144],[464,368],[256,480]]);
        c.fillStyle='#ffffff';c.globalAlpha=.22;polygon([[256,32],[464,144],[256,256],[48,144]]);
        c.fillStyle='#000000';c.globalAlpha=.22;polygon([[256,256],[464,144],[464,368],[256,480]]);c.globalAlpha=1;
    }
    return canvas.toDataURL('image/png');
}
