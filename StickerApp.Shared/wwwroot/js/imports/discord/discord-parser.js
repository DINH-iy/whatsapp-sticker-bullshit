import { inflateSync } from '../vendor/zip-reader.js';

export const MAX_JSON_BYTES = 32 * 1024 * 1024;
const MAX_COMPRESSED_BYTES = 16 * 1024 * 1024;
const MAX_FAVORITES = 5000;
const normalize = value => value.replace(/[_\s-]/g, '').toLowerCase();
const mediaHosts = /^(?:(?:[a-z0-9-]+\.)*tenor\.(?:com|co)|(?:static\d*|media\d*)\.klipy\.com|(?:[a-z0-9-]+\.)*giphy\.com|i\.imgur\.com|cdn\.discordapp\.com|media\.discordapp\.net|images-ext-[12]\.discordapp\.net)$/i;
export function safeUrl(value, media = false) {
    if (typeof value !== 'string' || value.length > 4096) return '';
    try {
        const url = new URL(value.startsWith('//') ? 'https:' + value : value);
        if (url.protocol !== 'https:' || url.username || url.password || url.port ||
            !(mediaHosts.test(url.hostname) || !media && /^(?:www\.)?(?:tenor\.com|klipy\.com|giphy\.com|imgur\.com|discord\.com)$/i.test(url.hostname))) return '';
        return url.href;
    } catch { return ''; }
}
function displayName(page, index) {
    try {
        const part = decodeURIComponent(new URL(page).pathname.split('/').filter(Boolean).at(-1) ?? '')
            .replace(/\.(gif|webp|png|jpe?g|mp4|webm)$/i, '').replace(/-?(?:gif-)?\d{6,}$/i, '').replace(/[-_]+/g, ' ').trim();
        return part ? part.slice(0, 96) : `Discord favourite ${index + 1}`;
    } catch { return `Discord favourite ${index + 1}`; }
}
export function extractFavorites(document) {
    const collections = [], seenObjects = new Set(); let visited = 0;
    function visit(value, depth = 0, key = '') {
        if (++visited > 100000 || depth > 24) throw new Error('This export is too complex. Choose account/user.json instead.');
        const name = normalize(key);
        if (typeof value === 'string' && ['settings','usersettings','data','frecency','favoritegifs','giffavorites','giffavoritesstore'].includes(name) && /^[\s]*[\[{]/.test(value)) {
            try { value = JSON.parse(value); } catch { return; }
        }
        if (!value || typeof value !== 'object' || seenObjects.has(value)) return;
        seenObjects.add(value);
        if (['favoritegifs','giffavorites','giffavoritesstore'].includes(name)) {
            collections.push(value.gifs ?? value.favorites ?? value); return;
        }
        if (depth === 0) {
            if (Array.isArray(value)) { collections.push(value); return; }
            if (value.gifs && (Array.isArray(value.gifs) || typeof value.gifs === 'object')) { collections.push(value.gifs); return; }
            if (Object.keys(value).some(key => safeUrl(key))) { collections.push(value); return; }
        }
        if (!Array.isArray(value)) {
            const entries=Object.entries(value).sort(([a],[b])=>Number(/settings|favorite|frecency/i.test(b))-Number(/settings|favorite|frecency/i.test(a)));
            for (const [childKey, child] of entries) { visit(child, depth + 1, childKey); if(collections.length)return; }
        }
    }
    visit(document);
    if (!collections.length) throw new Error('No GIF favourites section was found. Include Account data in your Discord export, or choose a favourites JSON file. Your messages and account details have not been imported.');
    const items = [], seen = new Set(); let skipped = 0, duplicateCount = 0;
    for (const collection of collections) {
        if (!collection || typeof collection !== 'object') continue;
        const entries = Array.isArray(collection) ? collection.map((value, index) => [String(index), value]) : Object.entries(collection);
        if (entries.length + items.length > MAX_FAVORITES) throw new Error('This export contains more than 5,000 favourites. Use a smaller favourites file.');
        for (const [key, value] of entries) {
            const metadata = value && typeof value === 'object' ? value : {};
            const page = safeUrl(metadata.url ?? metadata.pageUrl ?? (typeof value === 'string' ? value : key));
            const media = safeUrl(metadata.src ?? metadata.mediaUrl ?? metadata.media_url ?? page, true);
            if (!page && !media) { skipped++; continue; }
            const identity = media || page;
            if (seen.has(identity)) { duplicateCount++; continue; } seen.add(identity);
            const name = typeof metadata.title === 'string' && metadata.title.trim() ? metadata.title.trim().slice(0,96) : displayName(page || media, items.length);
            const video = /\.(?:mp4|webm)(?:\?|$)|\/(?:mp4|webm)(?:\?|$)/i.test(media) || metadata.format === 2 || metadata.format === 'VIDEO';
            items.push({ id: String(items.length), name, pageUrl: page || media, mediaUrl: media,
                video, order: Number.isFinite(metadata.order) ? metadata.order : items.length,
                problem: media ? null : 'This favourite has no supported direct media link. Open it in Discord to copy a fresh link.' });
        }
    }
    items.sort((a,b) => b.order-a.order);
    return { items, skipped, duplicateCount };
}
function crc32(bytes) {
    const table = new Uint32Array(256);
    for (let i=0;i<256;i++) { let n=i; for(let bit=0;bit<8;bit++)n=n&1?0xedb88320^(n>>>1):n>>>1;table[i]=n; }
    let crc=0xffffffff;for(const byte of bytes)crc=table[(crc^byte)&255]^(crc>>>8);return (crc^0xffffffff)>>>0;
}
async function readJsonFile(file) {
    if(file.size>MAX_JSON_BYTES)throw new Error('Account JSON files can be at most 32 MB.');
    return new Uint8Array(await file.arrayBuffer());
}
async function readAccountZip(file) {
    // Read slices, not the whole archive: messages and attachments stay on disk.
    const tailOffset=Math.max(0,file.size-65557),tail=new Uint8Array(await file.slice(tailOffset).arrayBuffer()),view=new DataView(tail.buffer);
    let end=-1;
    for(let i=tail.length-22;i>=0;i--)if(view.getUint32(i,true)===0x06054b50 && i+22+view.getUint16(i+20,true)===tail.length){end=i;break;}
    if(end<0)throw new Error('This is not a complete ZIP file. Download Discord’s package.zip again.');
    const count=view.getUint16(end+10,true),length=view.getUint32(end+12,true),offset=view.getUint32(end+16,true);
    if(view.getUint16(end+4,true)||view.getUint16(end+6,true)||view.getUint16(end+8,true)!==count||count===65535||length===0xffffffff||offset===0xffffffff)
        throw new Error('This ZIP uses a multi-part or ZIP64 format. Choose account/user.json from it instead.');
    if(length>32*1024*1024||offset+length>tailOffset+end)throw new Error('This ZIP directory is invalid or too large.');
    const directory=new Uint8Array(await file.slice(offset,offset+length).arrayBuffer()),data=new DataView(directory.buffer),decoder=new TextDecoder();
    const candidates=[];let cursor=0;
    for(let i=0;i<count;i++) {
        if(cursor+46>directory.length||data.getUint32(cursor,true)!==0x02014b50)throw new Error('This ZIP directory is damaged.');
        const nameLength=data.getUint16(cursor+28,true),extra=data.getUint16(cursor+30,true),comment=data.getUint16(cursor+32,true),next=cursor+46+nameLength+extra+comment;
        if(next>directory.length)throw new Error('This ZIP directory is incomplete.');
        const name=decoder.decode(directory.subarray(cursor+46,cursor+46+nameLength)).replace(/\\/g,'/'),parts=name.toLowerCase().split('/');
        if(!name.startsWith('/')&&!parts.some(part=>part==='..'||part==='.')&&!name.includes(':')) {
            const account=parts.at(-1)==='user.json'&&(parts.length===1||parts.at(-2)==='account');
            const favorites=['discord-favorite-gifs.json','favorite_gifs.json','favorites.json','gifs_backup.json'].includes(parts.at(-1));
            if(account||favorites)candidates.push({priority:account?0:1,flags:data.getUint16(cursor+8,true),method:data.getUint16(cursor+10,true),crc:data.getUint32(cursor+16,true),compressed:data.getUint32(cursor+20,true),size:data.getUint32(cursor+24,true),local:data.getUint32(cursor+42,true)});
        }
        cursor=next;
    }
    candidates.sort((a,b)=>a.priority-b.priority);
    if(!candidates.length)throw new Error('This ZIP has no Account/user.json or favourites JSON. Request Discord’s Account data and choose the downloaded package.zip.');
    if(candidates.length>1&&candidates[0].priority===candidates[1].priority)throw new Error('This ZIP contains several account files. Choose the account/user.json you want instead.');
    const entry=candidates[0];
    if(entry.flags&1)throw new Error('Password-protected ZIP files are not supported.');
    if(entry.size>MAX_JSON_BYTES||entry.compressed>MAX_COMPRESSED_BYTES)throw new Error('The account JSON is too large. Use a favourites-only JSON export.');
    if(![0,8].includes(entry.method)||entry.local+30>file.size)throw new Error('This ZIP uses an unsupported compression format.');
    const header=new DataView(await file.slice(entry.local,entry.local+30).arrayBuffer());
    if(header.getUint32(0,true)!==0x04034b50||header.getUint16(8,true)!==entry.method)throw new Error('The account ZIP entry is damaged.');
    const start=entry.local+30+header.getUint16(26,true)+header.getUint16(28,true);
    if(start+entry.compressed>offset)throw new Error('The account ZIP entry is incomplete.');
    const compressed=new Uint8Array(await file.slice(start,start+entry.compressed).arrayBuffer());
    const bytes=entry.method===0?compressed:inflateSync(compressed,{out:new Uint8Array(entry.size)});
    if(bytes.length!==entry.size||crc32(bytes)!==entry.crc)throw new Error('The account JSON could not be verified. Download the ZIP again.');
    return bytes;
}
export async function parseDiscordExport(file) {
    if(!file||!Number.isFinite(file.size)||!file.size)throw new Error('Choose a Discord ZIP or JSON file.');
    const prefix=new Uint8Array(await file.slice(0,4).arrayBuffer());
    const zip=prefix[0]===80&&prefix[1]===75;
    let bytes=zip?await readAccountZip(file):await readJsonFile(file),document;
    try { document=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes).replace(/^\uFEFF/,'')); }
    catch { throw new Error('This file does not contain valid account or favourites JSON.'); }
    bytes=null;
    return extractFavorites(document);
}
