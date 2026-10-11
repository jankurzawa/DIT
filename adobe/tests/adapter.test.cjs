const {test}=require('node:test');const assert=require('node:assert/strict');const {AdobeBridge,adobeTicks}=require('../dist/bridge.js');
function fixture({existing=false,conflict=false,wrongProject=false,video=false}={}){
  const calls=[];let locked=false;let imported=true;let rendered=false;const inserted=[];const handlers=new Map();
  const tick=t=>({ticks:t,seconds:Number(t)/254016000000,subtract(other){return tick((BigInt(this.ticks)-BigInt(other.ticks)).toString())}});
  const action=name=>{assert.equal(locked,true,'actions must be created under lockedAccess');return {name}};
  let proxyPath='';
  const clip={hasProxy:async()=>!!proxyPath,getProxyPath:async()=>proxyPath,name:'C0001.MOV',getMediaFilePath:async()=> 'E:\\Dzień 1\\Kamera\\C0001.MOV',createSetColorLabelAction:n=>action('color:'+n),attachProxy:async path=>{calls.push('attach');proxyPath=path;return true}};
  const item={getProjectItem:async()=>clip,getStartTime:async()=>tick('0'),getEndTime:async()=>tick(adobeTicks(10000000)),createMoveAction:delta=>({...action('move'),apply(){const start=BigInt(delta.ticks);item.getStartTime=async()=>tick(start.toString())}})};
  const track={getTrackItems:async()=>[...(existing?[item]:conflict?[{...item,getProjectItem:async()=>({...clip,getMediaFilePath:async()=> 'E:\\other.MOV'})}]:[]),...inserted],createSetNameAction:()=>action('track-name')};
  const root={getItems:async()=>imported?[clip]:[],createBinAction:()=>action('bin')};
  const sequence={name:'Dzień 1',getTimebase:async()=> '5080320000',getFrameSize:async()=>({width:3840,height:2160}),getAudioTrackCount:async()=>1,getAudioTrack:async()=>track,getVideoTrack:async()=>track};
  const project={path:wrongProject?'E:\\editor.prproj':'E:\\Dzień 1.prproj',getSequences:async()=>[sequence],getRootItem:async()=>root,lockedAccess:cb=>{locked=true;try{cb()}finally{locked=false}},executeTransaction:(cb)=>{cb({addAction:a=>{calls.push(a.name);a.apply?.()}});return true},save:async()=>{calls.push('save');return true},importFiles:async()=>{imported=true;return true}};
  const p={Project:{getActiveProject:async()=>project},FolderItem:{cast:()=>{throw Error('clip')}},ClipProjectItem:{cast:c=>c},TickTime:{createWithTicks:tick},Markers:{getMarkers:async owner=>({getMarkers:async()=>[],createAddMarkerAction:()=>action(owner===sequence?'sequence-marker':'clip-marker')})},SequenceEditor:{getEditor:()=>({createOverwriteItemAction:(source,start)=>({...action('insert'),apply(){inserted.push({getProjectItem:async()=>source,getStartTime:async()=>start,getEndTime:async()=>tick((BigInt(start.ticks)+254016000000n).toString())})}})})},Constants:{TrackItemType:{CLIP:1}},EncoderManager:{EVENT_RENDER_COMPLETE:'complete',EVENT_RENDER_ERROR:'error',EVENT_RENDER_CANCEL:'cancel',getManager:()=>({isAMEInstalled:true,launchEncoder:async()=>true,encodeProjectItem:async(_,path)=>{rendered=true;queueMicrotask(()=>handlers.get('complete')?.({outputFiles:[path]}));return true}})},EventManager:{addEventListener:(_,name,cb)=>handlers.set(name,cb),removeEventListener:(_,name)=>handlers.delete(name)}};
  p.OpenProjectOptions=class OpenProjectOptions {};
  p.CloseProjectOptions=class CloseProjectOptions {};
  let validates=0;
  const transport=async(url,options)=>{
    const path=new URL(url).pathname;
    if(path.endsWith('/prepare'))return new Response(JSON.stringify({exists:true,fromTemplate:false}));
    if(path.endsWith('/progress')||path.endsWith('/prepare-proxy'))return new Response('{}');
    if(path.endsWith('/proxy-path'))return new Response(JSON.stringify({path:'P:\\C0001_proxy.mov'}));
    if(path.endsWith('/validate-proxy'))return new Response(JSON.stringify({valid:rendered}));
    if(path.endsWith('/result')){calls.push('result:'+JSON.parse(options.body).state);return new Response('');}
    throw Error('Unexpected request '+url);
  };
  const asset={id:'a',logicalName:'C0001.MOV',main:{path:'E:\\Dzień 1\\Kamera\\C0001.MOV'},hash:'hash',media:{hasVideo:video,audioChannels:1,durationTicks:10000000},deviceId:'cam',proxyPath:null};
  const command={id:'batch:day',projectPath:'E:\\Dzień 1.prproj',sequenceName:'Dzień 1',assets:[asset],placements:[{assetId:'a',deviceId:'cam',trackName:'Kamera',audio:!video,trackIndex:0,startTicks:0,durationTicks:10000000,unresolved:true,audioChannels:1}],config:{proxyRoot:'P:\\',proxyPreset:'proxy.epr',sequencePreset:null,premiereTemplate:null,devices:[{id:'cam',name:'Kamera',audio:!video}]}};
  return {bridge:new AdobeBridge(p,'key',()=>{},transport),command,calls,track,clip,action,p,tick,inserted,transport,handlers};
}
test('imports placement with a clip marker and saves without sequence markers',async()=>{const f=fixture();await f.bridge.run(f.command);assert.ok(f.calls.includes('insert'));assert.ok(f.calls.includes('color:2'));assert.ok(f.calls.includes('clip-marker'));assert.ok(!f.calls.includes('sequence-marker'));assert.ok(f.calls.indexOf('save')<f.calls.indexOf('result:complete'));});
test('replay recognizes existing full path without duplicate placement',async()=>{const f=fixture({existing:true});await f.bridge.run(f.command);assert.ok(!f.calls.includes('insert'));});
test('foreign timeline conflict stops before destructive action',async()=>{const f=fixture({conflict:true});await assert.rejects(()=>f.bridge.run(f.command),/koliduje/);assert.deepEqual(f.calls,[]);});
test('wrong active project is refused',async()=>{const f=fixture({wrongProject:true});await assert.rejects(()=>f.bridge.run(f.command),/inny projekt/);assert.deepEqual(f.calls,[]);});
test('AME completion is correlated and independently validated before attach/save',async()=>{const f=fixture({video:true});await f.bridge.run(f.command);assert.ok(f.calls.indexOf('save')<f.calls.indexOf('attach'));assert.ok(f.calls.indexOf('attach')<f.calls.lastIndexOf('save'));assert.equal(f.calls.at(-1),'result:complete');});

test('native attach claiming success without a real proxy link cannot complete',async()=>{
  const f=fixture({video:true});f.clip.attachProxy=async()=>true;
  await assert.rejects(()=>f.bridge.run(f.command),/nie potwierdziło przypięcia/);assert.ok(!f.calls.includes('result:complete'));
});
test('a proxy attached to another path is replaced with the verified output',async()=>{
  const f=fixture({video:true});let path='P:/wrong.mov';f.clip.hasProxy=async()=>true;f.clip.getProxyPath=async()=>path;
  f.clip.attachProxy=async value=>{path=value;f.calls.push('attach');return true};
  await f.bridge.run(f.command);assert.equal(path,'P:\\C0001_proxy.mov');assert.equal(f.calls.at(-1),'result:complete');
});
test('replay reuses already attached proxy without attaching a second time',async()=>{
  const f=fixture({video:true});f.clip.hasProxy=async()=>true;f.clip.getProxyPath=async()=> 'p:/C0001_proxy.mov';
  await f.bridge.run(f.command);assert.ok(!f.calls.includes('attach'));assert.equal(f.calls.at(-1),'result:complete');
});
test('proxy link lost during save cannot be reported complete',async()=>{
  const f=fixture({video:true});const project=await f.p.Project.getActiveProject();const save=project.save;
  project.save=async()=>{if(f.calls.includes('attach'))f.clip.hasProxy=async()=>false;return save()};
  await assert.rejects(()=>f.bridge.run(f.command),/nie przetrwało zapisu/);assert.ok(!f.calls.includes('result:complete'));
});

test('missing AME completion event cannot stall a physically verified output',async()=>{
  const f=fixture({video:true});let rendered=false;
  f.p.EncoderManager.getManager=()=>({isAMEInstalled:true,launchEncoder:async()=>true,encodeProjectItem:async()=>{assert.ok(f.calls.includes('insert'));assert.ok(f.calls.includes('save'));rendered=true;return true}});
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>url.endsWith('/validate-proxy')?new Response(JSON.stringify({valid:rendered})):f.transport(url,options));
  await bridge.run(f.command);assert.ok(f.calls.includes('attach'));assert.equal(f.calls.at(-1),'result:complete');
});
test('job-only AME errors stop promptly while the timeline remains saved',async()=>{
  const f=fixture({video:true});f.p.EncoderManager.EVENT_RENDER_QUEUE='queue';
  f.p.EncoderManager.getManager=()=>({isAMEInstalled:true,launchEncoder:async()=>true,encodeProjectItem:async()=>{
    const queued={};Object.defineProperty(queued,'jobID',{value:'job-1'});f.handlers.get('queue')(queued);
    const failed={};Object.defineProperties(failed,{jobID:{value:'job-1'},inErrorNumber:{value:42}});f.handlers.get('error')(failed);return true;
  }});
  await assert.rejects(()=>new AdobeBridge(f.p,'key',()=>{},f.transport).run(f.command),/AME zgłosiło błąd 42/);
  assert.ok(f.calls.includes('insert'));assert.ok(f.calls.includes('save'));assert.ok(!f.calls.includes('attach'));assert.ok(!f.calls.includes('result:complete'));assert.equal(f.handlers.size,0);
});
test('host claiming transaction success without inserting media never completes',async()=>{
  const f=fixture();f.p.SequenceEditor.getEditor=()=>({createOverwriteItemAction:()=>f.action('silent-insert')});
  await assert.rejects(()=>f.bridge.run(f.command),/nie umieściło klipu na timeline/);assert.ok(!f.calls.includes('result:complete'));
});
test('all files are imported and their timeline saved before any encoding starts',async()=>{
  const f=fixture({video:true});const project=await f.p.Project.getActiveProject();const root=await project.getRootItem();
  const second={...f.clip,getMediaFilePath:async()=> 'E:/second.MOV'};const imported=[];const rendered=new Set();
  const bin={name:'Kamera',getItems:async()=>imported};root.getItems=async()=>[bin];f.p.FolderItem.cast=item=>{if(item===bin)return bin;throw Error('clip')};
  project.importFiles=async paths=>{f.calls.push('import:'+paths[0]);imported.push(paths[0]==='E:/second.MOV'?second:f.clip);return true};
  f.command.assets.push({...f.command.assets[0],id:'b',main:{path:'E:/second.MOV'}});f.command.placements.push({...f.command.placements[0],assetId:'b',startTicks:10000000});
  f.p.EncoderManager.getManager=()=>({isAMEInstalled:true,launchEncoder:async()=>true,encodeProjectItem:async(_,path)=>{
    assert.equal(f.calls.filter(c=>c.startsWith('import:')).length,2);assert.equal(f.calls.filter(c=>c==='insert').length,2);assert.ok(f.calls.includes('save'));rendered.add(path);return true;
  }});
  await new AdobeBridge(f.p,'key',()=>{},async(url,options)=>{
    const body=options.body?JSON.parse(options.body):{};
    if(url.endsWith('/proxy-path'))return new Response(JSON.stringify({path:'P:/'+body.assetId+'.mov'}));
    if(url.endsWith('/validate-proxy'))return new Response(JSON.stringify({valid:rendered.has(body.path)}));
    return f.transport(url,options);
  }).run(f.command);
  assert.equal(f.calls.filter(c=>c==='attach').length,2);assert.equal(f.calls.at(-1),'result:complete');
});

test('opening a day creates an empty project and UHD sequence from supplied preset',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();const sequence=(await project.getSequences())[0];
  f.command.assets=[];f.command.placements=[];f.p.Project.getActiveProject=async()=>null;
  f.p.Project.createProject=async path=>{assert.equal(path,f.command.projectPath);f.calls.push('create-project');return project};
  project.getSequences=async()=>[];project.createSequenceWithPresetPath=async(name,path)=>{assert.equal(name,'Dzień 1');assert.equal(path,'auto.sqpreset');f.calls.push('create-sequence');return sequence};
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:false,sequencePreset:'auto.sqpreset'})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')));
  await bridge.run(f.command);assert.deepEqual(f.calls.filter(x=>['create-project','create-sequence','save','result:complete'].includes(x)),['create-project','create-sequence','save','result:complete']);
});

test('days share one production project and replay only updates the selected sequence',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();const seed=(await project.getSequences())[0];
  project.path=f.command.projectPath='E:/Film.prproj';f.command.assets=[];f.command.placements=[];
  let active=null;const sequences=[];const touched=[];
  f.p.Project.getActiveProject=async()=>active;
  f.p.Project.createProject=async()=>{f.calls.push('create-project');return active=project};
  project.getSequences=async()=>sequences;
  project.createSequenceWithPresetPath=async name=>{
    const track={...f.track,createSetNameAction:()=>{touched.push(name);return f.action('track-name')}};
    const sequence={...seed,name,getAudioTrack:async()=>track,getVideoTrack:async()=>track};sequences.push(sequence);return sequence;
  };
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:!!active,sequencePreset:'auto.sqpreset'})):new Response(''));
  await bridge.run(f.command);await bridge.run({...f.command,id:'setup-day:2',sequenceName:'Dzień 2'});
  touched.length=0;await bridge.run({...f.command,id:'setup-day:2',sequenceName:'Dzień 2'});
  assert.equal(f.calls.filter(x=>x==='create-project').length,1);assert.deepEqual(sequences.map(s=>s.name),['Dzień 1','Dzień 2']);
  assert.ok(touched.length>0);assert.ok(touched.every(name=>name==='Dzień 2'));assert.equal(f.calls.filter(x=>x==='save').length,3);
});

test('template sequence gets the day name through its ProjectItem under locked access',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();const sequence=(await project.getSequences())[0];
  sequence.name='Szablon';sequence.getProjectItem=async()=>({createSetNameAction:name=>{assert.equal(name,'Dzień 1');return f.action('rename-sequence')}});
  f.command.assets=[];f.command.placements=[];
  const bridge=new AdobeBridge(f.p,'key',()=>{},async url=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:true,fromTemplate:true})):new Response(''));
  await bridge.run(f.command);assert.ok(f.calls.includes('rename-sequence'));assert.ok(f.calls.indexOf('rename-sequence')<f.calls.indexOf('save'));
});

test('new project path may settle before any sequence or media is touched',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];
  project.path='';f.p.Project.getActiveProject=async()=>null;f.p.Project.createProject=async()=>project;
  let waits=0;
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:false})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')),async ms=>{
    assert.equal(ms,100);assert.deepEqual(f.calls,[]);if(++waits===3)project.path=f.command.projectPath;
  });
  await bridge.run(f.command);assert.equal(waits,3);assert.equal(f.calls.at(-1),'result:complete');
});

test('new project using actual Adobe extended path is accepted without creating a duplicate',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];
  project.path='\\\\?\\'+f.command.projectPath;f.p.Project.getActiveProject=async()=>null;
  f.p.Project.createProject=async path=>{assert.equal(path,f.command.projectPath);f.calls.push('create-project');return project};
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:false})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')),async()=>{throw Error('equivalent path must match immediately')});
  await bridge.run(f.command);assert.equal(f.calls.filter(x=>x==='create-project').length,1);assert.equal(f.calls.at(-1),'result:complete');
});

test('switching from an owned project with extended path saves and closes the correct file',async()=>{
  const f=fixture();const target=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];
  const previous={path:'\\\\?\\E:\\Dzień 0.prproj',save:async()=>{f.calls.push('save-previous');return true},close:async()=>{f.calls.push('close-previous');return true}};
  f.p.Project.getActiveProject=async()=>previous;f.p.Project.open=async()=>{f.calls.push('open-next');return target};
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:true,ownedProjects:['E:/Dzień 0.prproj']})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')),async()=>{throw Error('owned path must match immediately')});
  await bridge.run(f.command);assert.deepEqual(f.calls.slice(0,3),['save-previous','close-previous','open-next']);assert.equal(f.calls.at(-1),'result:complete');
});

test('persistent project path mismatch reports both paths and never modifies project',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];
  project.path='E:\\different.prproj';f.p.Project.getActiveProject=async()=>null;f.p.Project.createProject=async()=>project;
  let waits=0;
  const bridge=new AdobeBridge(f.p,'key',()=>{},async()=>new Response(JSON.stringify({exists:false})),async()=>{waits++});
  await assert.rejects(()=>bridge.run(f.command),error=>error.message.includes(JSON.stringify(f.command.projectPath))&&error.message.includes(JSON.stringify(project.path)));
  assert.equal(waits,20);assert.deepEqual(f.calls,[]);
});

test('switching between owned day projects saves previous project before closing and opening next',async()=>{
  const f=fixture();const target=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];
  const previous={path:'E:/Dzień 0.prproj',save:async()=>{f.calls.push('save-previous');return true},close:async()=>{f.calls.push('close-previous');return true}};
  f.p.Project.getActiveProject=async()=>previous;f.p.Project.open=async path=>{assert.equal(path,f.command.projectPath);f.calls.push('open-next');return target};
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:true,ownedProjects:[previous.path]})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')));
  await bridge.run(f.command);assert.deepEqual(f.calls.slice(0,3),['save-previous','close-previous','open-next']);assert.equal(f.calls.at(-1),'result:complete');
});

test('active project path settles before deciding that it belongs to another editor',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];project.path='';
  let waits=0;
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:true,ownedProjects:[]})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')),async()=>{
    assert.deepEqual(f.calls,[]);if(++waits===3)project.path=f.command.projectPath;
  });
  await bridge.run(f.command);assert.equal(waits,3);assert.equal(f.calls.at(-1),'result:complete');
});

test('owned project with delayed path closes with explicit Adobe options before opening next',async()=>{
  const f=fixture();const target=await f.p.Project.getActiveProject();f.command.assets=[];f.command.placements=[];
  const previous={path:'',save:async()=>{f.calls.push('save-previous');return true},close:async options=>{assert.ok(options instanceof f.p.CloseProjectOptions);f.calls.push('close-previous');return true}};
  f.p.Project.getActiveProject=async()=>previous;f.p.Project.open=async(path,options)=>{assert.equal(path,f.command.projectPath);assert.ok(options instanceof f.p.OpenProjectOptions);f.calls.push('open-next');return target};
  let waits=0;
  const bridge=new AdobeBridge(f.p,'key',()=>{},async(url,options)=>new URL(url).pathname.endsWith('/prepare')?new Response(JSON.stringify({exists:true,ownedProjects:['E:/Dzień 0.prproj']})):(f.calls.push('result:'+JSON.parse(options.body).state),new Response('')),async()=>{
    assert.deepEqual(f.calls,[]);if(++waits===3)previous.path='E:/Dzień 0.prproj';
  });
  await bridge.run(f.command);assert.equal(waits,3);assert.deepEqual(f.calls.slice(0,3),['save-previous','close-previous','open-next']);assert.equal(f.calls.at(-1),'result:complete');
});

test('unknown active project path never authorizes closing or saving it',async()=>{
  const f=fixture();const project=await f.p.Project.getActiveProject();project.path=undefined;
  let waits=0;
  const bridge=new AdobeBridge(f.p,'key',()=>{},async()=>new Response(JSON.stringify({exists:true,ownedProjects:[f.command.projectPath]})),async()=>{waits++});
  await assert.rejects(()=>bridge.run(f.command),error=>error.message.includes('Adobe zwróciło: null'));assert.equal(waits,20);assert.deepEqual(f.calls,[]);
});

test('missing adaptive preset waits before modifying a project',async()=>{
  const f=fixture();const bridge=new AdobeBridge(f.p,'key',()=>{},async()=>new Response(JSON.stringify({waitingReason:'Wymagany preset adaptacyjny'})));
  await assert.rejects(()=>bridge.run(f.command),error=>error.name==='Error'&&/preset adaptacyjny/.test(error.message));assert.deepEqual(f.calls,[]);
});

test('late synchronized clip moves unresolved tail and migrates its legacy marker',async()=>{
  const f=fixture();const tail={...f.clip,name:'C0002.MOV',getMediaFilePath:async()=> 'E:\\Dzień 1\\Kamera\\C0002.MOV'};
  const item={getProjectItem:async()=>tail,getStartTime:async()=>f.tick('0'),getEndTime:async()=>f.tick(adobeTicks(10000000)),createMoveAction:delta=>({...f.action('move-tail'),apply(){item.getStartTime=async()=>f.tick(delta.ticks)}})};
  f.track.getTrackItems=async()=>[item,...f.inserted];
  f.command.assets.push({...f.command.assets[0],id:'tail',logicalName:'C0002.MOV',main:{path:await tail.getMediaFilePath()}});
  f.command.placements[0].unresolved=false;
  f.command.placements.push({...f.command.placements[0],assetId:'tail',startTicks:10000000,unresolved:true});
  const marker={getName:async()=> 'DO SYNCHRONIZACJI / tail'};
  f.p.Markers.getMarkers=async owner=>({getMarkers:async()=>owner===tail?[]:owner===f.clip?[]:[marker],createRemoveMarkerAction:()=>f.action('remove-marker'),createAddMarkerAction:()=>f.action('clip-marker')});
  // The tail is already imported in the project, as it is on the timeline.
  const project=await f.p.Project.getActiveProject();const root=await project.getRootItem();root.getItems=async()=>[f.clip,tail];
  await f.bridge.run(f.command);
  assert.ok(f.calls.indexOf('move-tail')<f.calls.indexOf('insert'));assert.ok(f.calls.includes('remove-marker'));assert.ok(f.calls.includes('clip-marker'));
});

test('manual correction cannot move an existing clip over a foreign clip',async()=>{
  const f=fixture({existing:true});f.command.assets[0].syncMethod='manual-admin';f.command.placements[0].startTicks=20000000;
  const own=(await f.track.getTrackItems())[0];const foreign={...own,getProjectItem:async()=>({...f.clip,getMediaFilePath:async()=> 'E:\\editor.MOV'}),getStartTime:async()=>f.tick(adobeTicks(20000000)),getEndTime:async()=>f.tick(adobeTicks(30000000))};
  f.track.getTrackItems=async()=>[own,foreign];await assert.rejects(()=>f.bridge.run(f.command),/koliduje/);assert.deepEqual(f.calls,[]);
});

test('resolving a previously marked clip moves it and removes yellow marker',async()=>{
  const f=fixture({existing:true});f.command.placements[0].unresolved=false;f.command.placements[0].startTicks=20000000;
  f.p.Markers.getMarkers=async()=>({getMarkers:async()=>[{getName:async()=> 'DO SYNCHRONIZACJI / a'}],createRemoveMarkerAction:()=>f.action('remove-marker')});
  await f.bridge.run(f.command);assert.ok(f.calls.includes('move'));assert.ok(f.calls.includes('remove-marker'));assert.ok(f.calls.includes('color:0'));
});


test('clip marker replay does not duplicate markers and permits subsequent synchronization',async()=>{
  const f=fixture({existing:true});const own={getName:async()=> 'DO SYNCHRONIZACJI / a'};const editor={getName:async()=> 'Uwagi monta�ysty'};
  f.p.Markers.getMarkers=async owner=>({getMarkers:async()=>owner===f.clip?[own,editor]:[editor],createAddMarkerAction:()=>{throw Error('duplicate marker')},createRemoveMarkerAction:marker=>{assert.equal(marker,own);return f.action('remove-clip-marker')}});
  await f.bridge.run(f.command);assert.ok(!f.calls.includes('remove-clip-marker'));
  f.command.placements[0].unresolved=false;f.command.placements[0].startTicks=20000000;
  await f.bridge.run(f.command);assert.ok(f.calls.includes('move'));assert.ok(f.calls.includes('remove-clip-marker'));
});

test('migration removes all matching legacy markers but preserves editor markers',async()=>{
  const f=fixture();const own={getName:async()=> 'DO SYNCHRONIZACJI / a'};const editor={getName:async()=> 'DO SYNCHRONIZACJI / editor'};
  f.p.Markers.getMarkers=async owner=>({getMarkers:async()=>owner===f.clip?[]:[own,own,editor],createAddMarkerAction:()=>f.action('clip-marker'),createRemoveMarkerAction:marker=>{assert.equal(marker,own);return f.action('remove-legacy')}});
  await f.bridge.run(f.command);assert.equal(f.calls.filter(c=>c==='remove-legacy').length,2);assert.equal(f.calls.filter(c=>c==='clip-marker').length,1);
});
