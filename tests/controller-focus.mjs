import assert from 'node:assert/strict';import vm from 'node:vm';import{readFileSync}from'node:fs';
const source=readFileSync('tv/app.js','utf8');const buttons=['wilds','mycopunk'].map(id=>({isConnected:true,tagName:'BUTTON',dataset:{id},classList:{contains:c=>c==='game-tile',add(){},remove(){}},focus(){},click(){this.clicked=true;}}));
const document={activeElement:{tagName:'BODY'}};const elements={games:{querySelectorAll:()=>buttons},setup:{hidden:true},options:{hidden:true},modal:{hidden:true}};
const ctx={api:async path=>{ctx.lastApi=path;},document,desktop:true,lastNavigationAt:0,$:id=>elements[id],selectGame:id=>ctx.selected=id,connected:true};vm.createContext(ctx);
vm.runInContext(source.slice(source.indexOf('var controllerFocus = null'),source.indexOf('function message(')),ctx);
vm.runInContext(source.slice(source.indexOf('function handleRemoteKey('),source.indexOf("document.addEventListener('keydown', handleRemoteKey)")),ctx);
ctx.focusControl(buttons[0]);const key=keyCode=>ctx.handleRemoteKey({keyCode,controller:true,preventDefault(){}});
key(39);assert.equal(ctx.selected,'mycopunk');key(37);assert.equal(ctx.selected,'wilds');key(13);assert.equal(buttons[0].clicked,true);assert.equal(document.activeElement.tagName,'BODY');
console.log('PASS: controle seleciona e confirma mesmo quando o navegador mantÃ©m foco DOM no BODY.');

ctx.handleRemoteKey({keyCode:39,preventDefault(){}});assert.equal(ctx.selected,'mycopunk');key(37);
key(10009);assert.equal(ctx.lastApi,'/api/launcher/hide','bolinha oculta launcher PC sem abrir opções');
