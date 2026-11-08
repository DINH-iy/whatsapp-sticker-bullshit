const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
function hsv(h,s,v=1){const c=v*s,x=c*(1-Math.abs(h/60%2-1)),m=v-c;const rgb=h<60?[c,x,0]:h<120?[x,c,0]:h<180?[0,c,x]:h<240?[0,x,c]:h<300?[x,0,c]:[c,0,x];return '#'+rgb.map(n=>Math.round((n+m)*255).toString(16).padStart(2,'0')).join('');}
export function create(wheel,reference){
    const knob=wheel.querySelector('.theme-color-knob');let hue=0,saturation=1,brightness=1,dragging=false,disposed=false;
    function display(){const a=hue*Math.PI/180;knob.style.left=`${50+Math.cos(a)*saturation*46}%`;knob.style.top=`${50+Math.sin(a)*saturation*46}%`;wheel.setAttribute('aria-valuenow',String(Math.round(hue)));}
    function set(color){const [r,g,b]=[1,3,5].map(i=>parseInt(color.slice(i,i+2),16)/255),max=Math.max(r,g,b),min=Math.min(r,g,b),d=max-min;
        hue=d?((max===r?(g-b)/d+(g<b?6:0):max===g?(b-r)/d+2:(r-g)/d+4)*60):0;saturation=max?d/max:0;brightness=max;display();}
    function choose(event){const rect=wheel.getBoundingClientRect(),x=event.clientX-rect.left-rect.width/2,y=event.clientY-rect.top-rect.height/2;hue=(Math.atan2(y,x)*180/Math.PI+360)%360;saturation=clamp(Math.hypot(x,y)/(rect.width*.46),0,1);brightness=1;display();if(!disposed)reference.invokeMethodAsync('WheelColorChanged',hsv(hue,saturation,brightness)).catch(()=>{});}
    function down(event){event.preventDefault();dragging=true;wheel.setPointerCapture(event.pointerId);choose(event);}
    function move(event){if(dragging)choose(event);}
    function up(){dragging=false;}
    function key(event){if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;event.preventDefault();hue=(hue+(event.key==='ArrowLeft'?-1:event.key==='ArrowRight'?1:0)+360)%360;saturation=clamp(saturation+(event.key==='ArrowUp'?.02:event.key==='ArrowDown'?-.02:0),0,1);display();reference.invokeMethodAsync('WheelColorChanged',hsv(hue,saturation,brightness)).catch(()=>{});}
    wheel.addEventListener('pointerdown',down);wheel.addEventListener('pointermove',move);wheel.addEventListener('pointerup',up);wheel.addEventListener('pointercancel',up);wheel.addEventListener('keydown',key);
    return {set,dispose(){disposed=true;wheel.removeEventListener('pointerdown',down);wheel.removeEventListener('pointermove',move);wheel.removeEventListener('pointerup',up);wheel.removeEventListener('pointercancel',up);wheel.removeEventListener('keydown',key);}};
}
