// Runs before the stylesheet so saved appearance is applied before the first paint.
(() => {
    const appearanceKey = 'stickers.theme';
    const colorKey = 'stickers.color';
    const rainbowKey = 'stickers.rainbowMotion';
    const customKey = 'stickers.customColor';
    const systemTheme = window.matchMedia('(prefers-color-scheme: dark)');
    const appearances = ['system', 'light', 'dark'];
    const colors = ['green', 'blue', 'purple', 'rose', 'amber', 'rainbow', 'custom'];

    function readPreference(key, allowedValues, fallback) {
        try {
            const saved = localStorage.getItem(key);
            return allowedValues.includes(saved) ? saved : fallback;
        } catch {
            return fallback;
        }
    }

    let appearance = readPreference(appearanceKey, appearances, 'system');
    let color = readPreference(colorKey, colors, 'green');
    let rainbowMotion = readPreference(rainbowKey, ['true','false'], 'false') === 'true';

    function readCustom(){try{const value=localStorage.getItem(customKey)||'';return /^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value)?value:'#32875f';}catch{return '#32875f';}}
    let customColor = readCustom();
    let customLoaded = false;
    let rainbowLoaded = false;
    function applyPreferences() {
        const resolvedAppearance = systemTheme.matches ? 'dark' : 'light';
        document.documentElement.dataset.theme = appearance === 'system' ? resolvedAppearance : appearance;
        document.documentElement.dataset.color = color;
        document.documentElement.dataset.customColor = customColor;
        if(color === 'custom' || customLoaded) { customLoaded = true; void import('./custom-theme.js').then(module=>module.apply()).catch(()=>{}); }
        document.documentElement.dataset.rainbowMode = rainbowMotion ? "scroll" : "fixed";
        if(color === 'rainbow' || rainbowLoaded) {
            rainbowLoaded = true;
            void import('./rainbow-theme.js').then(module=>module.sync()).catch(()=>{});
        }
    }

    function savePreference(key, value, allowedValues) {
        if (!allowedValues.includes(value)) return false;

        try {
            localStorage.setItem(key, value);
            return true;
        } catch {
            return false;
        }
    }

    window.stickerTheme = {
        get: () => appearance,
        getColor: () => color,
        getCustomColor: () => customColor,
        setCustomColor(value) {
            if(!/^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value)) return false;
            try {localStorage.setItem(customKey,value.toLowerCase());}catch{return false;}
            customColor=value.toLowerCase();applyPreferences();return true;
        },
        getRainbowMotion: () => rainbowMotion,
        setRainbowMotion(value) {
            if(!savePreference(rainbowKey,String(!!value),['true','false'])) return false;
            rainbowMotion=!!value;applyPreferences();return true;
        },
        set(value) {
            if (!savePreference(appearanceKey, value, appearances)) return false;
            appearance = value;
            applyPreferences();
            return true;
        },
        setColor(value) {
            if (!savePreference(colorKey, value, colors)) return false;
            color = value;
            applyPreferences();
            return true;
        }
    };

    systemTheme.addEventListener('change', applyPreferences);
    window.addEventListener('storage', event => {
        if (event.key !== appearanceKey && event.key !== colorKey && event.key !== rainbowKey && event.key !== customKey && event.key !== null) return;
        appearance = readPreference(appearanceKey, appearances, 'system');
        color = readPreference(colorKey, colors, 'green');
        rainbowMotion = readPreference(rainbowKey, ['true','false'], 'false') === 'true';
        customColor = readCustom();
        applyPreferences();
    });

    applyPreferences();
})();

// Interface typography is a device preference; sticker canvas fonts are separate.
(() => {
    const key='stickers.typography';
    const mediaModule=new URL('../media/layers/text-layers.js',document.currentScript.src).href;
    const defaults={font:'system',scale:100,bold:false,spacing:0,lineSpacing:100};
    const families={system:"'Segoe UI', Arial, sans-serif",sans:'sans-serif',serif:'serif',mono:'monospace',meme:'StikMeme, Impact, sans-serif'};
    let current={...defaults},customFace=null,revision=0,previewIndex=0;
    const validFont=font=>Object.hasOwn(families,font)||/^custom-[a-f0-9]{32}$/.test(font);
    function normalize(value){
        const number=(v,min,max,fallback)=>Number.isFinite(Number(v))?Math.max(min,Math.min(max,Number(v))):fallback;
        return {font:validFont(value?.font)?value.font:'system',scale:number(value?.scale,85,150,100),bold:value?.bold===true,spacing:number(value?.spacing,-0.5,2,0),lineSpacing:number(value?.lineSpacing,90,150,100)};
    }
    function apply(value,fontReady=false){
        const root=document.documentElement;
        root.style.setProperty('--app-font-family',families[value.font]??(fontReady?'StikInterfaceCustom':families.system));
        root.style.setProperty('--app-font-scale',String(value.scale/100));
        root.style.setProperty('--app-letter-spacing',`${value.spacing}px`);
        root.style.setProperty('--app-line-spacing',String(value.lineSpacing/100));
        root.dataset.appBold=String(value.bold);
    }
    async function cacheFont(value){
        const db=await new Promise((resolve,reject)=>{const request=indexedDB.open('stik-interface-font',1);request.onupgradeneeded=()=>request.result.createObjectStore('fonts');request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});
        try{return await new Promise((resolve,reject)=>{const tx=db.transaction('fonts',value===undefined?'readonly':'readwrite'),store=tx.objectStore('fonts');let result;
            if(value===undefined){const request=store.get('active');request.onsuccess=()=>result=request.result;}
            else if(value===null)store.delete('active');else store.put(value,'active');
            tx.oncomplete=()=>resolve(result);tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error);
        });}finally{db.close();}
    }
    async function loadFont(value,data,version){
        if(value.font==='meme')await (await import(mediaModule)).loadTextFont('meme');
        else if(value.font.startsWith('custom-')){
            let saved;
            if(data){const blob=await (await fetch(data)).blob();saved={id:value.font,blob};}
            else saved=await cacheFont();
            if(saved?.id!==value.font)throw new Error('This interface font is unavailable. Choose it again or reset the font settings.');
            const face=new FontFace('StikInterfaceCustom',await saved.blob.arrayBuffer());await face.load();
            if(version!==revision)return;
            if(customFace)document.fonts.delete(customFace);customFace=face;document.fonts.add(face);
            if(data)await cacheFont(saved);
        }
        if(version===revision)apply(value,true);
    }
    try{current=normalize(JSON.parse(localStorage.getItem(key)||'null'));}catch{}
    apply(current);
    loadFont(current,null,revision).catch(()=>{});
    window.stickerTypography={
        get:()=>({...current}),
        nextPreview(count){
            if(!Number.isInteger(count)||count<1)return 0;
            try{const saved=Number(localStorage.getItem('stickers.font-preview-next'));if(Number.isInteger(saved)&&saved>=0)previewIndex=saved%count;}catch{}
            const selected=previewIndex;previewIndex=(selected+1)%count;
            try{localStorage.setItem('stickers.font-preview-next',String(previewIndex));}catch{}
            return selected;
        },
        validateFont:async data=>(await import(mediaModule)).validateTextFont(data),
        async set(value,fontData=null){
            const next=normalize(value),version=++revision;
            try{await loadFont(next,fontData,version);if(version!==revision)return;
                localStorage.setItem(key,JSON.stringify(next));current=next;
                if(!next.font.startsWith('custom-')){if(customFace){document.fonts.delete(customFace);customFace=null;}await cacheFont(null).catch(()=>{});}
            }catch(error){apply(current,!!customFace);throw error;}
        },
        async reset(){await window.stickerTypography.set({...defaults});}
    };
    window.addEventListener('storage',event=>{if(event.key!==key&&event.key!==null)return;try{current=normalize(JSON.parse(localStorage.getItem(key)||'null'));apply(current);loadFont(current,null,++revision).catch(()=>{});}catch{}});
})();
