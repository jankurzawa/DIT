/* Pure presentation logic; the worker remains the authority for sessions/copy verification. */
(function(root){
  const ticks=10000000;
  const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const bytes=value=>Number(value)>=1e9?(Number(value)/1e9).toLocaleString('pl-PL',{maximumFractionDigits:2})+' GB':(Number(value||0)/1e6).toLocaleString('pl-PL',{maximumFractionDigits:1})+' MB';
  const duration=value=>{const s=Math.max(0,Math.floor(Number(value||0)/ticks));return [Math.floor(s/3600),Math.floor(s/60)%60,s%60].map(v=>String(v).padStart(2,'0')).join(':');};
  const processing=workflow=>!workflow?.ingested||!workflow?.backedUp||(workflow?.proxyRequired&&!workflow?.proxy)||!workflow?.inPremiere;
  function filterAssets(assets,workflows,batches,search='',stage='all',sort='name') {
    const states=new Map(workflows.map(w=>[w.assetId,w]));const errors=new Set(batches.filter(b=>b.error).map(b=>b.id));
    const query=search.trim().toLocaleLowerCase();
    const rows=assets.filter(a=>a.isMedia).map(asset=>({asset,w:states.get(asset.id),error:errors.has(asset.batchId)})).filter(({asset,w,error})=>{
      if(query&&!`${asset.logicalName} ${asset.ownerName||asset.ownerId}`.toLocaleLowerCase().includes(query))return false;
      return stage==='all'||stage==='processing'&&processing(w)&&!error||stage==='ready'&&!processing(w)&&!error||stage==='synced'&&!processing(w)&&w?.synchronized&&!error||stage==='sync'&&!w?.synchronized||stage==='proxy'&&w?.proxyRequired&&!w?.proxy||stage==='premiere'&&!w?.inPremiere||stage==='error'&&error;
    });
    rows.sort((a,b)=>sort==='size'?b.asset.bytes-a.asset.bytes:sort==='duration'?(b.asset.media?.durationTicks||0)-(a.asset.media?.durationTicks||0):sort==='owner'?String(a.asset.ownerName||a.asset.ownerId).localeCompare(String(b.asset.ownerName||b.asset.ownerId)):sort==='stage'?Number(!processing(a.w))-Number(!processing(b.w)):a.asset.logicalName.localeCompare(b.asset.logicalName,'pl',{numeric:true})*(sort==='reverse'?-1:1));
    return rows;
  }
  function localParts(date,zone) {
    const values=Object.fromEntries(new Intl.DateTimeFormat('sv-SE',{timeZone:zone,year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hourCycle:'h23'}).formatToParts(date).map(p=>[p.type,p.value]));
    return {date:`${values.year}-${values.month}-${values.day}`,time:`${values.hour}:${values.minute}:${values.second}`};
  }
  function zonedIso(date,time,zone) {
    const desired=`${date}T${time.length===5?time+':00':time}`;const wall=Date.parse(desired+'Z');if(!Number.isFinite(wall))throw Error('Podaj datę i godzinę.');
    let utc=wall;
    for(let i=0;i<4;i++){const local=localParts(new Date(utc),zone);utc+=wall-Date.parse(`${local.date}T${local.time}Z`);}
    const actual=localParts(new Date(utc),zone);if(`${actual.date}T${actual.time}`!==desired)throw Error('Ta godzina nie istnieje w wybranej strefie z powodu zmiany czasu.');
    return new Date(utc).toISOString();
  }
  const model={escape,bytes,duration,processing,filterAssets,localParts,zonedIso};
  if(typeof module!=='undefined')module.exports=model;else root.DitModel=model;
})(globalThis);
