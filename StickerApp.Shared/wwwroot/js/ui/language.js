const supported=['en','nl','de','ro'],key='stickers.language';
export function read(){let value='en';try{value=localStorage.getItem(key)||'en';}catch{}if(!supported.includes(value))value='en';document.documentElement.lang=value;return value;}
export function save(value){if(!supported.includes(value))throw new Error('Unsupported language.');localStorage.setItem(key,value);document.documentElement.lang=value;}
