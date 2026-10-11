const {test}=require('node:test');
const assert=require('node:assert/strict');
const {AdobeBridge}=require('../dist/bridge.js');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');

test('connection retries after worker outage without asking for the key again',async()=>{
  let calls=0,delays=0;const messages=[];
  const bridge=new AdobeBridge({},'test',m=>messages.push(m),async url=>{calls++;if(calls===1)throw Error('worker offline');if(url.endsWith('/status'))return new Response('{}');bridge.stop();return new Response(null,{status:204});},async()=>{delays++;});
  await bridge.start();assert.equal(calls,3);assert.equal(delays,1);assert.ok(messages.some(m=>m.includes('automatycznie')&&m.includes('worker offline')));
});
test('bad credentials stop reconnecting instead of endlessly retrying',async()=>{
  let calls=0;const bridge=new AdobeBridge({},'test',()=>{},async()=>{calls++;return new Response('',{status:401});},async()=>{throw Error('must not retry');});
  await bridge.start();assert.equal(calls,1);
});
function panel({saved=null,file=null,setupError=null,connectionError=null,storageError=null}={}){
  const handlers={},calls=[];let lifecycle;
  class Bridge {constructor(_,key){calls.push(['key',key]);}async checkConnection(){calls.push(['check']);if(connectionError)throw Error(connectionError);}start(){calls.push(['start']);}stop(){calls.push(['stop']);}}
  const secureStorage={async getItem(){if(storageError)throw Error(storageError);return saved===null?new Uint8Array():new TextEncoder().encode(saved);},async setItem(name,value){calls.push(['stored',name,value]);}};
  const localFileSystem={async getFileForOpening(){return file;}};
  const context={document:{getElementById(id){return {addEventListener(_,handler){handlers[id]=handler;},set textContent(value){calls.push(['status',value]);}};}},
    require(name){if(name==='./dist/bridge.js')return {AdobeBridge:Bridge};if(name==='premierepro')return {};if(name==='uxp')return {entrypoints:{setup(value){
      if(typeof value.plugin.create!=='function')throw Error('create method is not defined for plugin.');
      if(typeof value.panels.ditPanel.create!=='function')throw Error('create method is not defined for panel.');
      if(setupError)throw Error(setupError);lifecycle=value;
    }},storage:{secureStorage,localFileSystem}};throw Error(name);}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../panel.js'),'utf8'),context);
  return {handlers,calls,lifecycle};
}
test('panel restores encrypted connection automatically, including repeated show events',async()=>{
  const p=panel({saved:'A'.repeat(64)});p.lifecycle.panels.ditPanel.show();await new Promise(setImmediate);p.lifecycle.panels.ditPanel.show();await new Promise(setImmediate);
  assert.equal(p.calls.filter(c=>c[0]==='start').length,1);assert.ok(p.calls.some(c=>c[0]==='key'&&c[1]==='A'.repeat(64)));
});
test('first pairing saves the key securely and deletes the transport file',async()=>{
  let deleted=false;const p=panel({file:{read:async()=>JSON.stringify({key:'B'.repeat(64)}),delete:async()=>{deleted=true;}}});await new Promise(setImmediate);await p.handlers.connect();
  assert.ok(p.calls.some(c=>c[0]==='stored'));assert.ok(deleted);assert.equal(p.calls.filter(c=>c[0]==='start').length,1);
  assert.ok(p.calls.findIndex(c=>c[0]==='check')<p.calls.findIndex(c=>c[0]==='stored'));
});

test('failed handshake preserves pairing file and exposes the real connection error',async()=>{
  let deleted=false;const p=panel({connectionError:'GET request cannot have body',file:{read:async()=>JSON.stringify({key:'B'.repeat(64)}),delete:async()=>{deleted=true;}}});
  await new Promise(setImmediate);await p.handlers.connect();
  assert.equal(deleted,false);assert.ok(!p.calls.some(c=>c[0]==='stored'||c[0]==='start'));
  assert.ok(p.calls.some(c=>c[0]==='status'&&c[1].includes('GET request cannot have body')));
});

test('UXP GET has no body property and native fetch is not invoked with bridge receiver',async()=>{
  const original=global.fetch;let calls=0;
  global.fetch=async function(url,options){assert.notEqual(this?.constructor?.name,'AdobeBridge');assert.equal(Object.hasOwn(options,'body'),false);calls++;return new Response('{}');};
  try{await new AdobeBridge({},'key',()=>{}).checkConnection();assert.equal(calls,1);}finally{global.fetch=original;}
});

test('connected panel displays queue blocking reason instead of empty waiting state',async()=>{
  const messages=[];const bridge=new AdobeBridge({},'key',m=>messages.push(m),async url=>{
    if(url.endsWith('/status'))return new Response(JSON.stringify({message:'Połączono z DIT. Kolejka wstrzymana'}));
    bridge.stop();return new Response(null,{status:204});
  });
  await bridge.start();assert.ok(messages.includes('Połączono z DIT. Kolejka wstrzymana'));
});
test('invalid pairing file never stores credentials or starts Adobe work',async()=>{
  const p=panel({file:{read:async()=>JSON.stringify({key:'bad'}),delete:async()=>{throw Error('must not delete');}}});await new Promise(setImmediate);await p.handlers.connect();
  assert.ok(!p.calls.some(c=>c[0]==='stored'||c[0]==='start'));
});
test('host lifecycle failure is visible and file picker remains usable',async()=>{
  const p=panel({setupError:'host failed',file:{read:async()=>JSON.stringify({key:'C'.repeat(64)}),delete:async()=>{}}});
  assert.ok(p.calls.some(c=>c[0]==='status'&&c[1].includes('host failed')));
  await p.handlers.connect();assert.ok(p.calls.some(c=>c[0]==='stored'));assert.ok(p.calls.some(c=>c[0]==='start'));
});

test('secure storage denial is distinguished from a network denial',async()=>{
  const p=panel({storageError:'Permission denied'});await new Promise(setImmediate);
  assert.ok(p.calls.some(c=>c[0]==='status'&&c[1].includes('odczytać zapisanego klucza')&&c[1].includes('Permission denied')));
  assert.ok(!p.calls.some(c=>c[0]==='check'));
});
test('network denial identifies the exact request without credentials',async()=>{
  const bridge=new AdobeBridge({},'B'.repeat(64),()=>{},async()=>{throw Error('Permission denied');});
  await assert.rejects(()=>bridge.checkConnection(),/Sieć GET \/adobe\/status: Permission denied/);
});
test('Premiere compatibility manifest is paired with fixed runtime origin',()=>{
  const manifest=JSON.parse(fs.readFileSync(path.join(__dirname,'../manifest.json'),'utf8'));
  assert.equal(manifest.requiredPermissions.network.domains,'all');
});
test('runtime refuses external paths before sending credentials and disables redirects',async()=>{
  const sent=[];const bridge=new AdobeBridge({},'key',()=>{},async(url,options)=>{sent.push(url);assert.equal(options.redirect,'error');return new Response('{}');});
  for(const path of ['https://example.com','//example.com','/adobe/../../secret','/adobe/status?redirect=https://example.com','/adobe/next\\evil'])await assert.rejects(()=>bridge.request(path),/Niedozwolona/);
  assert.equal(sent.length,0);await bridge.checkConnection();assert.deepEqual(sent,['http://127.0.0.1:47831/adobe/status']);
});
test('unexpected redirect response is rejected',async()=>{
  const bridge=new AdobeBridge({},'key',()=>{},async()=>({url:'https://example.com',status:200}));
  await assert.rejects(()=>bridge.checkConnection(),/przekierowanie/);
});

test('handshake diagnoses an active project even with paused queue without claiming work',async()=>{
  const messages=[],requests=[];const original=console.info;console.info=message=>messages.push(message);
  const projectPath='D:\\Montaż\\Projekty\\Dzień 2.prproj';
  try {
    const bridge=new AdobeBridge({Project:{getActiveProject:async()=>({path:projectPath})}},'secret',()=>{},async url=>{requests.push(url);return new Response(JSON.stringify({paused:true}));});
    await bridge.checkConnection();assert.deepEqual(requests,['http://127.0.0.1:47831/adobe/status']);
    assert.equal(messages.length,1);assert.ok(messages[0].includes(JSON.stringify(projectPath)));assert.ok(!messages[0].includes('secret'));
  } finally {console.info=original;}
});

test('project diagnostic failure does not invalidate an authenticated connection',async()=>{
  const messages=[];const original=console.warn;console.warn=message=>messages.push(message);
  try {
    const bridge=new AdobeBridge({Project:{getActiveProject:async()=>{throw Error('host not ready')}}},'secret',()=>{},async()=>new Response('{}'));
    await bridge.checkConnection();assert.equal(messages.length,1);assert.ok(messages[0].includes('host not ready'));
  } finally {console.warn=original;}
});
