const key='stickers.app-guide-seen';
export function hasSeen(){return localStorage.getItem(key)==='1';}
export function markSeen(){localStorage.setItem(key,'1');}
