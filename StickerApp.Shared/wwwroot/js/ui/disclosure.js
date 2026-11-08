export function create(root, reference) {
    const close=()=>reference.invokeMethodAsync('ClosePopup').catch(()=>{});
    const outside=event=>{if(root.open&&!root.contains(event.target))close();};
    const key=event=>{if(root.open&&event.key==='Escape'){event.preventDefault();event.stopPropagation();close();}};
    document.addEventListener('pointerdown',outside,true);root.addEventListener('keydown',key);
    return {dispose(){document.removeEventListener('pointerdown',outside,true);root.removeEventListener('keydown',key);}};
}
