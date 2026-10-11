using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dit.Core;
using Dit.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["listen"]??"http://127.0.0.1:47831");
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=16*1024*1024);
var data=builder.Configuration["data"]??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DIT");
Directory.CreateDirectory(data);
using var instance=new Mutex(false,"Local\\DIT-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToUpperInvariant()))));
if(!instance.WaitOne(0))return;
string Tool(string name)=>builder.Configuration[name]??(File.Exists(Path.Combine(AppContext.BaseDirectory,name+".exe"))?Path.Combine(AppContext.BaseDirectory,name+".exe"):name);
string Key(string name)
{
    var path=Path.Combine(data,name);
    if(!File.Exists(path)) File.WriteAllText(path,Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    return File.ReadAllText(path).Trim();
}
var localKey=Key("local.key");var adobeKey=Key("adobe.key");
var adobeGate=new object();var adobeLastSeen=DateTimeOffset.MinValue;
builder.Services.ConfigureHttpJsonOptions(o=> {o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());});
builder.Services.AddSingleton<IStore>(_=>new SqliteStore(Path.Combine(data,"dit.sqlite")));
builder.Services.AddSingleton<IClock,SystemClock>();builder.Services.AddSingleton<IVolumeInspector,WindowsVolumes>();
builder.Services.AddSingleton<IMediaProbe>(_=>new Ffprobe(Tool("ffprobe")));
builder.Services.AddSingleton(_=>new SevenZip(builder.Configuration["sevenzip"]??Tool("7z")));builder.Services.AddSingleton<IFaultInjector,NoFaults>();
builder.Services.AddSingleton<Sessions>();builder.Services.AddSingleton<IngestEngine>();builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<RecoveryService>();
builder.Services.AddSingleton(sp=>new AudioSynchronizer(sp.GetRequiredService<IStore>(),sp.GetRequiredService<IngestEngine>(),Tool("ffmpeg")));
builder.Services.AddSingleton(sp=>new PreviewService(sp.GetRequiredService<IStore>(),Tool("ffmpeg")));
builder.Services.AddHostedService<QueueRunner>();if(builder.Configuration["adobe-auto-launch"]!="false")builder.Services.AddHostedService<AdobeLauncher>();
builder.Services.AddRateLimiter(o=>o.AddFixedWindowLimiter("login",l=> {l.PermitLimit=60;l.Window=TimeSpan.FromMinutes(1);l.QueueLimit=0;}));
var app=builder.Build();
var startupStore=app.Services.GetRequiredService<IStore>();
AdobeSetup.EnsureDayProjects(startupStore);
foreach(var config in startupStore.All<ProjectConfig>("project"))
{
    var upgraded=LegacyAudioDevices.Upgrade(config);if(ReferenceEquals(config,upgraded))continue;
    Configuration.Validate(upgraded);
    startupStore.Atomic(()=>{startupStore.Put("project",config.Id,upgraded);app.Services.GetRequiredService<IngestEngine>().WriteAudit(config.Id,"system","audio-sources-upgraded",$"Utworzono {upgraded.AudioSources.Length} osobnych mikroportów ze starego ustawienia Audio. Historyczne nagrania zachowano.");});
}
app.Use(async(context,next)=>
{
    try
    {
        if(context.Connection.RemoteIpAddress is not {} ip || !IPAddress.IsLoopback(ip)) {context.Response.StatusCode=403;return;}
        var isAdobe=context.Request.Path.StartsWithSegments("/adobe");
        var supplied=context.Request.Headers[isAdobe?"X-DIT-Adobe":"X-DIT-Key"].ToString();
        var expected=isAdobe?adobeKey:localKey;
        if(supplied.Length!=expected.Length||!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied),System.Text.Encoding.UTF8.GetBytes(expected))) {context.Response.StatusCode=401;return;}
        if(isAdobe){lock(adobeGate)adobeLastSeen=DateTimeOffset.UtcNow;}
        if(isAdobe&&context.Request.Method=="POST")
        {
            var parts=context.Request.Path.Value!.Split('/',StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length==3&&app.Services.GetRequiredService<IStore>().Get<AdobeCommand>("adobe",Uri.UnescapeDataString(parts[1])) is {} active)
            {
                if(active.State!="inflight"||active.RunId is not null&&context.Request.Headers["X-DIT-Run"].ToString()!=active.RunId){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new {error="Operację przejął nowy panel DIT lub została już zakończona. Stare wykonanie zostało zatrzymane."});return;}
                if(active.RunId is not null)app.Services.GetRequiredService<IStore>().Put("adobe-lease",active.Id,new AdobeLease(active.RunId,DateTimeOffset.UtcNow));
            }
        }
        await next();
    }
    catch(AccessException ex) {context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new {error=ex.Message});}
    catch(Exception ex) when(ex is ArgumentException or SafetyException or InvalidOperationException or IOException)
    {context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new {error=ex.Message});}
});
app.UseRateLimiter();
SessionView Auth(HttpContext h,ProjectService service,string id,bool admin=false)=>service.Require(h.Request.Headers["X-DIT-Session"].ToString(),id,admin);
app.MapGet("/health",()=>new {status="ok",version=typeof(ProjectService).Assembly.GetName().Version!.ToString(3),adobeQualified=false});
app.MapGet("/projects",(IStore db)=>db.All<ProjectConfig>("project").Select(c=>new {c.Id,c.Name,c.Tabs}));
app.MapGet("/projects/{id}/overview",(string id,IStore db,IngestEngine e)=>{if(db.Get<ProjectConfig>("project",id) is null)return Results.NotFound();return Results.Json(ActivityContext.DayStatus(db.All<ShootingDay>("day").Where(d=>d.ProjectId==id).ToArray(),e.Paused));});
app.MapPost("/projects",(CreateProjectRequest request,ProjectService service)=>service.Create(request));
app.MapPost("/login",(LoginRequest request,ProjectService service)=>service.Login(request)).RequireRateLimiting("login");
app.MapPost("/session/activity",(Sessions sessions)=> {sessions.Activity();return Results.Ok();});
app.MapPost("/session/lock",(HttpContext h,Sessions sessions)=> {sessions.Lock(h.Request.Headers["X-DIT-Session"].ToString());return Results.Ok();});
app.MapPost("/session/lock-all",(Sessions sessions)=> {sessions.LockAll();return Results.Ok();});
app.MapPost("/session/leave",(HttpContext h,Sessions sessions)=> {sessions.Leave(h.Request.Headers["X-DIT-Session"].ToString());return Results.Ok();});
app.MapPost("/session/enter",(HttpContext h,Sessions sessions)=> {sessions.Enter(h.Request.Headers["X-DIT-Session"].ToString());return Results.Ok();});
app.MapGet("/projects/{id}/config",(string id,HttpContext h,ProjectService s,IStore db)=> {Auth(h,s,id,true);return db.Get<ProjectConfig>("project",id);});
app.MapPut("/projects/{id}/config",(string id,ProjectConfig c,HttpContext h,ProjectService s)=> {if(id!=c.Id)throw new ArgumentException("Niezgodny projekt.");return s.Update(c,Auth(h,s,id,true));});
app.MapPut("/projects/{id}/setup",(string id,CreateProjectRequest r,HttpContext h,ProjectService s)=> {if(id!=r.Config.Id)throw new ArgumentException("Niezgodny projekt.");return s.Configure(r,Auth(h,s,id,true));});
app.MapPost("/projects/{id}/password",(string id,PasswordRequest r,HttpContext h,ProjectService s)=> {s.Password(id,Auth(h,s,id),r);return Results.Ok();});
app.MapGet("/projects/{id}/assets",(string id,HttpContext h,ProjectService s)=>s.Visible(id,Auth(h,s,id)));
app.MapGet("/projects/{id}/workflow",(string id,HttpContext h,ProjectService s)=>s.Workflow(id,Auth(h,s,id)));
app.MapGet("/projects/{id}/audio-inputs",(string id,HttpContext h,ProjectService s,IStore db)=>{var actor=Auth(h,s,id);var c=db.Get<ProjectConfig>("project",id)!;if(!actor.Admin&&c.Tabs.Single(t=>t.Id==actor.TabId).Kind!=TabKind.Audio)throw new AccessException();return c.AudioSources.Select(a=>new {a.DeviceId,Name=c.Devices.Single(d=>d.Id==a.DeviceId).Name,a.Channels,a.ChannelNames});});
app.MapGet("/projects/{id}/days/{day}/statistics",(string id,string day,HttpContext h,ProjectService s,IClock clock)=>{Auth(h,s,id,true);return s.DayStats(id,day,clock);});
app.MapPost("/projects/{id}/preview",async(string id,PreviewRequest request,HttpContext h,ProjectService service,PreviewService preview,IStore db,CancellationToken ct)=>
{
    var actor=Auth(h,service,id);var asset=service.Visible(id,actor).Single(a=>a.Id==request.AssetId);
    if(!asset.Main.Verified)throw new SafetyException("Nagranie nie jest zweryfikowane.");
    var config=db.Get<ProjectConfig>("project",id)!;
    return request.Thumbnail?await preview.Thumbnail(asset,config,ct):preview.Request(asset,config,false,request.Lightweight);
});
app.MapPost("/projects/{id}/restore",async(string id,RestoreRequest request,HttpContext h,ProjectService service,IStore db,RecoveryService recovery,CancellationToken ct)=>
{
    var actor=Auth(h,service,id,true);var batch=db.Get<Batch>("batch",request.BatchId)??throw new ArgumentException("Nieznane zgrywanie.");if(batch.ProjectId!=id)throw new AccessException();
    await recovery.Restore(batch.Id,request.Destination,ct);return Results.Ok();
});
app.MapPost("/projects/{id}/assign",async(string id,AssignmentRequest request,HttpContext h,ProjectService service,RecoveryService recovery,IStore db,CancellationToken ct)=>
{
    var actor=Auth(h,service,id,true);if(db.Get<Asset>("asset",request.AssetId)?.ProjectId!=id)throw new AccessException();
    return await recovery.Assign(request.AssetId,request.DayId,request.DeviceId,actor.UserId,ct);
});
app.MapPost("/projects/{id}/sync",async(string id,SyncRequest request,HttpContext h,ProjectService service,IngestEngine engine,IStore db,CancellationToken ct)=>
{
    var actor=Auth(h,service,id,true);var asset=db.Get<Asset>("asset",request.AssetId)??throw new ArgumentException("Nieznane nagranie.");
    if(asset.ProjectId!=id||request.PositionTicks<0)throw new ArgumentException("Niepoprawna korekta synchronizacji.");
    lock(adobeGate)
    {
        if(db.All<AdobeCommand>("adobe").Any(c=>c.ProjectId==id&&c.State is "inflight" or "analyzing"))throw new SafetyException("Zakończ analizę lub aktywny cykl Adobe przed korektą synchronizacji.");
        db.Put("asset",asset.Id,asset with {PositionTicks=request.PositionTicks,SyncMethod="manual-admin"});engine.WriteAudit(id,actor.UserId,"sync-correction",asset.Id+"="+request.PositionTicks);
        foreach(var command in db.All<AdobeCommand>("adobe").Where(c=>c.ProjectId==id&&c.DayId==asset.DayId))db.Put("adobe",command.Id,command with {State="superseded"});
    }
    var batch=db.Get<Batch>("batch",asset.BatchId)!;
    await engine.ScheduleAdobe(batch,ct);return Results.Ok();
});
app.MapGet("/projects/{id}/batches",(string id,HttpContext h,ProjectService s,IStore db)=> {var actor=Auth(h,s,id);return db.All<Batch>("batch").Where(b=>b.ProjectId==id&&(actor.Admin||b.OwnerId==actor.UserId)).Select(b=>new {b.Id,b.CreatedAt,b.OwnerId,b.Stage,b.Error,b.ClearDone,SourceRoot=b.Source.Root,TotalFiles=b.Source.Entries.Length,Progress=db.Get<TransferProgress>("progress",b.Id),Files=db.All<Asset>("asset").Count(a=>a.BatchId==b.Id)});});
app.MapPost("/projects/{id}/ingest",(string id,IngestRequest r,HttpContext h,ProjectService s,IStore db,IngestEngine e)=>e.Enqueue(db.Get<ProjectConfig>("project",id)!,Auth(h,s,id),r.Source,r.AudioDeviceId,r.Files));
app.MapGet("/projects/{id}/days",(string id,HttpContext h,ProjectService s,IStore db)=> {Auth(h,s,id);return db.All<ShootingDay>("day").Where(d=>d.ProjectId==id);});
app.MapPost("/projects/{id}/days",(string id,DayRequest r,HttpContext h,ProjectService s)=>s.OpenDay(id,r.At,Auth(h,s,id,true)));
app.MapPost("/projects/{id}/days/{day}/close",(string id,string day,DayRequest r,HttpContext h,ProjectService s,IStore db)=> {if(db.Get<ShootingDay>("day",day)?.ProjectId!=id)throw new AccessException();return s.CloseDay(day,r.At,Auth(h,s,id,true));});
app.MapGet("/projects/{id}/admin",(string id,HttpContext h,ProjectService s,IStore db,IngestEngine e,IVolumeInspector v)=>
{
    Auth(h,s,id,true);var config=db.Get<ProjectConfig>("project",id)!;
    var batches=db.All<Batch>("batch").Where(b=>b.ProjectId==id).ToArray();
    return new {Statistics=s.Stats(id),DeviceNames=config.Devices.ToDictionary(d=>d.Id,d=>d.Name),Alerts=db.All<Alert>("alert").Where(a=>a.ProjectId==id).Select(a=>ActivityContext.Enrich(a,config,batches)),Audit=db.All<AuditEvent>("audit").Where(a=>a.ProjectId==id).Select(a=>ActivityContext.Enrich(a,config,batches)),Pause=db.Get<PauseState>("system","pause"),Adobe=db.All<AdobeCommand>("adobe").Where(c=>c.ProjectId==id).Select(c=>new {c.Id,c.State,c.Error,Progress=db.Get<AdobeProgress>("adobe-progress",c.Id)}),Volumes=config.MainRoots.Concat(config.BackupRoots).Select(path=> {try {return new {Path=path,Info=(VolumeInfo?)v.Inspect(path),Error=(string?)null};} catch(Exception ex){return new {Path=path,Info=(VolumeInfo?)null,Error=(string?)ex.Message};}})};
});
app.MapPost("/projects/{id}/pause",(string id,HttpContext h,ProjectService s,IngestEngine e)=> {Auth(h,s,id,true);e.Pause("Pauza administratora");return Results.Ok();});
app.MapPost("/projects/{id}/resume",(string id,HttpContext h,ProjectService s,IngestEngine e,IStore db)=>
{
    var actor=Auth(h,s,id,true);
    if(db.All<Batch>("batch").Any(b=>b.ClearStarted&&!b.ClearDone))throw new SafetyException("Przerwane usuwanie wymaga inspekcji nośnika; automatyczne wznowienie jest zablokowane.");
    foreach(var b in db.All<Batch>("batch").Where(b=>b.ProjectId==id&&b.Stage is BatchStage.Failed or BatchStage.Paused)) db.Put("batch",b.Id,b with {Stage=BatchStage.Queued,Error=null});
    AdobeLeases.RecoverExpired(db,DateTimeOffset.UtcNow);
    foreach(var command in db.All<AdobeCommand>("adobe").Where(c=>c.ProjectId==id&&(c.State=="failed"||c.State=="inflight"&&c.RunId is null)))db.Put("adobe",command.Id,command with {State="pending",Error=null,RunId=null});
    e.Resume();e.WriteAudit(id,actor.UserId,"resume","global");return Results.Ok();
});
app.MapPost("/projects/{id}/approve/{batchId}",async(string id,string batchId,HttpContext h,ProjectService s,IngestEngine e,IStore db,CancellationToken ct)=>
{
    var actor=Auth(h,s,id,true);var batch=db.Get<Batch>("batch",batchId)??throw new ArgumentException("Nieznane zgrywanie.");if(batch.ProjectId!=id)throw new AccessException();
    db.Put("approval",batchId,true);e.WriteAudit(id,actor.UserId,"late-approved",batchId);await e.ScheduleAdobe(batch,ct);return Results.Ok();
});
app.MapPost("/projects/{id}/alerts/{key}/note",(string id,string key,NoteRequest r,HttpContext h,ProjectService s,IStore db)=> {var a=Auth(h,s,id,true);if(db.Get<Alert>("alert",key)?.ProjectId!=id)throw new AccessException();s.NoteAlert(key,r.Note,a);return Results.Ok();});
app.MapPost("/projects/{id}/alerts/{key}/resolve",(string id,string key,AlertResolutionRequest request,HttpContext h,ProjectService s,IClock clock)=>s.ResolveAlert(id,key,request,Auth(h,s,id,true),clock));
app.MapGet("/projects/{id}/report",(string id,HttpContext h,ProjectService s,IStore db)=> {Auth(h,s,id,true);return Results.Json(new {Project=id,Statistics=s.Stats(id),Assets=db.All<Asset>("asset").Where(a=>a.ProjectId==id),Audit=db.All<AuditEvent>("audit").Where(a=>a.ProjectId==id)},SqliteStore.Json);});
app.MapGet("/projects/{id}/report.csv",(string id,HttpContext h,ProjectService s)=>
{
    Auth(h,s,id,true);string Escape(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    var lines=s.Stats(id).Select(x=>string.Join(",",Escape(x.DeviceId),x.Clips,x.OriginalBytes,x.BackupBytes,x.ProxyBytes,x.DurationTicks,x.UnknownDurations));
    return Results.Text("Device,Clips,OriginalBytes,BackupBytes,ProxyBytes,DurationTicks,UnknownDurations\r\n"+string.Join("\r\n",lines),"text/csv",System.Text.Encoding.UTF8);
});
app.MapGet("/projects/{id}/adobe-key",(string id,HttpContext h,ProjectService s)=> {Auth(h,s,id,true);return new {key=adobeKey};});
app.MapGet("/projects/{id}/adobe-status",(string id,HttpContext h,ProjectService s)=>{Auth(h,s,id,true);lock(adobeGate)return new {Connected=DateTimeOffset.UtcNow-adobeLastSeen<TimeSpan.FromSeconds(15),LastSeen=adobeLastSeen==DateTimeOffset.MinValue?(DateTimeOffset?)null:adobeLastSeen,Premiere=AdobeSetup.PremiereExecutable(),Encoder=AdobeSetup.EncoderExecutable()};});
// Only the dedicated Adobe key can reach these endpoints; no shell or arbitrary script execution is exposed.
app.MapGet("/adobe/status",(IStore db,IngestEngine e)=>
{
    lock(adobeGate)
    {
        adobeLastSeen=DateTimeOffset.UtcNow;if(!db.Get<bool>("system","adobe-paired"))db.Put("system","adobe-paired",true);
        var commands=db.All<AdobeCommand>("adobe");
        var pending=commands.Where(c=>c.State=="pending").ToArray();
        var reasons=pending.Select(c=>db.Get<ProjectConfig>("project",c.ProjectId) is {} current?AdobeSetup.RefreshPresets(c,current):c).Select(AdobeSetup.WaitingReason).Where(r=>r is not null).Distinct().ToArray();
        var message=e.Paused?"Połączono z DIT. Kolejka wstrzymana — wznów ją w panelu administratora DIT.":commands.Any(c=>c.State=="inflight")?"Połączono z DIT. Trwa import, aktualizacja timeline lub tworzenie proxy.":pending.Length>0&&reasons.Length>0?"Połączono z DIT. "+string.Join(" ",reasons):commands.Any(c=>c.State=="failed")?"Połączono z DIT. Zadanie Adobe zakończone błędem — sprawdź sekcję Premiere i proxy w DIT.":"Połączono z DIT. Oczekiwanie na zadania; otwórz dzień zdjęciowy w DIT, aby utworzyć sekwencję dnia w projekcie produkcji.";
        return Results.Json(new {message,paused=e.Paused,pending=pending.Length});
    }
});
app.MapGet("/adobe/next",(IStore db,IngestEngine e)=>
{
    lock(adobeGate)
    {
        adobeLastSeen=DateTimeOffset.UtcNow;if(!db.Get<bool>("system","adobe-paired"))db.Put("system","adobe-paired",true);
        AdobeLeases.RecoverExpired(db,DateTimeOffset.UtcNow);
        if(e.Paused||db.All<AdobeCommand>("adobe").Any(c=>c.State=="inflight"))return Results.NoContent();
        foreach(var pending in db.All<AdobeCommand>("adobe").Where(c=>c.State=="pending").OrderBy(c=>db.Get<Batch>("batch",c.Id.Split(':')[0])?.CreatedAt).ThenBy(c=>c.Id,StringComparer.Ordinal))
        {
            var current=db.Get<ProjectConfig>("project",pending.ProjectId);var command=current is null?pending:AdobeSetup.RefreshPresets(pending,current);
            var waiting=AdobeSetup.WaitingReason(command);
            if(waiting is not null){db.Put("adobe",command.Id,command with {Error=waiting});continue;}
            var claimed=command with {State="inflight",Error=null,RunId=Guid.NewGuid().ToString("N")};db.Put("adobe",command.Id,claimed);db.Put("adobe-lease",command.Id,new AdobeLease(claimed.RunId!,DateTimeOffset.UtcNow));return Results.Json(claimed,SqliteStore.Json);
        }
        return Results.NoContent();
    }
});
app.MapPost("/adobe/{id}/heartbeat",(string id,IStore db)=>db.Get<AdobeCommand>("adobe",id)?.State=="inflight"?Results.Ok():Results.BadRequest());
app.MapPost("/adobe/{id}/prepare",async(string id,IStore db,IMediaProbe probe,CancellationToken ct)=>
{
    var command=db.Get<AdobeCommand>("adobe",id)??throw new ArgumentException("Nieznane polecenie.");
    var refreshed=new List<Asset>();
    foreach(var snapshot in command.Assets)
    {
        var asset=db.Get<Asset>("asset",snapshot.Id)??snapshot;FileSafety.NoLinks(asset.Main.Path);
        var media=await probe.ProbeAsync(asset.Main.Path,ct)??throw new SafetyException("Nie można odczytać nagrania: "+asset.LogicalName);
        asset=asset with {Media=media with {LtcStartTicks=asset.Media?.LtcStartTicks}};db.Put("asset",asset.Id,asset);refreshed.Add(asset);
    }
    command=command with {Assets=refreshed.ToArray(),Placements=Timeline.Build(command.Config,refreshed.ToArray())};db.Put("adobe",id,command);
    Directory.CreateDirectory(command.Config.PremiereRoot);FileSafety.NoLinks(command.ProjectPath);
    var fromTemplate=false;
    if(!File.Exists(command.ProjectPath)&&command.Config.PremiereTemplate is {} template)
    {await FileSafety.CopyVerifiedAsync(template,command.ProjectPath,new NoFaults(),ct);fromTemplate=true;}
    if(File.Exists(command.ProjectPath))await FileSafety.CopyVerifiedAsync(command.ProjectPath,command.ProjectPath+"."+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")+".before.bak",new NoFaults(),ct);
    string? preset=null,waitingReason=null;
    try{preset=command.Config.PremiereTemplate is not null?null:AdobeSetup.SequencePreset(command);}catch(SafetyException ex){waitingReason=ex.Message;}
    return new {exists=File.Exists(command.ProjectPath),fromTemplate,sequencePreset=preset,waitingReason,command.Assets,command.Placements,ownedProjects=db.All<AdobeCommand>("adobe").Where(c=>c.State=="complete").Select(c=>c.ProjectPath).Distinct().ToArray()};
});
app.MapPost("/adobe/{id}/proxy-path",(string id,PreviewRequest request,IStore db)=>
{
    var command=db.Get<AdobeCommand>("adobe",id)??throw new ArgumentException("Nieznane polecenie.");var asset=command.Assets.Single(a=>a.Id==request.AssetId);
    var day=db.Get<ShootingDay>("day",command.DayId)!;
    var path=ProxyPolicy.PathFor(command,day,asset);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);return new {path,preset=ProxyPresets.ForAsset(command,asset)};
});
app.MapPost("/adobe/{id}/prepare-proxy",async(string id,ProxyRequest request,IStore db,IMediaProbe probe,CancellationToken ct)=>
{
    var command=db.Get<AdobeCommand>("adobe",id)??throw new ArgumentException("Nieznane polecenie.");var asset=command.Assets.Single(a=>a.Id==request.AssetId);
    var path=ProxyPolicy.PathFor(command,db.Get<ShootingDay>("day",command.DayId)!,asset);
    if(!string.Equals(Path.GetFullPath(request.Path),path,StringComparison.OrdinalIgnoreCase))throw new SafetyException("Niezgodna ścieżka proxy.");
    FileSafety.NoLinks(path);
    if(File.Exists(path))
    {
        try{using var guard=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);}catch(IOException){return Results.Json(new {waiting=true});}
        MediaInfo? info;
        try{info=await probe.ProbeAsync(path,ct);}catch(IOException){info=null;}
        if(ProxyPolicy.Matches(asset.Media,info))return Results.Ok();
        // Keep rejected output for diagnosis; never overwrite an existing incompatible file.
        var rejected=Paths.Under(command.Config.ProxyRoot,Path.Combine("_DIT","Niezgodne proxy",Guid.NewGuid().ToString("N")+"-"+Path.GetFileName(path)));
        Directory.CreateDirectory(Path.GetDirectoryName(rejected)!);FileSafety.NoLinks(rejected);File.Move(path,rejected);
    }
    return Results.Ok();
});
app.MapPost("/adobe/{id}/validate-proxy",async(string id,ProxyRequest request,IStore db,IMediaProbe probe,CancellationToken ct)=>
{
    var command=db.Get<AdobeCommand>("adobe",id)??throw new ArgumentException("Nieznane polecenie.");var asset=command.Assets.Single(a=>a.Id==request.AssetId);
    if(!string.Equals(Path.GetFullPath(request.Path),ProxyPolicy.PathFor(command,db.Get<ShootingDay>("day",command.DayId)!,asset),StringComparison.OrdinalIgnoreCase)||!File.Exists(request.Path))return Results.Json(new {valid=false});
    FileSafety.NoLinks(request.Path);
    try
    {
        using var guard=new FileStream(request.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var info=await probe.ProbeAsync(request.Path,ct);
        var reason=ProxyPolicy.Mismatch(asset.Media,info);var valid=reason is null;
        if(valid&&probe is Ffprobe actual&&!await actual.CompleteVideoAsync(request.Path,ct))return Results.Json(new {valid=false,ready=false,reason="Plik proxy nie zawiera jeszcze wszystkich klatek."});
        if(valid)db.Put("asset",asset.Id,db.Get<Asset>("asset",asset.Id)! with {ProxyPath=request.Path});return Results.Json(new {valid,ready=true,reason});
    }
    catch(IOException){return Results.Json(new {valid=false,ready=false,reason="Plik proxy jest jeszcze zapisywany lub nie można go odczytać."});}
});
app.MapPost("/adobe/{id}/progress",(string id,AdobeProgress request,IStore db)=>
{
    var command=db.Get<AdobeCommand>("adobe",id)??throw new ArgumentException("Nieznane polecenie.");
    if(command.State!="inflight")throw new SafetyException("Polecenie Adobe nie jest aktywne.");
    if(request.Stage is not ("import" or "timeline" or "proxy" or "complete" or "ame-event")||request.Completed<0||request.Total<0||request.Completed>request.Total||!double.IsFinite(request.Percent)||request.Percent<0||request.Percent>100||request.Event?.Length>300||request.JobId?.Length>200||request.AssetId is {} assetId&&!command.Assets.Any(a=>a.Id==assetId))throw new ArgumentException("Niepoprawny postęp Adobe.");
    db.Put("adobe-progress",id,request with {UpdatedAt=DateTimeOffset.UtcNow});
    if(request.Stage=="timeline")db.Put("adobe-timeline",id,command);
    return Results.Ok();
});
app.MapPost("/adobe/{id}/result",async(string id,AdobeResult result,IStore db,IngestEngine e,IMediaProbe probe,CancellationToken ct)=>
{
    var command=db.Get<AdobeCommand>("adobe",id)??throw new ArgumentException("Nieznane polecenie.");
    if(result.State is not ("complete" or "waiting" or "failed"))throw new ArgumentException("Nieznany stan Adobe.");
    if(result.State=="complete")
    {
        if(command.State!="inflight")throw new SafetyException("Polecenie Adobe nie jest aktywne.");
        ProxyPolicy.RequireComplete(command,result.Proxies);
        if(!File.Exists(command.ProjectPath))throw new SafetyException("Brak zapisanego projektu Premiere.");
        if(result.Proxies is not null) foreach(var (assetId,path) in result.Proxies)
        {
            var asset=command.Assets.Single(a=>a.Id==assetId);
            if(!string.Equals(Path.GetFullPath(path),ProxyPolicy.PathFor(command,db.Get<ShootingDay>("day",command.DayId)!,asset),StringComparison.OrdinalIgnoreCase))throw new SafetyException("Niezgodna ścieżka proxy.");
            FileSafety.NoLinks(path);
            var info=await probe.ProbeAsync(path,ct)??throw new SafetyException("Niepoprawne proxy.");
            if(!ProxyPolicy.Matches(asset.Media,info))throw new SafetyException("Proxy niezgodne z oryginałem.");
            db.Put("asset",asset.Id,db.Get<Asset>("asset",asset.Id)! with {ProxyPath=path});
        }
        var version=command.ProjectPath+"."+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")+".bak";
        await FileSafety.CopyVerifiedAsync(command.ProjectPath,version,new NoFaults(),ct);
    }
    db.Put("adobe",id,command with {State=result.State=="waiting"?"pending":result.State,Error=result.Error});
    var batchId=id.Split(':')[0];var batch=db.Get<Batch>("batch",batchId)!;
    if(result.State=="failed") {e.Pause(result.Error??"Błąd Adobe");e.Warn(command.ProjectId,"adobe:"+id,"critical",result.Error??"Błąd Adobe");}
    if(batch is not null&&result.State=="complete"&&db.All<AdobeCommand>("adobe").Where(c=>c.Id.StartsWith(batchId+":",StringComparison.Ordinal)).All(c=>c.State=="complete")) db.Put("batch",batchId,batch with {Stage=BatchStage.Completed});
    e.WriteAudit(command.ProjectId,"adobe","adobe-"+result.State,id);return Results.Ok();
});
await app.RunAsync();

public sealed record DayRequest(DateTimeOffset At);
public sealed record NoteRequest(string Note);
public sealed record PreviewRequest(string AssetId,bool Lightweight=true,bool Thumbnail=false);
public sealed record RestoreRequest(string BatchId,string Destination);
public sealed record AssignmentRequest(string AssetId,string DayId,string DeviceId);
public sealed record ProxyRequest(string AssetId,string Path);
public sealed record SyncRequest(string AssetId,long PositionTicks);

public sealed class QueueRunner(IStore store,IngestEngine engine,AudioSynchronizer synchronizer,PreviewService previews,RecoveryService recovery,ILogger<QueueRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        engine.RecoverLegacyStoragePause();
        var interrupted=store.All<Batch>("batch").Where(b=>b.Stage is BatchStage.Copying or BatchStage.BackingUp or BatchStage.Erasing or BatchStage.Secured).ToArray();
        if(interrupted.Length>0)
        {
            engine.Pause("Wykryto przerwane zadania. Sprawdź nośniki i wznów jako administrator.");
            foreach(var batch in interrupted) store.Put("batch",batch.Id,batch with {Stage=BatchStage.Paused});
        }
        if(store.All<AdobeCommand>("adobe").Any(c=>c.State=="inflight"&&c.RunId is null))engine.Pause("Przerwana operacja starszego panelu Adobe. Sprawdź zapisany projekt i wznów jako administrator.");
        async Task Organize()
        {
            try{await recovery.OrganizeLegacy(stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
            catch(Exception ex){engine.Pause("Nie ukończono porządkowania starszych zgrywań: "+ex.Message);logger.LogError(ex,"Naprawa układu folderów przerwana");}
        }
        await Organize();
        Dictionary<string,Task> running=[];
        Task? analysis=null;CancellationTokenSource? analysisCancellation=null;
        try
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                foreach(var pair in running.Where(p=>p.Value.IsCompleted).ToArray()) {await pair.Value;running.Remove(pair.Key);}
                if(analysis?.IsCompleted==true){await analysis;analysis=null;analysisCancellation?.Dispose();analysisCancellation=null;}
                if(!engine.Paused&&running.Count==0&&analysis is null&&recovery.HasPendingOrganization)await Organize();
                if(!engine.Paused) foreach(var batch in store.All<Batch>("batch").Where(b=>b.Stage==BatchStage.Queued&&!running.ContainsKey(b.Id)).Take(Math.Max(0,2-running.Count)))
                {
                    analysisCancellation?.Cancel();
                    running[batch.Id]=Task.Run(()=>engine.Run(batch.Id,stoppingToken),stoppingToken);
                }
                if(!engine.Paused&&running.Count==0&&analysis is null&&store.All<AdobeCommand>("adobe").FirstOrDefault(c=>c.State=="analyzing") is {} command)
                {
                    analysisCancellation=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);var token=analysisCancellation.Token;
                    analysis=Task.Run(async()=>
                    {
                        try{await synchronizer.Run(command,token);var updated=store.Get<AdobeCommand>("adobe",command.Id)!;store.Put("adobe",command.Id,updated with {State="pending"});}
                        catch(OperationCanceledException) when(token.IsCancellationRequested){}
                        catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception){engine.Warn(command.ProjectId,"audio-analysis:"+command.Id,"sync",ex.Message);store.Put("adobe",command.Id,command with {State="pending"});}
                        catch(Exception ex){engine.Pause(ex.Message);engine.Warn(command.ProjectId,"audio-conflict:"+command.Id,"critical",ex.Message);}
                    },stoppingToken);
                }
                if(!engine.Paused&&running.Count==0&&analysis is null&&store.All<PreviewJob>("preview").FirstOrDefault(p=>p.State=="pending"&&!p.Thumbnail) is {} preview)
                {
                    analysisCancellation=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);var token=analysisCancellation.Token;
                    analysis=Task.Run(async()=>
                    {
                        try{await previews.Build(preview,token);}
                        catch(OperationCanceledException)when(token.IsCancellationRequested){}
                        catch(Exception ex){store.Put("preview",preview.Id,preview with {State="failed",Error=ex.Message});}
                    },stoppingToken);
                }
                await Task.Delay(500,stoppingToken);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {logger.LogInformation("Zatrzymywanie kolejki DIT.");}
        finally {analysisCancellation?.Cancel();if(analysis is not null)await analysis;analysisCancellation?.Dispose();await Task.WhenAll(running.Values.Select(async task=> {try{await task;}catch(OperationCanceledException){}}));}
    }
}
