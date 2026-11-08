// Preview-only zoom never changes exported pixels or the 512-unit workspace.
export function editorView(canvas,root){
    const wrap=canvas.parentElement, viewport=wrap.parentElement;
    let zoom=1,x=0,y=0,pan=false,drag=null;
    function sync(){
        wrap.style.transform=`translate(${x}px,${y}px) scale(${zoom})`;
        for(const label of root.querySelectorAll('[data-view-zoom-label]'))label.textContent=String(Math.round(zoom*100))+'%';
        for(const input of root.querySelectorAll('[data-view-zoom]'))input.value=String(Math.round(zoom*100));
        for(const button of root.querySelectorAll('[data-view-pan]'))button.setAttribute('aria-pressed',String(pan));
        viewport.classList.toggle('pan-preview',pan);
    }
    function click(event){
        const target=event.target.closest('[data-view-zoom-in],[data-view-zoom-out],[data-view-reset],[data-view-pan]');
        if(!target)return;
        if(target.hasAttribute('data-view-reset')){zoom=1;x=y=0;pan=false;}
        else if(target.hasAttribute('data-view-pan'))pan=!pan;
        else zoom=Math.max(.25,Math.min(4,zoom+(target.hasAttribute('data-view-zoom-in') ? .25 : -.25)));
        sync();
    }
    function input(event){if(event.target.matches('[data-view-loupe]')){root.dataset.cutoutLoupe=event.target.checked?'on':'off';try{localStorage.setItem('stickers.cutout-loupe',root.dataset.cutoutLoupe);}catch{}return;}if(event.target.matches('[data-view-zoom]')){const value=Number(event.target.value);if(Number.isFinite(value)&&value>=25&&value<=400){zoom=value/100;sync();}}}
    function down(event){if(!pan || event.button!==0)return;event.preventDefault();event.stopImmediatePropagation();drag={id:event.pointerId,x:event.clientX,y:event.clientY,ox:x,oy:y};viewport.setPointerCapture(event.pointerId);}
    function move(event){if(!drag)return;event.preventDefault();event.stopImmediatePropagation();const limit=viewport.clientWidth*zoom;x=Math.max(-limit,Math.min(limit,drag.ox+event.clientX-drag.x));y=Math.max(-limit,Math.min(limit,drag.oy+event.clientY-drag.y));sync();}
    function up(event){if(!drag)return;event.stopImmediatePropagation();if(viewport.hasPointerCapture(event.pointerId))viewport.releasePointerCapture(event.pointerId);drag=null;}
    try{root.dataset.cutoutLoupe=localStorage.getItem('stickers.cutout-loupe')==='off'?'off':'on';}catch{}
    for(const toggle of root.querySelectorAll('[data-view-loupe]'))toggle.checked=root.dataset.cutoutLoupe!=='off';
    root.addEventListener('click',click);root.addEventListener('input',input);
    viewport.addEventListener('pointerdown',down,true);viewport.addEventListener('pointermove',move,true);viewport.addEventListener('pointerup',up,true);viewport.addEventListener('pointercancel',up,true);
    return {get zoom(){return zoom;},dispose(){wrap.style.transform='';root.removeEventListener('click',click);root.removeEventListener('input',input);viewport.removeEventListener('pointerdown',down,true);viewport.removeEventListener('pointermove',move,true);viewport.removeEventListener('pointerup',up,true);viewport.removeEventListener('pointercancel',up,true);}};
}
export function cutoutLoupe(canvas,root){
    const lens=document.createElement('canvas');lens.width=lens.height=112;lens.className='cutout-loupe';lens.hidden=true;lens.setAttribute('aria-hidden','true');root.append(lens);
    return {
        show(event,point){
            if(root.dataset.cutoutLoupe==='off'){lens.hidden=true;return;}
            const rect=root.getBoundingClientRect(),sourceRect=canvas.getBoundingClientRect();
            const region=56*canvas.width/sourceRect.width,ctx=lens.getContext('2d');
            ctx.clearRect(0,0,112,112);ctx.fillStyle='#252830';ctx.fillRect(0,0,112,112);ctx.imageSmoothingEnabled=false;
            ctx.drawImage(canvas,point.x-region/2,point.y-region/2,region,region,0,0,112,112);
            ctx.strokeStyle='#fff';ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(48,56);ctx.lineTo(64,56);ctx.moveTo(56,48);ctx.lineTo(56,64);ctx.stroke();
            lens.style.left=`${Math.max(0,Math.min(rect.width-112,event.clientX-rect.left-56))}px`;
            const above=event.clientY-rect.top-144;
            lens.style.top=`${Math.max(0,Math.min(rect.height-112,above>=0?above:event.clientY-rect.top+40))}px`;lens.hidden=false;
        },hide(){lens.hidden=true;},dispose(){lens.remove();}
    };
}
