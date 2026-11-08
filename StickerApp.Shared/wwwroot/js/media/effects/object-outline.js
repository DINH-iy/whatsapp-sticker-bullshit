// Euclidean distance from opaque artwork gives a rounded outline through cutouts,
// disconnected objects and holes. Padding lets the stroke grow outside the source.
const cache = new WeakMap();
const canvasOf = (width,height) => typeof OffscreenCanvas !== 'undefined'
    ? new OffscreenCanvas(width,height) : Object.assign(document.createElement('canvas'),{width,height});
function distanceLine(input,output,n,sites,edges) {
    let k=0;sites[0]=0;edges[0]=-Infinity;edges[1]=Infinity;
    for(let q=1;q<n;q++) {
        let intersection;
        do {const v=sites[k];intersection=((input[q]+q*q)-(input[v]+v*v))/(2*(q-v));if(intersection<=edges[k])k--;else break;}while(k>=0);
        k++;sites[k]=q;edges[k]=intersection;edges[k+1]=Infinity;
    }
    k=0;
    for(let q=0;q<n;q++){while(edges[k+1]<q)k++;const delta=q-sites[k];output[q]=delta*delta+input[sites[k]];}
}
export function outlinedArtwork(source,layer) {
    const thickness=Math.max(0,Math.min(80,Number(layer.borderWidth)||0));
    if(!thickness)return null;
    const color=/^#[0-9a-f]{6}$/i.test(layer.borderColor??'')?layer.borderColor:'#ffffff';
    const key=`${layer.width}:${layer.height}:${thickness}:${color}`;
    if(!layer.gifSource&&cache.get(source)?.key===key)return cache.get(source).result;
    const scale=Math.min(1,1024/Math.max(layer.width,layer.height));
    const w=Math.max(1,Math.round(layer.width*scale)),h=Math.max(1,Math.round(layer.height*scale));
    const radius=thickness*scale,pad=Math.ceil(radius)+1,width=w+2*pad,height=h+2*pad;
    const image=canvasOf(width,height),context=image.getContext('2d',{willReadFrequently:true});
    context.drawImage(source,pad,pad,w,h);
    const original=context.getImageData(0,0,width,height),distance=new Float32Array(width*height);
    const length=Math.max(width,height),input=new Float64Array(length),output=new Float64Array(length),sites=new Int32Array(length),edges=new Float64Array(length+1);
    for(let y=0;y<height;y++){
        for(let x=0;x<width;x++)input[x]=original.data[(y*width+x)*4+3]>0?0:1e12;
        distanceLine(input,output,width,sites,edges);distance.set(output.subarray(0,width),y*width);
    }
    const stroke=context.createImageData(width,height),rgb=[1,3,5].map(offset=>parseInt(color.slice(offset,offset+2),16));
    for(let x=0;x<width;x++){
        for(let y=0;y<height;y++)input[y]=distance[y*width+x];
        distanceLine(input,output,height,sites,edges);
        for(let y=0;y<height;y++){
            const index=(y*width+x)*4;
            if(original.data[index+3])continue;
            const alpha=Math.max(0,Math.min(1,radius+.5-Math.sqrt(output[y])));
            if(!alpha)continue;
            stroke.data[index]=rgb[0];stroke.data[index+1]=rgb[1];stroke.data[index+2]=rgb[2];stroke.data[index+3]=Math.round(alpha*255);
        }
    }
    context.putImageData(stroke,0,0);context.drawImage(source,pad,pad,w,h);
    const result={image,pad:pad/scale,width:layer.width+2*pad/scale,height:layer.height+2*pad/scale};
    if(!layer.gifSource)cache.set(source,{key,result});
    return result;
}
export function drawLayerArtwork(context,source,layer) {
    const outline=outlinedArtwork(source,layer);
    if(outline)context.drawImage(outline.image,-layer.width/2-outline.pad,-layer.height/2-outline.pad,outline.width,outline.height);
    else context.drawImage(source,-layer.width/2,-layer.height/2,layer.width,layer.height);
}
