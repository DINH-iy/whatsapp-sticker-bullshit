// Only known sort/filter values are stored; searches and selection state stay temporary.
const imageSorts = ['default','oldest','name-asc','name-desc','type','favorites-first','layers-desc','groups-desc','duration-desc','duration-asc','file-desc','file-asc','storage-desc','storage-asc'];
const images = {
    sort: imageSorts, type: ['all','still','animated'], format: ['all','webp','png','jpeg','gif'],
    storage: ['all','small','medium','large'], editing: ['all','flat','layered','grouped','text'],
    spoiler: ['all','nsfw','safe'], favorites: ['all','yes','no'], membership: ['all','packed','unpacked']
};
const schemas = {
    library: { ...images, folder: ['all','none'] },
    'pack-stickers': { ...images, sort: [...imageSorts,'newest'] },
    packs: {
        sort: ['default','newest','name-asc','name-desc','publisher','updated-desc','updates-desc','public-first','group-first','solo-first','count-desc','count-asc','file-desc','file-asc','storage-desc','storage-asc'],
        type: ['all','still','animated','mixed','empty'], count: ['all','empty','few','ready','full'],
        storage: ['all','small','medium','large','huge'], ownership: ['all','group','solo'],
        visibility: ['all','public','private'], spoiler: ['all','nsfw','safe'], content: ['all','layered','grouped','text']
    }
};
const key = scope => 'stickers.browse.' + scope;
export function normalize(scope, value) {
    const schema = schemas[scope];
    if (!schema) throw new Error('Unknown browse preference scope.');
    const input = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
    return Object.fromEntries(Object.entries(schema).map(([name, allowed]) => {
        const selected = input[name];
        const folder = name === 'folder' && typeof selected === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(selected);
        return [name, allowed.includes(selected) || folder ? selected : allowed[0]];
    }));
}
export function read(scope) {
    try { return normalize(scope, JSON.parse(localStorage.getItem(key(scope)) || '{}')); }
    catch { return normalize(scope, {}); }
}
export function write(scope, value) {
    const safe = normalize(scope, value);
    try { localStorage.setItem(key(scope), JSON.stringify(safe)); } catch { /* Browsing still works when device storage is unavailable. */ }
}
