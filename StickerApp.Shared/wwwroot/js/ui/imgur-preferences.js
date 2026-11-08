const key='stik-er-in.imgur-client-id';
export function read(){try{return localStorage.getItem(key)??'';}catch{return '';}}
export function save(value){if(value)localStorage.setItem(key,value);else localStorage.removeItem(key);}
