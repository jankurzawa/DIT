const {escape:esc,bytes,duration,processing,filterAssets,localParts,zonedIso}=DitModel;
const $=selector=>document.querySelector(selector);
const state={projects:[],project:null,tab:null,sessions:new Map(),view:'home',assets:[],workflows:[],batches:[],audio:[],admin:null,selected:null,rows:[],source:'',files:null,editor:null,editorPage:'storage',busy:false,nativeDialog:false};
async function pick(kind){state.nativeDialog=true;try{return await dit.pick(kind);}finally{state.nativeDialog=false;await dit.api('POST','/session/activity',{});}}
const titles={home:'Panel administratora',footage:'Nagrania i zgrywania',stats:'Statystyki i dyski',days:'Dni zdjęciowe',alerts:'Alerty',history:'Historia operacji',adobe:'Premiere i proxy',report:'Raporty',config:'Konfiguracja projektu'};
const session=()=>state.sessions.get(`${state.project?.id}:${state.tab?.id}`);
const route=suffix=>`/projects/${encodeURIComponent(state.project.id)}${suffix}`;
const api=(method,suffix,body)=>dit.api(method,route(suffix),body,session()?.token);
function message(text,error=false){$('#message').textContent=text;$('#message').className=error?'error':'';}
async function guard(action){try{return await action();}catch(error){message(error.message,true);if(error.message.includes('403:')){state.sessions.clear();state.selected=null;renderLogin();}return null;}}
const button=(label,act,extra='')=>`<button data-act="${act}" ${extra} title="${esc(label)}">${label}</button>`;
function stopPlayer(){const video=$('#video');if(video){video.pause();video.removeAttribute('src');video.load();}state.selected=null;}
function header(){
  $('#project').innerHTML=state.projects.length?state.projects.map(p=>`<option value="${esc(p.id)}" ${state.project?.id===p.id?'selected':''}>${esc(p.name)}</option>`).join(''):'<option>Brak projektów</option>';
  $('#tabs').innerHTML=(state.project?.tabs||[]).map(t=>button(esc(t.name),'tab',`data-id="${esc(t.id)}" class="${state.tab?.id===t.id?'active':''}"`)).join('');
}
function title(){return `<div class="titlebar">${session()?.admin&&state.view!=='home'?button('← Panel administratora','home'):''}<h1>${esc(session()?.admin?titles[state.view]:state.tab?.name||'DIT')}</h1>${button('Zmień hasło','password','class="password"')}</div>`;}
async function projects(selected){
  state.projects=await dit.api('GET','/projects');state.project=state.projects.find(p=>p.id===(selected||state.project?.id))||state.projects[0]||null;
  if(!state.project){header();$('#workspace').innerHTML='<div class="center"><h1>Witaj w DIT</h1><p>Utwórz projekt, skonfiguruj kamery i nazwij mikroporty.</p>'+button('＋ Nowy projekt','new','class="primary"')+'</div>';return;}
  state.tab=state.project.tabs.find(t=>t.id===state.tab?.id)||state.project.tabs.find(t=>t.kind==='Admin');header();await overview();await openTab(state.tab.id);
}
async function overview(){if(!state.project)return;const o=await dit.api('GET',route('/overview'));$('#day-status').textContent=o.number?`Dzień ${o.number} · ${o.open?'OTWARTY':'ZAMKNIĘTY'}   ${new Date(o.start).toLocaleString('pl-PL')}   ·   ${o.paused?'Kolejka wstrzymana':'Kolejka aktywna'}`:'Nie otwarto dnia zdjęciowego';}
async function openTab(id){
  const previous=session();if(previous)await dit.api('POST','/session/leave',{},previous.token);
  stopPlayer();state.tab=state.project.tabs.find(t=>t.id===id);state.view=state.tab.kind==='Admin'?'home':'footage';state.editor=null;header();
  if(session()){try{await dit.api('POST','/session/enter',{},session().token);}catch{state.sessions.delete(`${state.project.id}:${id}`);}}
  if(!session())renderLogin();else await renderView();
}
function renderLogin(){
  $('#workspace').innerHTML=`<div class="center"><h1>${esc(state.tab?.name||'DIT')}</h1><p>Zaloguj się do zakładki</p><form id="login" class="panel login"><label>Hasło<input id="login-password" type="password" autocomplete="current-password"></label><div class="actions"><button type="submit" class="primary">Zaloguj</button></div></form></div>`;
  $('#login-password')?.focus();
  $('#login')?.addEventListener('submit',event=>{event.preventDefault();guard(async()=>{const value=await dit.api('POST','/login',{projectId:state.project.id,tabId:state.tab.id,userId:state.tab.userId,password:$('#login-password').value});state.sessions.set(`${state.project.id}:${state.tab.id}`,value);await renderView();});});
}
async function navigate(view,remember=true){stopPlayer();state.view=view;if(remember)history.pushState({view},'');await renderView();}
async function renderView(){
  if(!session())return renderLogin();
  if(state.view==='config')return editor(await api('GET','/config'));
  $('#workspace').innerHTML=title()+'<div id="view"></div>';
  if(state.view==='home'){
    $('#view').innerHTML='<div class="tiles">'+[['footage','Nagrania i zgrywania','Materiał, postęp kopii, proxy i podgląd'],['days','Dni zdjęciowe','Kalendarz, sekwencje i podsumowanie dnia'],['config','Konfiguracja','Magazyny, kamery, źródła audio i hasła'],['stats','Statystyki i dyski','Zajęte miejsce i zweryfikowane kopie'],['adobe','Premiere i proxy','Połączenie z Adobe i postęp kolejki'],['alerts','Alerty','Problemy wymagające uwagi'],['history','Historia','Operacje i ich szczegóły'],['report','Raporty','Eksport JSON i CSV']].map(([key,name,desc])=>button(`<strong>${name}</strong><small>${desc}</small>`,'navigate',`class="tile" data-view="${key}"`)).join('')+'</div>';
  } else if(state.view==='footage'){await footage();}
  else if(state.view==='days'){await days();}
  else if(state.view==='report'){$('#view').innerHTML='<section class="panel"><h2>Eksport raportu</h2><p>Statystyki, nagrania i historia operacji projektu.</p>'+button('Zapisz JSON','export')+' '+button('Zapisz CSV','export','data-csv="true"')+'</section>';}
  else {state.admin=await api('GET','/admin');renderAdmin();}
}
async function footage(){
  const audio=state.tab.kind==='Audio';state.source='';state.files=null;
  if(audio)state.audio=await api('GET','/audio-inputs');
  $('#view').innerHTML=(!session().admin?`<section class="panel ingest"><div class="row"><input id="source" placeholder="Wybierz kartę lub folder źródłowy" aria-label="Folder źródłowy">${audio?`<select id="audio-source" class="audio-name" aria-label="Źródło audio"><option value="">Wybierz źródło / mikroport…</option>${state.audio.map(a=>`<option value="${esc(a.deviceId)}">${esc(a.name)}</option>`).join('')}</select>`:''}${button('Wybierz folder…','source-folder')}${audio?button('Pliki…','source-files'):''}${button('↓ Rozpocznij zgrywanie','ingest','class="primary"')}</div>${audio&&!state.audio.length?'<p class="error">Dodaj nazwane źródła audio w konfiguracji administratora.</p>':''}</section>`:'')+
    `<div class="columns"><section class="panel"><h2>Nagrania</h2><small class="count" id="asset-count"></small><div class="filters"><input id="search" placeholder="Szukaj nazwy lub właściciela" aria-label="Szukaj nagrań"><select id="filter" aria-label="Etap"><option value="all">Wszystkie etapy</option><option value="processing">W toku</option><option value="ready">Kopie i Premiere gotowe</option><option value="synced">W pełni gotowe</option><option value="sync">Do synchronizacji</option><option value="proxy">Brak proxy</option><option value="premiere">Brak w Premiere</option><option value="error">Błędy</option></select><select id="sort" aria-label="Sortowanie"><option value="name">Nazwa A–Z</option><option value="reverse">Nazwa Z–A</option><option value="stage">Etap pracy</option><option value="owner">Właściciel</option><option value="size">Największe pliki</option><option value="duration">Najdłuższe nagrania</option></select></div><div class="scroll"><table><thead><tr><th>Nazwa pliku / etapy</th>${session().admin?'<th>Właściciel</th>':''}<th>Rozmiar</th><th>Czas</th></tr></thead><tbody id="asset-rows"></tbody></table><div id="no-assets" class="empty" hidden>Brak nagrań pasujących do filtrów.</div></div></section><section class="panel"><h2>Zgrywania</h2>${session().admin?`<div class="section-actions">${button('Wstrzymaj','pause')}${button('Wznów','resume')}</div>`:''}<div id="batch-rows" class="scroll"></div></section><section class="panel"><h2 id="player-title">Podgląd</h2><div class="preview"><video id="video" playsinline preload="auto"></video><span id="empty-player">Wybierz nagranie z listy</span></div><input class="timeline" id="seek" type="range" min="0" max="1" value="0" step="0.01" aria-label="Pozycja odtwarzania"><div class="player-meta"><span id="player-time">00:00 / 00:00</span><select id="speed" aria-label="Prędkość odtwarzania">${[1,1.5,2,3,4].map(v=>`<option value="${v}">${v}×</option>`).join('')}</select></div><div class="controls">${button('◀◀','previous','aria-label="Poprzednie nagranie"')}${button('▶','play','id="play" class="primary" aria-label="Odtwórz lub wstrzymaj"')}${button('▶▶','next','aria-label="Następne nagranie"')}</div></section></div>`;
  $('#search').addEventListener('input',renderAssets);$('#filter').addEventListener('change',renderAssets);$('#sort').addEventListener('change',renderAssets);
  const video=$('#video');$('#seek').addEventListener('input',()=>{if(Number.isFinite(video.duration))video.currentTime=Number($('#seek').value);});
  $('#speed').addEventListener('change',()=>{video.playbackRate=Number($('#speed').value);});
  for(const event of ['timeupdate','loadedmetadata','seeked'])video.addEventListener(event,()=>{if(Number.isFinite(video.duration)){$('#seek').max=String(video.duration);$('#seek').value=String(video.currentTime);$('#player-time').textContent=`${duration(video.currentTime*1e7)} / ${duration(video.duration*1e7)}`;}});
  video.addEventListener('play',()=>{$('#play').textContent='Ⅱ';});video.addEventListener('pause',()=>{$('#play').textContent='▶';});
  video.addEventListener('loadeddata',()=>{$('#empty-player').hidden=true;});
  video.addEventListener('error',()=>{if(video.src){$('#empty-player').hidden=false;$('#empty-player').textContent='Nie udało się odtworzyć podglądu. Sprawdź postęp kolejki.';}});
  await refreshFootage();
}
async function refreshFootage(){
  const [assets,workflows,batches]=await Promise.all([api('GET','/assets'),api('GET','/workflow'),api('GET','/batches')]);
  if(state.view!=='footage'||!$('#asset-rows'))return;
  const names=new Map(state.project.tabs.map(t=>[t.userId,t.name]));state.assets=assets.map(a=>({...a,ownerName:names.get(a.ownerId)||a.ownerId}));state.workflows=workflows;state.batches=batches;renderAssets();renderBatches();
}
function renderAssets(){
  if(!$('#asset-rows'))return;
  state.rows=filterAssets(state.assets,state.workflows,state.batches,$('#search').value,$('#filter').value,$('#sort').value);
  const mark=ready=>ready?'✓':'○';
  $('#asset-rows').innerHTML=state.rows.map(({asset:a,w,error})=>`<tr class="selectable ${state.selected===a.id?'selected':''}" data-act="asset" data-id="${esc(a.id)}"><td>${esc(a.logicalName)}<span class="stages">${mark(w?.ingested)} Zgrane　${mark(w?.backedUp)} Backup<br>${w?.proxyRequired===false?'—':mark(w?.proxy)} Proxy　${mark(w?.inPremiere)} Premiere　${mark(w?.synchronized)} Sync${error?'<br><span class="error">Błąd zgrywania</span>':''}</span></td>${session().admin?`<td>${esc(a.ownerName)}</td>`:''}<td class="nowrap">${bytes(a.bytes)}</td><td class="nowrap">${duration(a.media?.durationTicks)}</td></tr>`).join('');
  $('#asset-count').textContent=`${state.rows.length} z ${state.assets.filter(a=>a.isMedia).length} nagrań · ${state.workflows.filter(processing).length} w toku`;
  $('#no-assets').hidden=state.rows.length>0;
}
const stageNames={Queued:'W kolejce',Copying:'Zgrywanie',BackingUp:'Backup',Secured:'Kopie gotowe',WaitingAdobe:'Oczekuje na Premiere',WaitingApproval:'Wymaga zgody',Completed:'Ukończone',Paused:'Wstrzymane',Failed:'Błąd',WaitingMetadata:'Analiza metadanych',WaitingAssignment:'Organizowanie'};
function renderBatches(){
  const names=new Map(state.project.tabs.map(t=>[t.userId,t.name]));
  $('#batch-rows').innerHTML=state.batches.slice().sort((a,b)=>new Date(b.createdAt)-new Date(a.createdAt)).map(b=>{
    const p=b.progress;const percent=b.stage==='Completed'?100:p?.totalBytes?Math.min(100,(p.mainBytes+p.backupBytes)/(2*p.totalBytes)*100):0;
    return `<article class="batch"><strong>${esc(names.get(b.ownerId)||b.ownerId)}</strong><small>${esc(b.sourceRoot)}</small><progress max="100" value="${percent}"></progress><small>${esc(stageNames[b.stage]||b.stage)} · ${percent.toFixed(0)}%<br>${b.files}/${b.totalFiles} plików${p?' · '+bytes(p.mainBytes)+' / '+bytes(p.totalBytes):''}</small>${b.error?`<small class="error">${esc(b.error)}</small>`:''}${session().admin&&b.stage==='WaitingApproval'?button('Zatwierdź spóźnione zgrywanie','approve',`data-id="${esc(b.id)}"`):''}</article>`;
  }).join('')||'<div class="empty">Brak zgrywań</div>';
}
let previewGeneration=0;
async function selectAsset(id){
  const generation=++previewGeneration;const asset=state.assets.find(a=>a.id===id);if(!asset)return;
  const video=$('#video');video.pause();video.removeAttribute('src');video.removeAttribute('poster');video.load();state.selected=id;renderAssets();
  $('#player-title').textContent=asset.logicalName;$('#empty-player').hidden=false;$('#empty-player').textContent=asset.media?.hasVideo?'Przygotowywanie pierwszej klatki…':'Nagranie audio';
  if(asset.media?.hasVideo){const thumb=await dit.preview(state.project.id,id,session().token,true);if(generation!==previewGeneration||!$('#video'))return;video.poster=thumb.url;$('#empty-player').hidden=true;}
  for(let i=0;i<300;i++){
    const result=await dit.preview(state.project.id,id,session().token,false);
    if(generation!==previewGeneration||state.selected!==id||!$('#video'))return;
    if(result.state==='ready'){video.src=result.url;video.playbackRate=Number($('#speed').value);video.load();if(!asset.media?.hasVideo){$('#empty-player').hidden=false;$('#empty-player').textContent='♪ '+asset.logicalName;}return;}
    message('Trwa przygotowywanie lekkiego podglądu. Zgrywanie ma pierwszeństwo.');await new Promise(resolve=>setTimeout(resolve,1000));
  }
  message('Podgląd nadal oczekuje w kolejce. Wybierz nagranie ponownie później.');
}
function table(head,rows){return `<div class="scroll"><table><thead><tr>${head.map(v=>`<th>${v}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`;}
function renderAdmin(){
  if(!$('#view'))return;const a=state.admin;
  if(state.view==='stats'){
    const sum=key=>a.statistics.reduce((n,s)=>n+s[key],0);
    $('#view').innerHTML=`<section class="panel"><div class="summary"><div><strong>${sum('clips')} nagrań</strong><small>Zweryfikowany materiał</small></div><div><strong>${duration(sum('durationTicks'))}</strong><small>Czas materiału</small></div><div><strong>${bytes(sum('originalBytes'))}</strong><small>Oryginały</small></div></div><h2>Statystyki urządzeń</h2>${table(['Urządzenie','Nagrania','Czas','Oryginały','Backup','Proxy'],a.statistics.map(s=>`<tr><td>${esc(a.deviceNames[s.deviceId]||s.deviceId)}</td><td>${s.clips}</td><td>${duration(s.durationTicks)}</td><td>${bytes(s.originalBytes)}</td><td>${bytes(s.backupBytes)}</td><td>${bytes(s.proxyBytes)}</td></tr>`))}<h3>Magazyny</h3>${table(['Folder','Fizyczny dysk','Wolne miejsce','Stan'],a.volumes.map(v=>`<tr><td>${esc(v.path)}</td><td>${esc(v.info?.physicalDiskId||'—')}</td><td>${v.info?bytes(v.info.availableBytes):'—'}</td><td>${esc(v.error||'Dostępny')}</td></tr>`))}</section>`;
  } else if(state.view==='adobe'){
    $('#view').innerHTML=`<section class="panel"><h2>Premiere i proxy</h2><p id="adobe-status">Sprawdzanie połączenia…</p><div class="section-actions">${button('Skonfiguruj / otwórz Adobe','adobe-connect','class="primary"')}${button('Wstrzymaj kolejkę','pause')}${button('Wznów kolejkę','resume')}</div><p class="muted">Jeden projekt Premiere na produkcję; osobna sekwencja na dzień. Domyślny preset proxy i panel DIT są w pakiecie. Premiere Pro, Media Encoder i Creative Cloud wymagają osobnej instalacji.</p>${table(['Operacja','Etap','Postęp','Informacja'],a.adobe.map(c=>`<tr><td>${esc(c.id)}</td><td>${esc(c.progress?.stage||c.state)}</td><td>${c.progress?`${c.progress.completed}/${c.progress.total}<progress max="100" value="${c.progress.percent}"></progress>`:'—'}</td><td>${esc(c.error||'')}</td></tr>`))}</section>`;
    guard(async()=>{const s=await api('GET','/adobe-status');if($('#adobe-status'))$('#adobe-status').textContent=s.connected?'Połączono z panelem DIT w Premiere':'Panel Premiere nie jest połączony. Uruchom konfigurację.';});
  } else if(state.view==='alerts'){
    $('#view').innerHTML=`<section class="panel"><h2>Alerty</h2>${table(['Komunikat','Stan','Notatka','Akcja'],a.alerts.map(alert=>`<tr><td>${esc(alert.message)}</td><td>${alert.resolved?'Rozwiązany':'Aktywny'}</td><td>${esc(alert.note||'')}</td><td>${button(alert.resolved?'Otwórz ponownie':'Rozwiąż','resolve',`data-id="${esc(alert.key)}" data-resolved="${!alert.resolved}"`)}</td></tr>`))}</section>`;
  } else if(state.view==='history'){
    $('#view').innerHTML=`<section class="panel"><h2>Historia operacji</h2>${table(['Kiedy','Kto','Operacja','Szczegóły'],a.audit.slice().reverse().map(v=>`<tr><td>${esc(new Date(v.at).toLocaleString('pl-PL'))}</td><td>${esc(v.actorId)}</td><td>${esc(v.action)}</td><td>${esc(v.detail)}</td></tr>`))}</section>`;
  }
}
async function days(){
  state.days=await api('GET','/days');
  $('#view').innerHTML=`<section class="panel"><div class="section-actions">${button('Otwórz dzień','open-day','class="primary"')}${button('Zamknij dzień','close-day')}</div><div class="split"><div>${table(['Dzień','Stan','Początek','Koniec'],state.days.slice().reverse().map(d=>`<tr class="selectable" data-act="day" data-id="${esc(d.id)}"><td>${d.number}</td><td>${d.closed?'Zamknięty':'Otwarty'}</td><td>${esc(new Date(d.start).toLocaleString('pl-PL'))}</td><td>${d.end?esc(new Date(d.end).toLocaleString('pl-PL')):'—'}</td></tr>`))}</div><div id="day-summary"><p class="muted">Wybierz dzień, aby zobaczyć podsumowanie.</p></div></div></section>`;
  if(state.days.length)await daySummary(state.days.at(-1).id);
}
async function daySummary(id){
  const stats=await api('GET',`/days/${encodeURIComponent(id)}/statistics`);if(!$('#day-summary'))return;
  for(const row of document.querySelectorAll('[data-act="day"]'))row.classList.toggle('selected',row.dataset.id===id);
  $('#day-summary').innerHTML=`<h2>Podsumowanie dnia</h2><div class="summary"><div><strong>${stats.clips}</strong><small>Nagrania</small></div><div><strong>${duration(stats.durationTicks)}</strong><small>Materiał</small></div><div><strong>${bytes(stats.bytes)}</strong><small>Rozmiar</small></div></div><p>Długość dnia: ${duration(stats.elapsedTicks)}</p>${table(['Karta / źródło','Nagrania','Czas','Rozmiar'],stats.cards.map(c=>`<tr><td>${esc(c.tab)}<small><br>${esc(c.source)}</small></td><td>${c.clips}</td><td>${duration(c.durationTicks)}</td><td>${bytes(c.bytes)}</td></tr>`))}`;
}
function zones(selected){return [...new Set([selected||'Europe/Warsaw','UTC',...Intl.supportedValuesOf('timeZone')])].map(z=>`<option ${z===selected?'selected':''}>${esc(z)}</option>`).join('');}
let dialogSave;
function modal(name,fields,save){$('#dialog-title').textContent=name;$('#dialog-fields').innerHTML=fields;$('#dialog-error').textContent='';dialogSave=save;$('#dialog').showModal();}
async function dayDialog(close){
  const config=await api('GET','/config');const now=localParts(new Date(),config.timeZone);const day=close?state.days.find(d=>!d.closed):null;if(close&&!day)throw Error('Nie ma otwartego dnia.');
  modal(close?'Koniec dnia zdjęciowego':'Początek dnia zdjęciowego',`<div class="formgrid"><label>Data<input id="date" type="date" value="${now.date}" required></label><label>Godzina<input id="time" type="time" step="1" value="${now.time}" required></label></div><label>Strefa czasowa<select id="zone">${zones(config.timeZone)}</select></label>`,async()=>{const at=zonedIso($('#date').value,$('#time').value,$('#zone').value);await api('POST',close?`/days/${day.id}/close`:'/days',{at});await overview();await days();});
}
function configRow(kind,value={}){
  return {id:value.id||crypto.randomUUID().replaceAll('-',''),name:value.name||'',password:'',changed:false,...value,kind};
}
function editor(config=null){
  if(!state.editor){
    state.editor={config:config?structuredClone(config):{id:crypto.randomUUID().replaceAll('-',''),name:'',mainRoots:[],backupRoots:[],proxyRoot:'',premiereRoot:'',timeZone:Intl.DateTimeFormat().resolvedOptions().timeZone||'Europe/Warsaw',proxyPreset:'dit:proxy-720p',compressBackup:false,autoClear:false},editing:!!config,cameras:config?config.tabs.filter(t=>t.kind==='Camera').map(t=>configRow('camera',{...t,device:config.devices.find(d=>d.id===t.deviceId)})):[configRow('camera',{name:'Kamera 1'})],audio:config?config.audioSources.map(a=>configRow('audio',{...config.devices.find(d=>d.id===a.deviceId),source:a})):[configRow('audio',{name:'Mikroport 1'})],audioPassword:'',audioChanged:false,adminPassword:''};
  }
  state.view='config';const e=state.editor,c=e.config;
  $('#workspace').innerHTML=`<div class="titlebar">${button('← Panel administratora','cancel-editor')}<h1>${e.editing?'Konfiguracja projektu':'Nowy projekt'}</h1></div><div class="editor"><h2>Ustawienia produkcji</h2><p class="muted">Projekt, magazyny i zespół — przygotuj przestrzeń do pracy.</p><nav class="editor-nav">${[['storage','Projekt i magazyny'],['team','Zakładki i audio'],['profiles','Premiere i profile']].map(([id,label])=>button(label,'editor-page',`data-page="${id}" class="${state.editorPage===id?'active':''}"`)).join('')}</nav><div class="editor-page" id="editor-page"></div><div class="actions">${button('Anuluj','cancel-editor')}${button('Zapisz konfigurację','save-editor','class="primary"')}</div></div>`;
  if(state.editorPage==='storage'){
    $('#editor-page').innerHTML=`<div class="formgrid"><label>Nazwa projektu<input data-config="name" value="${esc(c.name)}"></label><label>Strefa czasowa<select data-config="timeZone">${zones(c.timeZone)}</select></label>${[['mainRoots','Foldery główne'],['backupRoots','Foldery backupu'],['proxyRoot','Folder proxy'],['premiereRoot','Folder projektów Premiere']].map(([key,name])=>`<label>${name}<div class="row"><${key.endsWith('Roots')?'textarea':'input'} data-config="${key}" ${key.endsWith('Roots')?'':`value="${esc(c[key])}"`}>${key.endsWith('Roots')?esc(c[key].join('\n'))+'</textarea>':''}${button('Wybierz…','config-path',`data-key="${key}"`)}</div></label>`).join('')}</div><p class="muted">Foldery nie mogą się pokrywać. Backup wymaga niezależnego fizycznego dysku. Każdy folder główny i backupowy podaj w osobnym wierszu.</p>${!e.editing?'<label>Hasło administratora (opcjonalne)<input id="admin-password" type="password" autocomplete="new-password"></label>':''}`;
  } else if(state.editorPage==='team'){
    $('#editor-page').innerHTML=`<h2>Kamery i operatorzy</h2><div id="camera-config"></div>${button('＋ Dodaj zakładkę','add-camera')}<h3>Audio — jedna zakładka, nazwane źródła</h3><label>Hasło zakładki Audio<input id="audio-password" type="password" autocomplete="new-password" placeholder="${e.editing?'Bez zmiany hasła':'Opcjonalne'}"></label><div id="audio-config"></div>${button('＋ Dodaj źródło audio','add-audio')}<p class="muted">Każdy mikroport jest osobnym źródłem. Przy zgrywaniu wybierzesz je z listy obok ścieżki.</p>`;
    renderConfigRows();$('#audio-password').value=e.audioPassword;$('#audio-password').addEventListener('input',()=>{e.audioPassword=$('#audio-password').value;e.audioChanged=true;});
  } else {
    $('#editor-page').innerHTML=`<div class="formgrid">${[['proxyPreset','Preset proxy AME','preset'],['sequencePreset','Preset sekwencji (opcjonalnie)','sequence'],['premiereTemplate','Projekt wzorcowy (opcjonalnie)','template']].map(([key,label,kind])=>`<label>${label}<div class="row"><input data-config="${key}" value="${esc(c[key]||'')}" placeholder="${key==='proxyPreset'?'dit:proxy-720p':'Opcjonalne'}">${button('Wybierz więcej…','profile-path',`data-key="${key}" data-kind="${kind}"`)}</div></label>`).join('')}</div><label class="check"><input data-config="compressBackup" type="checkbox" ${c.compressBackup?'checked':''}> Kompresuj backup bezstratnie (7z)</label><div class="info">Automatyczne czyszczenie kart na macOS jest wyłączone. Kopiowanie i weryfikacja SHA-256 działają przed przekazaniem materiału do Adobe. Sekwencje: UHD 3840×2160, 50 fps.</div>`;
  }
  for(const input of document.querySelectorAll('[data-config]'))input.addEventListener('input',()=>{const key=input.dataset.config;c[key]=input.type==='checkbox'?input.checked:key.endsWith('Roots')?input.value.split('\n').map(v=>v.trim()).filter(Boolean):input.value;});
  if($('#admin-password')){$('#admin-password').value=e.adminPassword;$('#admin-password').addEventListener('input',()=>{e.adminPassword=$('#admin-password').value;});}
}
function renderConfigRows(){
  for(const [key,selector] of [['cameras','#camera-config'],['audio','#audio-config']]){
    $(selector).innerHTML=state.editor[key].map((v,i)=>`<div class="config-row"><label>${key==='cameras'?'Nazwa zakładki / operatora':'Nazwa źródła / mikroportu'}<input data-row="${key}" data-index="${i}" data-field="name" value="${esc(v.name)}"></label>${key==='cameras'?`<label>Nowe hasło<input type="password" data-row="${key}" data-index="${i}" data-field="password" value="${esc(v.password)}" placeholder="${state.editor.editing?'Bez zmiany hasła':'Opcjonalne'}" autocomplete="new-password"></label>`:''}${button('Usuń','remove-config-row',`data-row="${key}" data-index="${i}"`)}</div>`).join('');
  }
  for(const input of document.querySelectorAll('[data-field]'))input.addEventListener('input',()=>{const row=state.editor[input.dataset.row][Number(input.dataset.index)];row[input.dataset.field]=input.value;if(input.dataset.field==='password')row.changed=true;});
}
async function saveEditor(){
  const e=state.editor,c=structuredClone(e.config);const previous=c.tabs||[];const admin=previous.find(t=>t.kind==='Admin')||{id:'admin',name:'Admin',kind:'Admin',userId:'admin',deviceId:null};const audioTab=previous.find(t=>t.kind==='Audio')||{id:'audio',name:'Audio',kind:'Audio',userId:'audio',deviceId:null};
  const users=[],changes={};c.tabs=[admin];c.devices=[];c.audioSources=[];
  if(!e.editing)users.push({id:admin.userId,name:'Admin',password:e.adminPassword,admin:true},{id:audioTab.userId,name:'Audio',password:e.audioPassword,admin:false});else if(e.audioChanged)changes[audioTab.userId]=e.audioPassword;
  for(const row of e.cameras){
    const existing=previous.find(t=>t.id===row.id);const deviceId=existing?.deviceId||row.id+'-device';const userId=existing?.userId||row.id+'-user';
    c.devices.push({...row.device,id:deviceId,name:row.name.trim(),audio:false});c.tabs.push({...existing,id:row.id,name:row.name.trim(),kind:'Camera',deviceId,userId});
    if(!existing)users.push({id:userId,name:row.name.trim(),password:row.password,admin:false});else if(row.changed)changes[userId]=row.password;
  }
  c.tabs.push(audioTab);
  for(const row of e.audio){const old=e.config.devices?.find(d=>d.id===row.id);c.devices.push({...old,id:row.id,name:row.name.trim(),audio:true});c.audioSources.push({...row.source,deviceId:row.id,pattern:row.source?.pattern||'',channels:row.source?.channels||1,channelNames:row.source?.channelNames||[row.name.trim()]});}
  c.autoClear=false;c.width=3840;c.height=2160;c.fps=50;c.revision=c.revision||1;c.proxyPreset=c.proxyPreset||'dit:proxy-720p';
  const body={config:c,users,passwordChanges:changes};
  if(e.editing)await api('PUT','/setup',body);else await dit.api('POST','/projects',body);
  const id=c.id;state.editor=null;state.editorPage='storage';state.view='home';await projects(id);message('Zapisano konfigurację.');
}
document.addEventListener('click',event=>{const node=event.target.closest('[data-act]');if(!node)return;guard(async()=>{
  const action=node.dataset.act;
  if(action==='new'){stopPlayer();state.editor=null;state.editorPage='storage';return editor();}
  if(action==='tab')return openTab(node.dataset.id);
  if(action==='navigate')return navigate(node.dataset.view);
  if(action==='home')return navigate('home');
  if(action==='lock'){await dit.api('POST','/session/lock-all',{});state.sessions.clear();stopPlayer();return state.project?renderLogin():projects();}
  if(action==='password'){const actor=session();return modal('Zmień hasło',`${!actor.admin?'<label>Aktualne hasło<input id="current-password" type="password" autocomplete="current-password"></label>':''}<label>Nowe hasło<input id="new-password" type="password" autocomplete="new-password"></label>`,async()=>{await api('POST','/password',{userId:actor.userId,currentPassword:$('#current-password')?.value||'',newPassword:$('#new-password').value});state.sessions.delete(`${state.project.id}:${state.tab.id}`);renderLogin();});}
  if(action==='source-folder'){const files=await pick('folder');if(files.length){state.source=files[0];state.files=null;$('#source').value=state.source;}return;}
  if(action==='source-files'){const files=await pick('audio');if(files.length){const parent=files[0].slice(0,files[0].lastIndexOf('/'));if(files.some(f=>f.slice(0,f.lastIndexOf('/'))!==parent))throw Error('Wybierz pliki z jednego folderu.');state.source=parent;state.files=files;$('#source').value=parent+' · '+files.length+' plików';}return;}
  if(action==='ingest'){const source=state.files?state.source:$('#source').value.trim();const audioDeviceId=$('#audio-source')?.value||null;if(state.tab.kind==='Audio'&&!audioDeviceId)throw Error('Wybierz konkretne źródło audio z listy.');await api('POST','/ingest',{source,audioDeviceId,files:state.files});message('Dodano zgrywanie do kolejki.');return refreshFootage();}
  if(action==='asset')return selectAsset(node.dataset.id);
  if(action==='play'){const v=$('#video');if(!v.src)return;return v.paused?v.play():v.pause();}
  if(action==='previous'||action==='next'){const i=state.rows.findIndex(r=>r.asset.id===state.selected);const target=state.rows[i+(action==='next'?1:-1)];if(target)return selectAsset(target.asset.id);return;}
  if(action==='pause'||action==='resume'){await api('POST','/'+action,{});await overview();return message(action==='pause'?'Kolejka wstrzymana.':'Kolejka wznowiona.');}
  if(action==='approve'){await api('POST','/approve/'+encodeURIComponent(node.dataset.id),{});return refreshFootage();}
  if(action==='day')return daySummary(node.dataset.id);
  if(action==='open-day'||action==='close-day')return dayDialog(action==='close-day');
  if(action==='adobe-connect')return message(await dit.connectAdobe(state.project.id,session().token));
  if(action==='export'){if(await dit.exportReport(state.project.id,session().token,node.dataset.csv==='true'))message('Zapisano raport.');return;}
  if(action==='resolve')return modal('Notatka do alertu','<label>Opis rozwiązania / komentarz<textarea id="alert-note"></textarea></label>',async()=>{await api('POST','/alerts/'+encodeURIComponent(node.dataset.id)+'/resolve',{resolved:node.dataset.resolved==='true',note:$('#alert-note').value});await renderView();});
  if(action==='cancel-dialog')return $('#dialog').close();
  if(action==='cancel-editor'){state.editor=null;state.view='home';return state.project?renderView():projects();}
  if(action==='editor-page'){state.editorPage=node.dataset.page;return editor();}
  if(action==='add-camera'||action==='add-audio'){state.editor[action==='add-camera'?'cameras':'audio'].push(configRow(action==='add-camera'?'camera':'audio'));return renderConfigRows();}
  if(action==='remove-config-row'){state.editor[node.dataset.row].splice(Number(node.dataset.index),1);return renderConfigRows();}
  if(action==='config-path'||action==='profile-path'){const paths=await pick(node.dataset.kind||'folder');if(paths.length){const key=node.dataset.key;state.editor.config[key]=key.endsWith('Roots')?[...state.editor.config[key],paths[0]]:paths[0];editor();}return;}
  if(action==='save-editor')return saveEditor();
});});
$('#dialog-form').addEventListener('submit',async event=>{event.preventDefault();try{await dialogSave();$('#dialog').close();}catch(error){$('#dialog-error').textContent=error.message;}});
$('#project').addEventListener('change',()=>guard(async()=>{if(session())await dit.api('POST','/session/leave',{},session().token);stopPlayer();state.project=state.projects.find(p=>p.id===$('#project').value);state.tab=null;await projects(state.project.id);}));
window.addEventListener('popstate',event=>{if(session()?.admin&&event.state?.view)guard(()=>navigate(event.state.view,false));});
window.addEventListener('mouseup',event=>{if(event.button===3){event.preventDefault();history.back();}else if(event.button===4){event.preventDefault();history.forward();}});
let lastActivity=0;for(const event of ['pointerdown','keydown','wheel'])window.addEventListener(event,()=>{if(Date.now()-lastActivity>10000){lastActivity=Date.now();dit.api('POST','/session/activity',{}).catch(()=>{});}});
window.addEventListener('blur',()=>{if(session()&&!state.nativeDialog)dit.api('POST','/session/leave',{},session().token).catch(()=>{});});
window.addEventListener('focus',()=>{if(session())guard(()=>dit.api('POST','/session/enter',{},session().token));});
setInterval(()=>guard(async()=>{if(state.busy)return;state.busy=true;try{await overview();if(!session()||state.editor)return;if(state.view==='footage')await refreshFootage();else if(['stats','adobe'].includes(state.view)){state.admin=await api('GET','/admin');renderAdmin();}}finally{state.busy=false;}}),2000);
const ready=guard(async()=>{const info=await dit.ready();$('#version').textContent=`DIT ${info.version} · macOS`;await projects();message('Gotowe. Kopie i kolejka działają w tle.');});
window.runSmoke=async()=>{await ready;if(!$('#workspace').textContent.includes('Witaj w DIT'))throw Error('Fresh workspace did not render');editor();state.editorPage='team';editor();if(!$('#audio-config input')||!$('#camera-config input'))throw Error('Camera and audio editor failed');state.editor=null;await projects();return {passed:['Packaged Electron renderer starts with sandbox and context isolation','Native self-contained Mac worker starts and answers authenticated API','Fresh workspace renders without importing user data','Camera and audio source forms render']};};
