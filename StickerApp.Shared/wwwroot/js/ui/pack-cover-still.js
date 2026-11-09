const cache=new Map();
export function firstFrame(source) {
    if(cache.has(source))return cache.get(source);
    const work=(async()=>{
        const blob=await(await fetch(source)).blob();
        let image,url;
        try {
            if(typeof createImageBitmap==='function')image=await createImageBitmap(blob);
            else {image=new Image();url=URL.createObjectURL(blob);image.src=url;await image.decode();}
            const width=image.width||image.naturalWidth,height=image.height||image.naturalHeight;
            if(!width||!height)throw new Error('Invalid cover image.');
            const scale=Math.min(1,256/Math.max(width,height)),canvas=document.createElement('canvas');
            canvas.width=Math.max(1,Math.round(width*scale));canvas.height=Math.max(1,Math.round(height*scale));
            canvas.getContext('2d').drawImage(image,0,0,canvas.width,canvas.height);
            return canvas.toDataURL('image/png');
        } finally {image?.close?.();if(url){image.src='';URL.revokeObjectURL(url);}}
    })();
    cache.set(source,work);
    while(cache.size>12)cache.delete(cache.keys().next().value);
    work.catch(()=>cache.delete(source));return work;
}
