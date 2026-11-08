import {paletteForPosition} from './rainbow-palette.js';
const selector='a,button,.button,input,select,textarea,progress,h1,h2,h3,summary,.settings-card,.editor-tool-section,.app-brand-mark,.stat-highlight';
const originals=new Map();let enabled=false,observer,frame=0,timer=0,mode='fixed',route='';
function restore(element,values){for(const [key,value]of values){if(value)element.style.setProperty(key,value);else element.style.removeProperty(key);}}
function clear(){for(const [element,values]of originals)restore(element,values);originals.clear();}
function update(){
    frame=0;if(!enabled)return;
    const current=location.pathname+location.search;
    if(current!==route){clear();route=current;}
    const dark=document.documentElement.dataset.theme==='dark',scrolling=mode==='scroll';
    const height=scrolling?window.innerHeight:Math.max(document.documentElement.scrollHeight,window.innerHeight);
    const updates=[];
    for(const element of document.querySelectorAll(selector)){
        if(element.closest('.danger,.nsfw-name-tag')){if(originals.has(element)){restore(element,originals.get(element));originals.delete(element);}continue;}
        if(!scrolling&&originals.has(element)||element.closest('svg,.demo-artboard,canvas'))continue;
        const rect=element.getBoundingClientRect();if(!rect.width||!rect.height)continue;
        if(scrolling&&(rect.bottom<0||rect.top>height))continue;
        let y=rect.top+Math.min(rect.height,48)/2,total=height;
        if(!scrolling){
            y+=window.scrollY;
            for(let parent=element.parentElement;parent&&parent!==document.body&&parent!==document.documentElement;parent=parent.parentElement){
                y+=parent.scrollTop;
                if(parent.scrollHeight>parent.clientHeight+2)total=Math.max(total,parent.scrollHeight+parent.getBoundingClientRect().top+window.scrollY);
            }
        }
        updates.push([element,paletteForPosition(y,total,dark)]);
    }
    for(const [element,colors]of updates){
        if(!originals.has(element))originals.set(element,['--accent','--button-background'].map(key=>[key,element.style.getPropertyValue(key)]));
        if(element.style.getPropertyValue('--accent')!==colors.accent)element.style.setProperty('--accent',colors.accent);
        if(element.style.getPropertyValue('--button-background')!==colors.button)element.style.setProperty('--button-background',colors.button);
    }
    for(const [element,values]of originals)if(!element.isConnected){restore(element,values);originals.delete(element);}
}
function schedule(){if(enabled&&!frame)frame=requestAnimationFrame(update);}
function changed(){clearTimeout(timer);timer=setTimeout(schedule,100);}
function scrolled(){if(mode==='scroll')schedule();}
function resized(){if(mode==='scroll')schedule();}
export function sync(){
    const root=document.documentElement,active=root.dataset.color==='rainbow',next=root.dataset.rainbowMode==='scroll'?'scroll':'fixed';
    // Theme/mode changes deliberately assign a new palette; scrolling never reassigns fixed colors.
    if(next!==mode||root.dataset.theme!==sync.theme){clear();route='';}mode=next;sync.theme=root.dataset.theme;
    if(active&&!enabled){
        enabled=true;
        window.addEventListener('scroll',scrolled,{capture:true,passive:true});window.addEventListener('resize',resized);
        observer=new MutationObserver(changed);observer.observe(root,{childList:true,subtree:true,attributes:true,attributeFilter:['class','hidden','open']});
    }else if(!active&&enabled){
        enabled=false;observer?.disconnect();window.removeEventListener('scroll',scrolled,true);window.removeEventListener('resize',resized);
        if(frame)cancelAnimationFrame(frame);frame=0;clearTimeout(timer);clear();route='';
    }
    changed();
}
