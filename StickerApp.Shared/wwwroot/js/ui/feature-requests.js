const key='stickers.feature-request-drafts';
const categories=['feature','improvement','bug'];
function valid(item){return item && typeof item.id==='string' && /^[0-9a-f-]{36}$/i.test(item.id) && typeof item.title==='string' && item.title.length<=120 && typeof item.details==='string' && item.details.length<=3000 && categories.includes(item.category) && Number.isFinite(Date.parse(item.createdAt));}
export function read(){
    const data=JSON.parse(localStorage.getItem(key)||'[]');
    if(!Array.isArray(data)||data.length>50||!data.every(valid))throw new Error('Saved drafts could not be read. Export or recover your browser data before clearing storage.');
    return data;
}
export function save(draft){
    if(!draft?.title?.trim()||draft.title.trim().length>120||typeof draft.details!=='string'||draft.details.length>3000||!categories.includes(draft.category))throw new Error('Add a title up to 120 characters and details up to 3,000 characters.');
    const items=read(),index=items.findIndex(item=>item.id===draft.id);
    if(index<0&&items.length>=50)throw new Error('You can save up to 50 drafts. Export and remove an older draft first.');
    const item={id:index<0?crypto.randomUUID():items[index].id,title:draft.title.trim(),details:draft.details.trim(),category:draft.category,createdAt:index<0?new Date().toISOString():items[index].createdAt};
    if(index<0)items.unshift(item);else items[index]=item;
    localStorage.setItem(key,JSON.stringify(items));return items;
}
export function remove(id){const items=read().filter(item=>item.id!==id);localStorage.setItem(key,JSON.stringify(items));return items;}
