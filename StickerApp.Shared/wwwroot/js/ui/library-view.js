const key = name => 'stikerin.view.' + name;
export function read(name) { try { return localStorage.getItem(key(name)) === 'list'; } catch { return false; } }
export function write(name, list) { try { localStorage.setItem(key(name), list ? 'list' : 'tiles'); } catch { } }
