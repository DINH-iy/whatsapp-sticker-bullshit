import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {writeFileSync} from 'node:fs';
import {parseDiscordExport,extractFavorites,safeUrl} from '../../StickerApp.Shared/wwwroot/js/imports/discord/discord-parser.js';
const require=createRequire(import.meta.url),{zipSync,strToU8}=require('../gif/node_modules/fflate');
const account={email:'private@example.invalid',messages:['not favourites'],settings:{favoriteGifs:{gifs:{
 'https://tenor.com/view/happy-gif-123456':{src:'https://media.tenor.com/test.mp4',format:2,order:10},
 'https://tenor.com/view/smile-gif-123457':{src:'https://media.tenor.com/smile.gif',order:20},
 'https://tenor.com/view/duplicate-gif-123458':{src:'https://media.tenor.com/smile.gif',order:1},
 'https://evil.invalid/private':{src:'http://127.0.0.1/private'},
 'https://tenor.com/view/no-source-gif-123459':{src:'https://evil.invalid/private'}
}}}};
let passed=0; const check=(name,fn)=>{fn();console.log('PASS '+name);passed++;};
const parsed=extractFavorites(account);
check('extract only favourites, order and duplicates',()=>{assert.equal(parsed.items.length,3);assert.equal(parsed.items[0].name,'smile');assert.equal(parsed.duplicateCount,1);assert.equal(parsed.skipped,1);assert.equal(parsed.items[1].video,true);assert.ok(parsed.items[2].problem);});
check('personal fields never appear in result',()=>{assert.ok(!JSON.stringify(parsed).includes('private@example'));assert.ok(!JSON.stringify(parsed).includes('messages'));});
check('unsafe and lookalike media hosts rejected',()=>{for(const url of ['http://media.tenor.com/a','https://media.tenor.com.evil.test/a','https://x@media.tenor.com/a','https://127.0.0.1/a','https://media.tenor.com:444/a'])assert.equal(safeUrl(url,true),'');});
check('stringified settings and root backups supported',()=>{assert.equal(extractFavorites({settings:JSON.stringify(account.settings)}).items.length,3);assert.equal(extractFavorites(account.settings.favoriteGifs.gifs).items.length,3);assert.equal(extractFavorites({gifs:['https://media.tenor.com/a.gif']}).items.length,1);});
for(const level of [0,6]){
 const bytes=zipSync({'Account/user.json':strToU8(JSON.stringify(account)),'Messages/private.txt':strToU8('private message'.repeat(100000))},{level});
 const result=await parseDiscordExport(new File([bytes],'package.zip'));
 check('ZIP compression '+level+' reads Account/user.json',()=>assert.equal(result.items.length,3));
 if(level===6)writeFileSync('/private/tmp/stik-discord-export-fixture.zip',bytes);
}
await assert.rejects(()=>parseDiscordExport(new File([zipSync({'messages/private.json':strToU8('{}')})],'bad.zip')),/no Account/);passed++;
await assert.rejects(()=>parseDiscordExport(new File(['{bad json'],'user.json')),/valid account/);passed++;
check('unrelated account data is never interpreted as favourites',()=>assert.throws(()=>extractFavorites({email:'private@example.invalid',avatar:'https://media.tenor.com/a.gif'}),/No GIF/));
check('empty favourites stays empty',()=>assert.equal(extractFavorites({favoriteGifs:{gifs:{}}}).items.length,0));
const huge=new File([new Uint8Array(33*1024*1024)],'user.json');await assert.rejects(()=>parseDiscordExport(huge),/32 MB/);passed++;
const corrupt=zipSync({'Account/user.json':strToU8(JSON.stringify(account))},{level:0});corrupt[50]^=1;
await assert.rejects(()=>parseDiscordExport(new File([corrupt],'corrupt.zip')));passed++;
console.log(`Passed ${passed} Discord parser checks.`);
