export function create(root,reference){
    const viewport=root.querySelector('.app-guide-slides');let frame=0,index=0;
    const slides=[...viewport.children];slides.forEach((slide,i)=>slide.setAttribute('aria-hidden',String(i!==0)));
    const update=()=>{cancelAnimationFrame(frame);frame=requestAnimationFrame(()=>{const next=Math.max(0,Math.min(4,Math.round(viewport.scrollLeft/Math.max(1,viewport.clientWidth))));if(next!==index){index=next;slides.forEach((slide,i)=>slide.setAttribute('aria-hidden',String(i!==index)));reference.invokeMethodAsync('SlideChanged',index).catch(()=>{});}});};
    viewport.addEventListener('scroll',update,{passive:true});
    return {go(next){viewport.scrollTo({left:viewport.clientWidth*next,behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});},dispose(){cancelAnimationFrame(frame);viewport.removeEventListener('scroll',update);}};
}
