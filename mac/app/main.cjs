const {app,BrowserWindow,ipcMain,dialog,protocol,net,shell,Menu} = require('electron');
const fs = require('node:fs/promises');
const path = require('node:path');
const {pathToFileURL} = require('node:url');
const {spawn} = require('node:child_process');
const {randomUUID} = require('node:crypto');
const {validateApi,validateAudio,AUDIO} = require('./policy.cjs');
protocol.registerSchemesAsPrivileged([{scheme:'dit-media',privileges:{secure:true,standard:true,stream:true,supportFetchAPI:true}}]);
const data = process.env.DIT_TEST_DATA || path.join(app.getPath('appData'),'DIT');
const resources = process.env.DIT_PAYLOAD || path.join(process.resourcesPath,'dit');
const port = Number(process.env.DIT_TEST_PORT || 47831);
const media = new Map();
let window, worker, log, localKey, startup, quitting = false;
if(!app.requestSingleInstanceLock())app.quit();
else {
  app.on('second-instance',()=>{if(window){window.show();window.focus();}});
  app.whenReady().then(start).catch(error=>{dialog.showErrorBox('DIT — uruchamianie',error.message);app.exit(1);});
}
async function api(method,route,body,token) {
  validateApi(method,route);
  const response = await fetch(`http://127.0.0.1:${port}${route}`,{method,headers:{'X-DIT-Key':localKey||'','Content-Type':'application/json',...(token?{'X-DIT-Session':token}:{})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(120000)});
  const text = await response.text();
  if (!response.ok) {
    if(response.status===403)media.clear();
    let message = text;try{message=JSON.parse(text).error||text;}catch{}
    throw Error(`${response.status}: ${message||response.statusText}`);
  }
  if(route.startsWith('/session/lock'))media.clear();
  if(!text)return null;
  return response.headers.get('content-type')?.includes('json')?JSON.parse(text):text;
}
async function startWorker() {
  await fs.mkdir(data,{recursive:true,mode:0o700});
  try {localKey=(await fs.readFile(path.join(data,'local.key'),'utf8')).trim();await api('GET','/health');return;} catch {}
  log=await fs.open(path.join(data,'worker.log'),'a',0o600);
  worker=spawn(path.join(resources,'worker','Dit.Mac.Worker'),['--data',data,'--listen',`http://127.0.0.1:${port}`,'--ffmpeg',path.join(resources,'worker','ffmpeg'),'--ffprobe',path.join(resources,'worker','ffprobe'),'--sevenzip',path.join(resources,'worker','7zz')],{stdio:['ignore',log.fd,log.fd],detached:false});
  let spawnError;worker.once('error',error=>{spawnError=error;});
  for(let attempt=0;attempt<100;attempt++) {
    if(spawnError)throw spawnError;
    if(worker.exitCode!==null)throw Error(`Proces roboczy zakończył się kodem ${worker.exitCode}. Log: ${path.join(data,'worker.log')}`);
    try{localKey=(await fs.readFile(path.join(data,'local.key'),'utf8')).trim();await api('GET','/health');return;}catch{}
    await new Promise(resolve=>setTimeout(resolve,200));
  }
  throw Error(`Nie uruchomiono procesu roboczego. Sprawdź ${path.join(data,'worker.log')}`);
}
function trusted(event) {if(!window||event.sender!==window.webContents||event.senderFrame!==window.webContents.mainFrame)throw Error('Nieznane okno.');}
function handle(name,fn) {ipcMain.handle(name,async(event,...args)=>{trusted(event);return fn(...args);});}
async function start() {
  protocol.handle('dit-media',request=>{
    const target=media.get(new URL(request.url).hostname);
    return target?net.fetch(pathToFileURL(target).href,{headers:request.headers}):new Response('Nieznany plik',{status:404});
  });
  window = new BrowserWindow({width:1440,height:940,minWidth:1050,minHeight:720,title:'DIT — Media Workspace',show:!process.env.DIT_SMOKE,backgroundColor:'#0e131a',webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true}});
  window.webContents.setWindowOpenHandler(()=>({action:'deny'}));
  window.webContents.on('will-navigate',event=>event.preventDefault());
  window.on('closed',()=>{window=null;});
  handle('ready',async()=>{await startup;return {version:app.getVersion(),data};});
  handle('api',api);
  handle('pick',async kind=>{
    if(!['folder','audio','files','preset','sequence','template'].includes(kind))throw Error('Nieznany wybór plików.');
    const extensions=kind==='audio'?[...AUDIO].map(x=>x.slice(1)):kind==='preset'?['epr']:kind==='sequence'?['sqpreset']:kind==='template'?['prproj']:null;
    const result=await dialog.showOpenDialog(window,{properties:kind==='folder'?['openDirectory','createDirectory']:['openFile',...(['files','audio'].includes(kind)?['multiSelections']:[])],...(extensions?{filters:[{name:kind==='audio'?'Pliki audio':'Plik Adobe',extensions}]}:{})});
    if(result.canceled)return [];
    return kind==='audio'?validateAudio(result.filePaths):result.filePaths;
  });
  handle('preview',async(project,asset,token,thumbnail)=>{
    const result=await api('POST',`/projects/${encodeURIComponent(project)}/preview`,{assetId:asset,thumbnail:!!thumbnail,lightweight:true},token);
    if(!result.path)return result;
    const id=randomUUID();media.set(id,result.path);
    // Keep the capability map bounded while a long shoot is browsed.
    if(media.size>300)media.delete(media.keys().next().value);
    return {state:result.state,url:`dit-media://${id}/media`};
  });
  handle('adobe',async(project,token)=>{
    const {key}=await api('GET',`/projects/${encodeURIComponent(project)}/adobe-key`,undefined,token);
    const connection=path.join(data,'DIT.ditconnection');
    await fs.writeFile(connection,JSON.stringify({url:`http://127.0.0.1:${port}`,key}),{mode:0o600});
    await fs.chmod(connection,0o600);
    const error=await shell.openPath(path.join(resources,'adobe','DIT-Premiere.ccx'));if(error)throw Error(error);
    const status=await api('GET',`/projects/${encodeURIComponent(project)}/adobe-status`,undefined,token);
    if(status.premiere)await shell.openPath(status.premiere);
    shell.showItemInFolder(connection);
    return 'Zainstaluj panel w Creative Cloud. W Premiere otwórz Okno → Wtyczki UXP → DIT i jednorazowo wybierz pokazany plik DIT.ditconnection. Połączenie zostanie zapamiętane.';
  });
  handle('export',async(project,token,csv)=>{
    const result=await api('GET',`/projects/${encodeURIComponent(project)}/report${csv?'.csv':''}`,undefined,token);
    const chosen=await dialog.showSaveDialog(window,{defaultPath:`DIT-raport.${csv?'csv':'json'}`});
    if(chosen.canceled)return false;
    await fs.writeFile(chosen.filePath,csv?result:JSON.stringify(result,null,2));return true;
  });
  const template=[{label:'DIT',submenu:[{role:'about'},{type:'separator'},{role:'hide'},{role:'hideOthers'},{role:'unhide'},{type:'separator'},{role:'quit'}]},{role:'editMenu'},{role:'viewMenu'},{role:'windowMenu'}];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
  startup=startWorker();startup.catch(()=>{});
  await window.loadFile(path.join(__dirname,'ui','index.html'));
  if(process.env.DIT_SMOKE){
    try {
      await startup;
      const result=await window.webContents.executeJavaScript('window.runSmoke()');
      await new Promise(resolve=>setTimeout(resolve,300));
      await fs.writeFile(process.env.DIT_SMOKE+'.png',(await window.webContents.capturePage()).toPNG());
      await fs.writeFile(process.env.DIT_SMOKE,JSON.stringify(result,null,2));
      worker?.kill('SIGTERM');app.exit(0);
    }catch(error){await fs.writeFile(process.env.DIT_SMOKE,JSON.stringify({error:error.stack}));worker?.kill('SIGTERM');app.exit(1);}
  }
}
app.on('window-all-closed',()=>{if(!process.env.DIT_SMOKE)app.quit();});
// Closing the UI leaves secured-copy work running, as in the Windows application.
// The worker exits with the login session; the next launch reconnects to its key.
app.on('before-quit',()=>{quitting=true;if(worker&&!process.env.DIT_SMOKE){worker.unref();}media.clear();});
