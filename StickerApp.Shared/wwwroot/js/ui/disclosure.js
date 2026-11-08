export function create(root, reference) {
    const close=()=>reference.invokeMethodAsync('ClosePopup').catch(()=>{});
    const outside=event=>{if(root.open&&!root.contains(event.target))close();};
    const key=event=>{if(root.open&&event.key==='Escape'){event.preventDefault();event.stopPropagation();close();}};
    let frame=0;
    function position(){
        cancelAnimationFrame(frame);
        frame=requestAnimationFrame(()=>{
            if(!root.open)return;
            const panel=root.querySelector(':scope > :not(summary)');
            if(!panel)return;
            const viewport=window.visualViewport, left=viewport?.offsetLeft||0, top=viewport?.offsetTop||0;
            const width=viewport?.width||innerWidth,height=viewport?.height||innerHeight;
            const modal=root.closest('dialog')?.getBoundingClientRect();
            const bounds={left:Math.max(left,modal?.left??left),right:Math.min(left+width,modal?.right??left+width),top:Math.max(top,modal?.top??top),bottom:Math.min(top+height,modal?.bottom??top+height)};
            const anchor=root.querySelector('summary').getBoundingClientRect();
            Object.assign(panel.style,{position:'fixed',boxSizing:'border-box',maxWidth:`${bounds.right-bounds.left-24}px`,maxHeight:`${bounds.bottom-bounds.top-24}px`,margin:'0',overflowY:'auto',right:'auto',bottom:'auto',left:'0px',top:'0px'});
            const size=panel.getBoundingClientRect();
            const x=Math.max(bounds.left+12,Math.min(bounds.right-size.width-12,anchor.right-size.width));
            let y=anchor.bottom+6;
            if(y+size.height>bounds.bottom-12)y=anchor.top-size.height-6;
            y=Math.max(bounds.top+12,Math.min(bounds.bottom-size.height-12,y));
            panel.style.left=`${x-size.left}px`;panel.style.top=`${y-size.top}px`;
        });
    }
    const observer=new MutationObserver(position);observer.observe(root,{attributes:true,attributeFilter:['open']});
    document.addEventListener('pointerdown',outside,true);root.addEventListener('keydown',key);
    window.addEventListener('resize',position);document.addEventListener('scroll',position,true);
    window.visualViewport?.addEventListener('resize',position);
    return {dispose(){cancelAnimationFrame(frame);observer.disconnect();document.removeEventListener('pointerdown',outside,true);root.removeEventListener('keydown',key);window.removeEventListener('resize',position);document.removeEventListener('scroll',position,true);window.visualViewport?.removeEventListener('resize',position);}};
}
