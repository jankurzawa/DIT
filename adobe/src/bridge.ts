/* Host objects are supplied by Adobe; all worker payloads have explicit local types. */
type Host = any;
interface Asset {id:string;logicalName:string;main:{path:string};hash:string;media:{hasVideo:boolean;audioChannels:number;durationTicks:number};deviceId:string;proxyPath:string|null;}
interface Placement {assetId:string;deviceId:string;trackName:string;audio:boolean;trackIndex:number;startTicks:number;durationTicks:number;unresolved:boolean;audioChannels:number;}
interface Command {id:string;runId?:string;projectPath:string;sequenceName:string;assets:Asset[];placements:Placement[];config:{proxyRoot:string;proxyPreset:string|null;sequencePreset:string|null;premiereTemplate:string|null;devices:{id:string;name:string;audio:boolean}[]};}
export function adobeTicks(ticks:number):string {
  if(!Number.isSafeInteger(ticks)||ticks<0)throw new Error("Niepoprawna pozycja klipu.");
  return (BigInt(ticks)*254016n/10n).toString();
}
export function normalize(path:string):string {
  let result=path.replace(/\\/g,"/");
  // Premiere createProject returns Windows extended-length paths (\\?\D:\...),
  // while open/getActiveProject may return their conventional spelling.
  // Only DOS drive and UNC forms have a conventional equivalent here.
  if(/^\/\/\?\/[A-Za-z]:\//.test(result))result=result.slice(4);
  else if(/^\/\/\?\/UNC\//i.test(result))result="//"+result.slice(8);
  return result.toLowerCase();
}
export function validatePlacements(items:Placement[]):void {
  for(const item of items){if(!Number.isSafeInteger(item.durationTicks)||item.durationTicks<=0)throw new Error("Nieznany czas klipu.");adobeTicks(item.startTicks);}
  for(const group of new Set(items.map(i=>i.deviceId))){const sorted=items.filter(i=>i.deviceId===group).sort((a,b)=>a.startTicks-b.startTicks);for(let i=1;i<sorted.length;i++){if(sorted[i].startTicks<sorted[i-1].startTicks+sorted[i-1].durationTicks)throw new Error("Kolizja klipów urządzenia.");}}
}
export class AdobeWaitingError extends Error {}
class AuthorizationError extends Error {}
export class AdobeBridge {
  private active=false;
  private running=false;
  private runId:string|null=null;
  constructor(private p:Host,private key:string,private log:(text:string)=>void,private transport:typeof fetch=(url,options)=>fetch(url,options),private delay:(ms:number)=>Promise<void>=ms=>new Promise(resolve=>setTimeout(resolve,ms))){}
  async checkConnection():Promise<void> {
    await this.request("/adobe/status");
    // Read-only diagnostics also work while the worker queue is paused.
    try {const project=await this.p.Project.getActiveProject();console.info("DIT: aktywny projekt "+JSON.stringify(project?.path??null));}
    catch(error){console.warn("DIT: odczyt aktywnego projektu: "+String(error));}
  }
  stop():void {this.active=false;this.log("Panel wstrzymany. Trwające wywołanie Adobe kończy bieżący etap.");}
  async start():Promise<void> {
    if(this.active||this.running)return;this.active=true;
    while(this.active){
      let command:Command|undefined;
      try{
        const state=await this.request("/adobe/status");
        command=await this.request("/adobe/next");
        if(command){this.log("Przygotowywanie projektu: "+command.projectPath);await this.run(command);this.log("Zapisano: "+command.projectPath);}else this.log(state.message??"Połączono z DIT. Oczekiwanie na zadania.");
      }catch(error){
        const message=error instanceof Error?error.message:String(error);this.log(message);
        if(command){
          const waiting=error instanceof AdobeWaitingError;
          if(!waiting)this.active=false;
          try{await this.request("/adobe/"+encodeURIComponent(command.id)+"/result",{state:waiting?"waiting":"failed",error:message});}catch{this.active=false;/* A disconnected leased run is recovered by a newly connected panel after expiry. */}
        }else if(error instanceof AuthorizationError)this.active=false;
        else this.log("Nie można połączyć z DIT (127.0.0.1:47831): "+message+". Uruchom DIT. Próba zostanie ponowiona automatycznie.");
      }
      if(this.active)await this.delay(2000);
    }
  }
  private async request(path:string,body?:unknown):Promise<any> {
    // Premiere 26.x rejects explicit loopback manifest domains. The manifest uses the
    // supported compatibility declaration; this adapter always targets this fixed worker.
    if(!/^\/adobe\/[A-Za-z0-9%_:/.-]+$/.test(path)||path.includes("..")||path.includes("//"))throw new Error("Niedozwolona ścieżka interfejsu Adobe.");
    // UXP rejects GET requests containing a body property, even when its value is undefined.
    const options:RequestInit={method:body===undefined?"GET":"POST",redirect:"error",headers:{"X-DIT-Adobe":this.key,"Content-Type":"application/json"}};
    if(this.runId)(options.headers as Record<string,string>)["X-DIT-Run"]=this.runId;
    if(body!==undefined)options.body=JSON.stringify(body);
    let response:Response;
    try {response=await this.transport("http://127.0.0.1:47831"+path,options);}
    catch(error){throw new Error("Sieć "+options.method+" "+path+": "+(error instanceof Error?error.message:String(error)));}
    if(response.url&&response.url!=="http://127.0.0.1:47831"+path)throw new Error("DIT odrzucił przekierowanie poza docelowy interfejs.");
    if(response.status===204)return undefined;
    if(response.status===401||response.status===403)throw new AuthorizationError("Połączenie wygasło. Wybierz ponownie plik połączenia w panelu DIT.");
    if(!response.ok)throw new Error("DIT: "+await response.text());
    const text=await response.text();return text?JSON.parse(text):undefined;
  }
  private transaction(project:Host,build:(compound:Host)=>void,label:string):void {
    let success=false;
    project.lockedAccess(()=>{success=project.executeTransaction((compound:Host)=>build(compound),label);});
    if(!success)throw new Error("Adobe nie zatwierdziło operacji: "+label);
  }
  private async clips(folder:Host):Promise<Host[]> {
    const result:Host[]=[];
    for(const item of await folder.getItems()){
      let child:Host;try{child=this.p.FolderItem.cast(item);}catch{/* clip */}
      if(child)result.push(...await this.clips(child));else {try{result.push(this.p.ClipProjectItem.cast(item));}catch{/* Unsupported project item. */}}
    }
    return result;
  }
  private async projectPath(project:Host,expected:string[]):Promise<string|null> {
    if(!project)return null;
    // Host properties can still be stale after getActiveProject, not only after open/create.
    let path:string|null=null;
    for(let attempt=0;attempt<=20;attempt++){
      const value=project.path;path=typeof value==="string"?value:null;
      if(path!==null&&expected.some(candidate=>normalize(candidate)===normalize(path!)))return path;
      if(attempt<20)await this.delay(100);
    }
    return path;
  }
  private async progress(command:Command,stage:string,completed:number,total:number,assetId?:string,percent=0,jobId?:string,event?:string):Promise<void>{
    await this.request("/adobe/"+encodeURIComponent(command.id)+"/progress",{stage,completed,total,assetId:assetId??null,percent,updatedAt:new Date().toISOString(),jobId:jobId??null,event:event??null});
  }
  async run(command:Command):Promise<void> {
    if(this.running)throw new Error("Panel już wykonuje zadanie Adobe.");
    this.running=true;this.runId=command.runId??null;
    const heartbeat=this.runId?setInterval(()=>{void this.request("/adobe/"+encodeURIComponent(command.id)+"/heartbeat",{}).catch(error=>{console.warn("DIT heartbeat: "+String(error));/* The next awaited stage request enforces ownership; a late heartbeat after completion must not stop the next job. */});},5000):null;
    try{await this.execute(command);}finally{if(heartbeat!==null)clearInterval(heartbeat);this.running=false;/* Keep run ID until start() has reported any failure. */}
  }
  private async execute(command:Command):Promise<void> {
    validatePlacements(command.placements);
    if(!command.config.proxyPreset&&command.assets.some(a=>a.media?.hasVideo))throw new AdobeWaitingError("Wskaż sprawdzony preset proxy AME w konfiguracji DIT.");
    const prepared=await this.request("/adobe/"+encodeURIComponent(command.id)+"/prepare",{});
    if(prepared.waitingReason)throw new AdobeWaitingError(prepared.waitingReason);
    if(prepared.assets){command.assets=prepared.assets;command.placements=prepared.placements;validatePlacements(command.placements);}
    let project=await this.p.Project.getActiveProject();
    let path=await this.projectPath(project,[command.projectPath,...(prepared.ownedProjects??[])]);
    if(!project||path===null||normalize(path)!==normalize(command.projectPath)){
      if(project){
        if(path===null||!(prepared.ownedProjects??[]).some((owned:string)=>normalize(owned)===normalize(path!)))throw new AdobeWaitingError("Otwarty jest inny projekt montażysty. Zamknij go; panel wznowi pracę automatycznie. Adobe zwróciło: "+JSON.stringify(path)+"; projekty DIT: "+JSON.stringify(prepared.ownedProjects??[]));
        if(!await project.save()||!await project.close(new this.p.CloseProjectOptions()))throw new Error("Nie potwierdzono zapisu i zamknięcia poprzedniego projektu DIT.");
      }
      project=prepared.exists?await this.p.Project.open(command.projectPath,new this.p.OpenProjectOptions()):await this.p.Project.createProject(command.projectPath);
      path=await this.projectPath(project,[command.projectPath]);
    }
    if(!project||path===null||normalize(path)!==normalize(command.projectPath))throw new Error("Niezgodny projekt docelowy. Oczekiwano: "+JSON.stringify(command.projectPath)+"; Adobe zwróciło: "+JSON.stringify(path));
    let sequences=await project.getSequences();
    let sequence=sequences.find((s:Host)=>s.name===command.sequenceName);
    if(!sequence){
      if(sequences.length===1&&prepared.fromTemplate){
        sequence=sequences[0];
        const item=await sequence.getProjectItem();
        this.transaction(project,c=>c.addAction(item.createSetNameAction(command.sequenceName)),"DIT: nazwa sekwencji dnia");
      }
      else if(command.config.sequencePreset||prepared.sequencePreset)sequence=await project.createSequenceWithPresetPath(command.sequenceName,command.config.sequencePreset??prepared.sequencePreset);
      else throw new AdobeWaitingError("Wymagany szablon projektu lub preset sekwencji UHD 50 fps.");
    }
    if(BigInt(await sequence.getTimebase())!==5080320000n)throw new Error("Sekwencja musi mieć 50 fps.");
    const rect=await sequence.getFrameSize();
    if((rect.width??rect.right-rect.left)!==3840||(rect.height??rect.bottom-rect.top)!==2160)throw new Error("Sekwencja musi mieć rozmiar UHD.");
    const root=await project.getRootItem();
    const imported=new Map<string,Host>();
    for(const clip of await this.clips(root))imported.set(normalize(await clip.getMediaFilePath()),clip);
    const proxies:Record<string,string>={};let importedCount=0;
    for(const asset of command.assets){
      this.log("Import "+(++importedCount)+"/"+command.assets.length+": "+asset.logicalName);
      await this.progress(command,"import",importedCount-1,command.assets.length,asset.id);
      let clip=imported.get(normalize(asset.main.path));
      if(!clip){
        const device=command.config.devices.find(d=>d.id===asset.deviceId)!;
        let bin=(await root.getItems()).find((i:Host)=>i.name===device.name);
        if(!bin){this.transaction(project,c=>c.addAction(root.createBinAction(device.name,false)),"DIT: folder urządzenia");bin=(await root.getItems()).find((i:Host)=>i.name===device.name);}
        if(!bin||!await project.importFiles([asset.main.path],true,bin,false))throw new Error("Nie udało się zaimportować "+asset.logicalName);
        // Match full paths rather than relying on potentially repeated filenames.
        for(const item of await this.clips(root))if(normalize(await item.getMediaFilePath())===normalize(asset.main.path)){clip=item;break;}
        if(!clip)throw new Error("Nie znaleziono zaimportowanego pliku.");imported.set(normalize(asset.main.path),clip);
      }
    }
    const editor=this.p.SequenceEditor.getEditor(sequence);
    // Templates provide adaptive audio tracks. Refuse insufficient track layouts instead of downmixing.
    if(await sequence.getAudioTrackCount()<command.config.devices.length)throw new AdobeWaitingError("Szablon wymaga osobnej ścieżki audio dla każdego urządzenia.");
    const namedTracks:{track:Host;name:string}[]=[];
    let videoIndex=0;
    for(let audioIndex=0;audioIndex<command.config.devices.length;audioIndex++){
      const device=command.config.devices[audioIndex];namedTracks.push({track:await sequence.getAudioTrack(audioIndex),name:device.name});
      if(!device.audio)namedTracks.push({track:await sequence.getVideoTrack(videoIndex++),name:device.name});
    }
    if(namedTracks.some(t=>!t.track))throw new AdobeWaitingError("Preset ma za mało ścieżek dla urządzeń produkcji.");
    const markers=await this.p.Markers.getMarkers(sequence);
    const existingMarkers=await markers.getMarkers([]);
    const actions:((compound:Host)=>void)[]=[];
    const moves:{target:number;delta:number;action:(compound:Host)=>void}[]=[];
    for(const placement of command.placements){
      const asset=command.assets.find(a=>a.id===placement.assetId)!;
      const clip=imported.get(normalize(asset.main.path));
      const title="DO SYNCHRONIZACJI / "+asset.id;
      let marker:Host;
      for(const candidate of existingMarkers)if(await candidate.getName()===title){marker=candidate;break;}
      const clipMarkers=await this.p.Markers.getMarkers(clip);
      const ownedClipMarkers:Host[]=[];
      for(const candidate of await clipMarkers.getMarkers([]))if(await candidate.getName()===title)ownedClipMarkers.push(candidate);
      const audioIndex=command.config.devices.findIndex(d=>d.id===placement.deviceId);
      const videoTrack=placement.audio?null:await sequence.getVideoTrack(placement.trackIndex);
      const audioTrack=await sequence.getAudioTrack(audioIndex);
      const primaryTrack=placement.audio?audioTrack:videoTrack;
      if(!primaryTrack)throw new Error("Brak ścieżki w szablonie.");
      const existing=(await primaryTrack.getTrackItems(this.p.Constants.TrackItemType.CLIP,false));
      let same:Host;
      for(const item of existing){const source=this.p.ClipProjectItem.cast(await item.getProjectItem());if(normalize(await source.getMediaFilePath())===normalize(asset.main.path)){if(same)throw new Error("Powielony klip na ścieżce. Wymagana kontrola administratora.");same=item;}}
      const start=this.p.TickTime.createWithTicks(adobeTicks(placement.startTicks));
      const safetyItems=videoTrack?[...existing,...await audioTrack.getTrackItems(this.p.Constants.TrackItemType.CLIP,false)]:existing;
      for(const item of safetyItems){
        const source=this.p.ClipProjectItem.cast(await item.getProjectItem());const path=normalize(await source.getMediaFilePath());
        if(path===normalize(asset.main.path))continue;
        const owned=command.assets.find(a=>normalize(a.main.path)===path);
        const planned=owned&&command.placements.find(p=>p.assetId===owned.id);
        // Compare final positions of known DIT clips; all moves execute before inserts.
        const s=planned?planned.startTicks:Number((await item.getStartTime()).seconds)*10000000,e=planned?s+planned.durationTicks:Number((await item.getEndTime()).seconds)*10000000;
        if(placement.startTicks<e&&placement.startTicks+placement.durationTicks>s)throw new Error("Istniejący klip koliduje z nowym. Wymagana kontrola administratora.");
      }
      if(same){
        const old=await same.getStartTime();
        if(old.ticks!==start.ticks){
          if(!placement.unresolved&&!marker&&!ownedClipMarkers.length&&!(asset as Asset & {syncMethod?:string}).syncMethod?.startsWith("manual"))throw new Error("Istniejąca pozycja zsynchronizowanego klipu różni się od planu.");
          const delta=start.subtract(old);const deltaValue=placement.startTicks-Number(BigInt(old.ticks)*10n/254016n);
          moves.push({target:placement.startTicks,delta:deltaValue,action:c=>c.addAction(same.createMoveAction(delta))});
          // Move matching linked camera audio by the same delta.
          if(videoTrack)for(const item of await audioTrack.getTrackItems(this.p.Constants.TrackItemType.CLIP,false)){const source=this.p.ClipProjectItem.cast(await item.getProjectItem());if(normalize(await source.getMediaFilePath())===normalize(asset.main.path))moves.push({target:placement.startTicks,delta:deltaValue,action:c=>c.addAction(item.createMoveAction(delta))});}
        }
      }else{
        actions.push(c=>c.addAction(editor.createOverwriteItemAction(this.p.ProjectItem?this.p.ProjectItem.cast(clip):clip,start,placement.audio?-1:placement.trackIndex,asset.media.audioChannels>0?audioIndex:-1)));
      }
      if(videoTrack)actions.push(c=>c.addAction(videoTrack.createSetNameAction(placement.trackName)));
      actions.push(c=>c.addAction(audioTrack.createSetNameAction(placement.trackName)));
      // Migrate only this asset's legacy DIT sequence marker. Editor markers stay intact.
      for(const candidate of existingMarkers)if(await candidate.getName()===title)actions.push(c=>c.addAction(markers.createRemoveMarkerAction(candidate)));
      if(placement.unresolved){
        actions.push(c=>c.addAction(clip.createSetColorLabelAction(2)));
        if(!ownedClipMarkers.length)actions.push(c=>c.addAction(clipMarkers.createAddMarkerAction(title,"Comment",this.p.TickTime.createWithTicks("0"),this.p.TickTime.createWithTicks("0"),"DIT: klip oczekuje na synchronizację. "+asset.logicalName)));
        for(const duplicate of ownedClipMarkers.slice(1))actions.push(c=>c.addAction(clipMarkers.createRemoveMarkerAction(duplicate)));
      }else if(marker||ownedClipMarkers.length){
        for(const candidate of ownedClipMarkers)actions.push(c=>c.addAction(clipMarkers.createRemoveMarkerAction(candidate)));
        actions.push(c=>c.addAction(clip.createSetColorLabelAction(0)));
      }
    }
    moves.sort((a,b)=>a.delta>0&&b.delta>0?b.target-a.target:a.delta<=0&&b.delta<=0?a.target-b.target:a.delta>0?-1:1);
    this.transaction(project,c=>{for(const {track,name} of namedTracks)c.addAction(track.createSetNameAction(name));for(const move of moves)move.action(c);for(const action of actions)action(c);},"DIT: aktualizacja materiału");
    // A successful transaction does not prove that the host actually placed media.
    for(const placement of command.placements){
      const asset=command.assets.find(a=>a.id===placement.assetId)!;
      const track=placement.audio?await sequence.getAudioTrack(command.config.devices.findIndex(d=>d.id===placement.deviceId)):await sequence.getVideoTrack(placement.trackIndex);
      const matches=[];
      for(const item of await track.getTrackItems(this.p.Constants.TrackItemType.CLIP,false)){
        const source=this.p.ClipProjectItem.cast(await item.getProjectItem());
        if(normalize(await source.getMediaFilePath())===normalize(asset.main.path))matches.push(item);
      }
      // Premiere snaps insertions to the sequence frame boundary.
      const expected=BigInt(adobeTicks(placement.startTicks));
      if(matches.length!==1)throw new Error("Premiere nie umieściło klipu na timeline: "+asset.logicalName);
      const actual=BigInt((await matches[0].getStartTime()).ticks);
      if((actual>expected?actual-expected:expected-actual)>5080320000n)throw new Error("Niezgodna pozycja klipu na timeline: "+asset.logicalName);
    }
    if(!await project.save())throw new Error("Premiere nie potwierdziło zapisu projektu.");
    await this.progress(command,"timeline",command.assets.length,command.assets.length,undefined,100);
    const videoAssets=command.assets.filter(a=>a.media.hasVideo);let completed=0;
    for(const asset of videoAssets){
      const clip=imported.get(normalize(asset.main.path));
      const paths=await this.request("/adobe/"+encodeURIComponent(command.id)+"/proxy-path",{assetId:asset.id});
      this.log("Proxy "+(completed+1)+"/"+videoAssets.length+": "+asset.logicalName);
      await this.progress(command,"proxy",completed,videoAssets.length,asset.id);
      const check=await this.request("/adobe/"+encodeURIComponent(command.id)+"/validate-proxy",{assetId:asset.id,path:paths.path});
      if(!check.valid){while((await this.request("/adobe/"+encodeURIComponent(command.id)+"/prepare-proxy",{assetId:asset.id,path:paths.path}))?.waiting)await this.delay(2000);const existing=await this.request("/adobe/"+encodeURIComponent(command.id)+"/validate-proxy",{assetId:asset.id,path:paths.path});if(!existing.valid)await this.encode(clip,paths.path,paths.preset??command.config.proxyPreset!,command.id,asset.id,completed,videoAssets.length);}
      if(!await clip.hasProxy()||normalize(await clip.getProxyPath())!==normalize(paths.path)){
        if(!await clip.attachProxy(paths.path,false,false))throw new Error("Nie udało się przypiąć proxy: "+asset.logicalName);
      }
      if(!await clip.hasProxy()||normalize(await clip.getProxyPath())!==normalize(paths.path))throw new Error("Premiere nie potwierdziło przypięcia właściwego proxy: "+asset.logicalName);
      proxies[asset.id]=paths.path;
      if(!await project.save())throw new Error("Nie potwierdzono zapisu przypiętego proxy.");
      if(!await clip.hasProxy()||normalize(await clip.getProxyPath())!==normalize(paths.path))throw new Error("Przypięcie proxy nie przetrwało zapisu projektu: "+asset.logicalName);
      await this.progress(command,"proxy",++completed,videoAssets.length,asset.id,100);
    }
    await this.progress(command,"complete",command.assets.length,command.assets.length,undefined,100);
    await this.request("/adobe/"+encodeURIComponent(command.id)+"/result",{state:"complete",error:null,proxies});
  }
  private async encode(clip:Host,path:string,preset:string,commandId:string,assetId:string,completed=0,total=1):Promise<void> {
    const manager=this.p.EncoderManager.getManager();if(!manager.isAMEInstalled)throw new Error("Brak Adobe Media Encoder.");
    if(!await manager.launchEncoder())throw new Error("AME nie został uruchomiony.");
    let jobId:string|undefined;let failure:string|undefined;let rendered=false;let lastSignal=Date.now();let lastReport=0;let percent=0;
    const event=(kind:string)=>(value:Host)=>{
      // Read SDK properties directly: native host fields need not be enumerable.
      const files=value?.outputFiles??(value?.outputFile?[value.outputFile]:[]);
      const matching=Array.from(files as string[]).some(file=>typeof file==="string"&&normalize(file)===normalize(path));
      const id=typeof value?.jobID==="string"?value.jobID:undefined;
      if(kind==="queue"&&!jobId)jobId=id;
      if(!matching&&(!jobId||id!==jobId))return;
      if(matching&&id)jobId=id;lastSignal=Date.now();
      if(kind==="complete")rendered=true;
      if(kind==="error")failure="AME zgłosiło błąd "+String(value?.inErrorNumber??"nieznany")+" (zadanie "+String(id??"bez ID")+").";
      if(kind==="cancel")failure="Anulowano kodowanie proxy w AME.";
      if(kind==="progress"&&typeof value?.progressAmount==="number")percent=Math.max(0,Math.min(100,value.progressAmount<=1?value.progressAmount*100:value.progressAmount));
      console.info("DIT AME "+kind+" job="+String(id??"")+" output="+(matching?path:"")+" progress="+percent);
    };
    const listeners:[string,(event:Host)=>void][]=[
      [this.p.EncoderManager.EVENT_RENDER_QUEUE,event("queue")],[this.p.EncoderManager.EVENT_RENDER_COMPLETE,event("complete")],
      [this.p.EncoderManager.EVENT_RENDER_ERROR,event("error")],[this.p.EncoderManager.EVENT_RENDER_CANCEL,event("cancel")],
      [this.p.EncoderManager.EVENT_RENDER_PROGRESS,event("progress")]
    ].filter(entry=>typeof entry[0]==="string") as [string,(event:Host)=>void][];
    for(const [name,handler] of listeners)this.p.EventManager.addEventListener(manager,name,handler,false);
    try{
      if(!await manager.encodeProjectItem(clip,path,preset,0,false,true))throw new Error("AME odrzuciło zadanie.");
      while(true){
        if(failure)throw new Error(failure);
        const result=await this.request("/adobe/"+encodeURIComponent(commandId)+"/validate-proxy",{assetId,path});
        if(result.valid)return; // Worker also verifies the physical output, independently of host events.
        if(rendered&&result.ready!==false)throw new Error("Wynik AME nie przeszedł weryfikacji: "+(result.reason??"niekompletny plik"));
        if(Date.now()-lastSignal>15*60*1000)throw new Error("AME nie potwierdza postępu od 15 minut. Sprawdź kolejkę AME. "+(result.reason??""));
        if(Date.now()-lastReport>1000){lastReport=Date.now();this.log("Proxy "+(completed+1)+"/"+total+": "+Math.round(percent)+"%");await this.request("/adobe/"+encodeURIComponent(commandId)+"/progress",{stage:"proxy",assetId,completed,total,percent,updatedAt:new Date().toISOString(),jobId:jobId??null,event:rendered?"complete":"rendering"});}
        await this.delay(2000);
      }
    }finally{for(const [name,handler] of listeners)this.p.EventManager.removeEventListener(manager,name,handler,false);}
  }
}
