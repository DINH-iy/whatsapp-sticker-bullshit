import { rotationGrip } from '../shared/rotation-grip.js';
// One retained source for moving, rotating and sizing: dragging never resamples
// an already resampled preview. Coordinates are always in the 512px workspace.
export function createTransformHandles(canvas, root, changed, syncPreview) {
    const wrap = canvas.parentElement, overlay = document.createElement('div');
    overlay.className = 'image-transform-handles'; overlay.hidden = true; wrap.append(overlay);
    let source = null, value, drag;
    const locked = () => root.querySelector('[data-image-resize-ratio]')?.checked !== false;
    const buttons = [];
    const radians = () => value.rotation * Math.PI / 180;
    function draw() {
        canvas.width = canvas.height = 512;
        const context = canvas.getContext('2d');
        context.save(); context.translate(value.x, value.y); context.rotate(radians());
        context.drawImage(source, -value.imageWidth / 2, -value.imageHeight / 2, value.imageWidth, value.imageHeight); context.restore();
        syncPreview(); place();
        for(const field of root.querySelectorAll('[data-image-transform-property]')) {
            if(field!==document.activeElement)field.value=String(Math.round(value[field.dataset.imageTransformProperty]));
        }
        const size = root.querySelector('[data-image-resize-size]');
        if (size) size.textContent = `${Math.round(value.imageWidth)} × ${Math.round(value.imageHeight)}`;
        const angle = root.querySelector('[data-image-transform-angle]');
        if (angle) angle.textContent = `${Math.round(value.rotation)}°`;
    }
    function place() {
        if (!source) return;
        const scale = wrap.clientWidth / 512, a = radians(), c = Math.cos(a), s = Math.sin(a);
        for (const { button, x, y, mode } of buttons) {
            const dx = x * value.imageWidth / 2, dy = y * value.imageHeight / 2;
            let px = (value.x + dx*c - dy*s) * scale, py = (value.y + dx*s + dy*c) * scale;
            if (mode === 'rotate') {
                // A fixed offset in artwork coordinates: never switch sides as
                // the artwork rotates. Clamping at the workspace edge is continuous.
                const radius = Math.max(28, value.imageHeight*scale/2-60);
                px = value.x*scale + Math.sin(a)*radius;
                py = value.y*scale - Math.cos(a)*radius;
            }
            button.style.left = `${Math.max(18, Math.min(wrap.clientWidth-18, px))}px`;
            button.style.top = `${Math.max(18, Math.min(wrap.clientHeight-18, py))}px`;
        }
        Object.assign(outline.style, {left:`${value.x*scale}px`,top:`${value.y*scale}px`,width:`${value.imageWidth*scale}px`,height:`${value.imageHeight*scale}px`,transform:`translate(-50%,-50%) rotate(${value.rotation}deg)`});
    }
    function limitSize(w,h,preferWidth) {
        if (locked()) {
            const ratio = source.width/source.height;
            if(preferWidth) h=w/ratio; else w=h*ratio;
            const fit = Math.min(1,512/Math.max(w,h)); w*=fit; h*=fit;
        }
        value.imageWidth=Math.max(1,Math.min(512,w)); value.imageHeight=Math.max(1,Math.min(512,h));
    }
    function finish(event) {
        if (!drag || drag.pointer!==event.pointerId) return;
        if(event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
        drag=null; changed(Math.round(value.imageWidth),Math.round(value.imageHeight));
    }
    function bind(target,mode,x=0,y=0) {
        target.addEventListener('pointerdown',event=>{
            if(!source || drag || event.button!==0 || root.getAttribute('aria-busy')==='true')return;
            event.preventDefault();event.stopPropagation();
            const rect=wrap.getBoundingClientRect();
            drag={pointer:event.pointerId,clientX:event.clientX,clientY:event.clientY,start:{...value},scale:rect.width/512,
                angle:Math.atan2(event.clientY-rect.top-value.y*rect.height/512,event.clientX-rect.left-value.x*rect.width/512)};
            target.setPointerCapture(event.pointerId);
        });
        target.addEventListener('pointermove',event=>{
            if(!drag || drag.pointer!==event.pointerId)return;
            const dx=(event.clientX-drag.clientX)/drag.scale,dy=(event.clientY-drag.clientY)/drag.scale;
            if(mode==='move') { value.x=Math.max(0,Math.min(512,drag.start.x+dx)); value.y=Math.max(0,Math.min(512,drag.start.y+dy)); }
            else if(mode==='rotate') {
                const rect=wrap.getBoundingClientRect();
                const angle=Math.atan2(event.clientY-rect.top-value.y*drag.scale,event.clientX-rect.left-value.x*drag.scale);
                value.rotation=drag.start.rotation+(angle-drag.angle)*180/Math.PI;
                if(event.shiftKey)value.rotation=Math.round(value.rotation/15)*15;
            } else {
                const a=drag.start.rotation*Math.PI/180,localX=dx*Math.cos(a)+dy*Math.sin(a),localY=-dx*Math.sin(a)+dy*Math.cos(a);
                limitSize(drag.start.imageWidth+2*localX*x,drag.start.imageHeight+2*localY*y,!!x&&(!y||Math.abs(localX)>=Math.abs(localY)));
            }
            draw();
        });
        target.addEventListener('pointerup',finish);target.addEventListener('pointercancel',finish);
        target.addEventListener('keydown',event=>{
            if(!source||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;
            event.preventDefault();event.stopPropagation();
            const step=event.shiftKey?10:1,dx=event.key==='ArrowLeft'?-step:event.key==='ArrowRight'?step:0,dy=event.key==='ArrowUp'?-step:event.key==='ArrowDown'?step:0;
            if(mode==='move'){value.x=Math.max(0,Math.min(512,value.x+dx));value.y=Math.max(0,Math.min(512,value.y+dy));}
            else if(mode==='rotate')value.rotation+=dx||dy;
            else limitSize(value.imageWidth+dx*x,value.imageHeight+dy*y,!!(dx&&x));
            draw();changed(Math.round(value.imageWidth),Math.round(value.imageHeight));
        });
    }
    const outline=document.createElement('div');outline.className='image-transform-outline';outline.tabIndex=0;outline.setAttribute('role','button');outline.setAttribute('aria-label','Move image');overlay.append(outline);bind(outline,'move');
    for(const [name,x,y] of [['top left',-1,-1],['top',0,-1],['top right',1,-1],['left',-1,0],['right',1,0],['bottom left',-1,1],['bottom',0,1],['bottom right',1,1],['rotation',0,-1]]) {
        const mode=name==='rotation'?'rotate':'resize',button=document.createElement('button');button.type='button';button.className=`layer-handle image-transform-handle ${mode==='rotate'?'image-rotation-handle':''}`;
        button.setAttribute('aria-label',mode==='rotate'?'Rotate image':`Resize image ${name}`);button.dataset.tapFeedback='off';
        const grip=mode==='rotate'?rotationGrip():document.createElement('span');button.append(grip);overlay.append(button);buttons.push({button,x,y,mode});bind(button,mode,x,y);
    }
    function input(event){
        if(!source || !event.target.matches('[data-image-transform-property]'))return;
        const number=Number(event.target.value),property=event.target.dataset.imageTransformProperty;
        if(!Number.isFinite(number)||event.target.value==='')return;
        if(property==='imageWidth')limitSize(number,value.imageHeight,true);
        else if(property==='imageHeight')limitSize(value.imageWidth,number,false);
        draw();changed(Math.round(value.imageWidth),Math.round(value.imageHeight));
    }
    root.addEventListener('input',input);
    const observer=new ResizeObserver(place);observer.observe(wrap);
    function end(){
        if(!source)return null;
        const result={source,edit:{type:'transform',width:512,height:512,...value}};
        source=null;drag=null;overlay.hidden=true;
        canvas.width=result.source.width;canvas.height=result.source.height;canvas.getContext('2d').drawImage(result.source,0,0);syncPreview();
        for(const field of root.querySelectorAll('[data-animation-seek]'))field.disabled=false;
        return result;
    }
    return {
        get active(){return !!source;},
        begin(){end();source=document.createElement('canvas');source.width=canvas.width;source.height=canvas.height;source.getContext('2d').drawImage(canvas,0,0);
            value={x:256,y:256,imageWidth:source.width,imageHeight:source.height,rotation:0};overlay.hidden=false;
            for(const field of root.querySelectorAll('[data-animation-seek]'))field.disabled=true;
            draw();},end,dispose(){end();root.removeEventListener('input',input);observer.disconnect();overlay.remove();}
    };
}
