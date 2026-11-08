import { parseDiscordExport } from './discord-parser.js';
self.onmessage=async({data})=>{
    try { self.postMessage({result:await parseDiscordExport(data.file)}); }
    catch(error) { self.postMessage({error:error.message??'The Discord export could not be read.'}); }
};
