using System.Text.Json;
using Dit.Core;

namespace Dit.Infrastructure;

public sealed record FilePlan(string Id,string Relative,string LogicalName,string Hash,string MainPath,VolumeInfo MainVolume,string BackupPath,VolumeInfo BackupVolume,string? DeviceId,string? DayId,MediaInfo? Media,bool Late);
public sealed record PauseState(bool Paused,string? Reason);
public sealed class AudioSelectionException(string message):ArgumentException(message);

public sealed class IngestEngine(IStore store,IVolumeInspector volumes,IMediaProbe probe,IClock clock,SevenZip archive,IFaultInjector faults)
{
    private readonly StorageAllocator allocator=new(volumes);
    private readonly SemaphoreSlim planning=new(1,1);
    public bool Paused=>store.Get<PauseState>("system","pause")?.Paused==true;
    public void Pause(string reason) => store.Put("system","pause",new PauseState(true,reason));
    public void Resume() => store.Put("system","pause",new PauseState(false,null));
    private void Check(CancellationToken ct) {ct.ThrowIfCancellationRequested();if(Paused) throw new SafetyException("Proces wstrzymany przez administratora lub błąd krytyczny.");}
    public Batch Enqueue(ProjectConfig config,SessionView actor,string source,string? audioDeviceId=null,string[]? files=null)
    {
        if(Paused) throw new SafetyException("Zgrywanie wstrzymane. Administrator musi wybrać „Wznów” w panelu Admin. Powód: "+store.Get<PauseState>("system","pause")?.Reason);
        var tab=config.Tabs.Single(t=>t.Id==actor.TabId);
        if(tab.Kind==TabKind.Admin) throw new AccessException("Zgrywanie odbywa się w zakładce kamery lub Audio.");
        if(audioDeviceId is not null&&(tab.Kind!=TabKind.Audio||!config.AudioSources.Any(s=>s.DeviceId==audioDeviceId)))throw new ArgumentException("Niepoprawne źródło mikroportu.");
        if(tab.Kind==TabKind.Audio&&audioDeviceId is null&&config.AudioSources.Length!=1)throw new ArgumentException("Wybierz konkretne urządzenie audio przed rozpoczęciem zgrywania.");
        source=Path.GetFullPath(source);
        foreach(var destination in config.MainRoots.Concat(config.BackupRoots).Append(config.ProxyRoot).Append(config.PremiereRoot))
            if(Configuration.IsWithin(source,destination)||Configuration.IsWithin(destination,source)) throw new SafetyException("Źródło i miejsce docelowe pokrywają się.");
        ValidateStores(config);
        var activeDay=store.All<ShootingDay>("day").Where(d=>d.ProjectId==config.Id).OrderByDescending(d=>d.Number).FirstOrDefault()??throw new SafetyException("Najpierw otwórz dzień zdjęciowy w panelu administratora.");
        if(store.All<Batch>("batch").Any(b=>(b.Stage is BatchStage.Queued or BatchStage.Copying or BatchStage.BackingUp or BatchStage.Secured or BatchStage.Erasing or BatchStage.Paused) && (Configuration.IsWithin(source,b.Source.Root)||Configuration.IsWithin(b.Source.Root,source)))) throw new SafetyException("To źródło ma już aktywne zgrywanie. Sprawdź zakładkę Zgrywania.");
        var snapshot=FileSafety.Snapshot(source,volumes);
        if(files is not null)
        {
            if(tab.Kind!=TabKind.Audio||audioDeviceId is null||files.Length==0)throw new ArgumentException("Wskaż źródło audio i pliki mikroportu.");
            var selected=files.Select(f=>Path.GetRelativePath(source,Paths.Under(source,f))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(selected.Any(f=>!snapshot.Entries.Any(e=>e.RelativePath.Equals(f,StringComparison.OrdinalIgnoreCase))))throw new ArgumentException("Wybrane nagranie nie istnieje w źródle.");
            if(selected.Count!=snapshot.Entries.Length)snapshot=snapshot with {FullEntries=snapshot.Entries,Entries=snapshot.Entries.Where(e=>selected.Contains(e.RelativePath)).ToArray()};
        }
        if(tab.Kind==TabKind.Audio)
        {
            if(files is not null&&snapshot.Entries.Any(e=>!Paths.IsAudio(e.RelativePath)))throw new AudioSelectionException("W zakładce Audio można wybierać wyłącznie pliki audio: WAV, BWF, RF64, MP3, FLAC, AAC, AIF, AIFF lub M4A.");
            var audioEntries=snapshot.Entries.Where(e=>Paths.IsAudio(e.RelativePath)).ToArray();
            if(audioEntries.Length==0)throw new AudioSelectionException("W wybranym folderze nie ma obsługiwanych plików audio.");
            if(audioEntries.Length!=snapshot.Entries.Length)snapshot=snapshot with {FullEntries=snapshot.FullEntries??snapshot.Entries,Entries=audioEntries};
        }
        var batch=new Batch(Guid.NewGuid().ToString("N"),config.Id,tab.Id,actor.UserId,tab.UserId,config,snapshot,clock.UtcNow,DefaultDayId:activeDay.Id,AudioDeviceId:audioDeviceId);
        store.Put("batch",batch.Id,batch);Audit(batch,"ingest-queued",source);return batch;
    }
    private void ValidateStores(ProjectConfig config)
    {
        var main=config.MainRoots.Select(volumes.Inspect).ToArray();var backup=config.BackupRoots.Select(volumes.Inspect).ToArray();
        if(main.Concat(backup).Any(v=>!v.StableIdentity))throw new SafetyException("Nie udało się potwierdzić tożsamości magazynu. Sprawdź podłączone dyski.");
        if(main.Select(v=>v.PhysicalDiskId).Intersect(backup.Select(v=>v.PhysicalDiskId)).Any())throw new SafetyException("Folder główny i backup są na tym samym fizycznym dysku. Wskaż osobny dysk dla backupu; inna partycja nie wystarczy.");
    }
    public int RecoverLegacyStoragePause()
    {
        const string legacy="Magazyny nie są niezależne od źródła i siebie nawzajem.";
        if(store.Get<PauseState>("system","pause") is not {Paused:true,Reason:legacy})return 0;
        var batches=store.All<Batch>("batch");var failed=batches.Where(b=>b.Stage==BatchStage.Failed&&b.Error==legacy).ToArray();
        if(failed.Length==0||batches.Any(b=>b.ClearStarted&&!b.ClearDone)||failed.Any(b=>b.Config.AutoClear||b.ClearStarted))return 0;
        if(batches.Any(b=>b.Stage==BatchStage.Failed&&b.Error!=legacy))return 0;
        try{foreach(var batch in failed){ValidateStores(batch.Config);ValidateSnapshot(batch.Source);}}
        catch(Exception ex)when(ex is IOException or SafetyException or System.ComponentModel.Win32Exception or ArgumentException){return 0;}
        foreach(var batch in failed)
        {
            store.Put("batch",batch.Id,batch with {Stage=BatchStage.Queued,Error=null});
            var key="critical:"+batch.Id;
            if(store.Get<Alert>("alert",key) is {} alert)store.Put("alert",key,alert with {Resolved=true,ResolvedAt=clock.UtcNow,ResolvedBy="system",Note="Naprawiona reguła magazynów. Źródło sprawdzone ponownie; wznowiono kopiowanie bez czyszczenia."});
            Audit(batch,"legacy-storage-recovered","Usunięto przestarzałą blokadę po kontroli dysków i manifestu źródła.");
        }
        Resume();return failed.Length;
    }
    public async Task Run(string id,CancellationToken ct)
    {
        var batch=store.Get<Batch>("batch",id)??throw new ArgumentException("Nieznane zgrywanie.");
        if(batch.Stage==BatchStage.Completed) return;
        try
        {
            Check(ct);
            if(batch.ClearStarted && !batch.ClearDone) throw new SafetyException("Przerwane usuwanie wymaga sprawdzenia źródła przez administratora.");
            if(!batch.ClearDone)
            {
                ValidateSnapshot(batch.Source);
                Stage(ref batch,BatchStage.Copying);
                var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in batch.Source.Entries)
                {
                    Check(ct);
                    var path=Paths.Under(batch.Source.Root,entry.RelativePath);
                    var hash=await FileSafety.HashAsync(path,ct);hashes[entry.RelativePath]=hash;
                    var assetId=batch.Id+":"+entry.RelativePath;
                    if(store.Get<Asset>("asset",assetId) is {} existing)
                    {
                        if(existing.Hash!=hash) throw new SafetyException("Źródło zmieniło się po przyjęciu zgrywania.");
                        await Verify(existing,ct);continue;
                    }
                    await CopyFile(batch,entry,hash,ct);
                }
                ValidateSnapshot(batch.Source);
                batch=batch with {Source=batch.Source with {Entries=batch.Source.Entries.Select(e=>e with {Hash=hashes[e.RelativePath]}).ToArray()}};
                Stage(ref batch,BatchStage.Secured);
                await PersistReports(batch,ct);
                faults.Hit("after-reports");
                UpdateNumbering(batch.ProjectId);
                Check(ct);
                var current=store.Get<ProjectConfig>("project",batch.ProjectId)!;
                if(batch.Source.FullEntries is not null&&batch.Config.AutoClear)Warn(batch.ProjectId,"partial-source:"+batch.Id,"clear-blocked","Zgrano wybrane pliki. Czyszczenie całej karty wymaga zabezpieczenia kompletnej zawartości.");
                if(batch.Config.AutoClear && current.AutoClear && batch.Source.FullEntries is null)
                {
                    var batchAssets=BatchAssets(batch.Id);
                    var qualified=batchAssets.Any(a=>a.IsMedia)&&batchAssets.Where(a=>a.IsMedia).All(a=>a.DeviceId is {} d && current.Devices.Any(x=>x.Id==d&&x.ClearQualified));
                    var independentSource=batchAssets.All(a=>a.Main.PhysicalDiskId!=batch.Source.PhysicalDiskId&&a.Backup.PhysicalDiskId!=batch.Source.PhysicalDiskId);
                    if(!independentSource)Warn(batch.ProjectId,"clear-shared-source:"+batch.Id,"clear-blocked","Kopie zweryfikowano. Czyszczenie zablokowane: źródło jest na tym samym dysku co jedna z kopii.");
                    else if(qualified && OperatingSystem.IsWindows())
                    {
                        await new CardEraser(volumes,archive,faults).Erase(batch,batchAssets,()=> !Paused && store.Get<ProjectConfig>("project",batch.ProjectId)?.AutoClear==true,
                            b=>{batch=b;store.Put("batch",b.Id,b);},ct);
                        Audit(batch,"source-cleared",batch.Source.Root);
                    }
                    else Warn(batch.ProjectId,"clear-unqualified:"+batch.Id,"clear-blocked","Czyszczenie zablokowane: profil formatu lub źródło nie ma kwalifikacji.");
                }
            }
            await ScheduleAdobe(batch,ct);
        }
        catch(OperationCanceledException) {Stage(ref batch,BatchStage.Paused);throw;}
        catch(Exception ex)
        {
            batch=store.Get<Batch>("batch",id)??batch;
            batch=batch with {Stage=BatchStage.Failed,Error=ex.Message};store.Put("batch",id,batch);
            if(ex is AudioSelectionException){Warn(batch.ProjectId,"invalid-audio:"+id,"invalid-audio",ex.Message);Audit(batch,"warning",ex.Message);}
            else {Pause(ex.Message);Warn(batch.ProjectId,"critical:"+id,"critical",ex.Message);Audit(batch,"critical",ex.Message);}
        }
    }
    private void Stage(ref Batch b,BatchStage stage) {b=b with {Stage=stage,Error=null};store.Put("batch",b.Id,b);Audit(b,"stage",stage.ToString());faults.Hit("stage:"+stage);}
    public Asset[] BatchAssets(string id)=>store.All<Asset>("asset").Where(a=>a.BatchId==id).ToArray();
    public static Asset[] Unique(Asset[] assets)=>assets.GroupBy(a=>(a.ProjectId,a.OwnerId,a.DeviceId,a.DayId,a.Hash,a.LogicalName)).Select(g=>g.First()).ToArray();
    private async Task CopyFile(Batch batch,SourceEntry entry,string hash,CancellationToken ct)
    {
        var config=batch.Config;var id=batch.Id+":"+entry.RelativePath;
        var file=Paths.Under(batch.Source.Root,entry.RelativePath);
        var tab=config.Tabs.Single(t=>t.Id==batch.TabId);
        var deviceId=batch.AudioDeviceId??Configuration.ResolveDevice(config,tab,entry.RelativePath);
        MediaInfo? media=null;
        if(Paths.IsMedia(file))
        {
            try {media=await probe.ProbeAsync(file,ct);}
            catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception or JsonException)
            {Warn(batch.ProjectId,"metadata:"+id,"metadata","Nie można odczytać metadanych; kopie będą zabezpieczone: "+ex.Message);}
        }
        if(tab.Kind==TabKind.Audio&&(media is null||media.HasVideo||media.AudioChannels<1))throw new AudioSelectionException("Odrzucono plik „"+entry.RelativePath+"”: nie potwierdzono pliku wyłącznie audio. Wideo i pliki o zmienionym rozszerzeniu nie są zgrywane w zakładce Audio.");
        var device=config.Devices.SingleOrDefault(d=>d.Id==deviceId);
        var days=store.All<ShootingDay>("day").Where(d=>d.ProjectId==config.Id).ToArray();
        var captured=media?.CapturedAt?.AddTicks(device?.ClockOffsetTicks??0);
        var matchedDay=Days.Resolve(days,captured);
        var day=matchedDay??days.SingleOrDefault(d=>d.Id==batch.DefaultDayId)??days.SingleOrDefault(d=>!d.Closed);
        if(day is not null&&matchedDay is null&&Paths.IsMedia(file))Warn(config.Id,"day-fallback:"+id,"day-fallback",$"Data nagrania {entry.RelativePath} nie pasuje do dni produkcji. Zapisano w dniu {day.Number}, wybranym przy rozpoczęciu zgrywania.");
        var late=day?.Closed==true;
        var duplicate=store.All<Asset>("asset").FirstOrDefault(a=>a.ProjectId==config.Id&&a.OwnerId==batch.OwnerId&&a.DeviceId==deviceId&&a.DayId==day?.Id&&a.Hash==hash&&Path.GetFileName(a.OriginalPath)==Path.GetFileName(entry.RelativePath));
        if(duplicate is not null)
        {
            await Verify(duplicate,ct);
            store.Put("asset",id,duplicate with {Id=id,BatchId=batch.Id,OriginalPath=entry.RelativePath,Late=late});
            Audit(batch,"duplicate-verified",entry.RelativePath);return;
        }
        if(late) Warn(config.Id,"late:"+id,"late",$"Spóźniony materiał: {entry.RelativePath}, Dzień {day!.Number}. Aktualizacja Premiere wymaga zgody.");
        if(Paths.IsMedia(file) && (device is null || day is null)) Warn(config.Id,"assignment:"+id,"assignment","Materiał wymaga przypisania przez admina: "+entry.RelativePath);
        StorageAllocator.Reservation? main=null,backup=null;
        await planning.WaitAsync(ct);
        FilePlan plan;
        try
        {
            var existing=store.Get<FilePlan>("file-plan",id);
            if(existing is not null)
            {
                if(existing.Hash!=hash) throw new SafetyException("Źródło zmieniło się względem planu kopiowania.");
                plan=existing;
            }
            else
            {
                var forbidden=new HashSet<string>();
                // Require all configured backup volumes to be disjoint from all main volumes, not just this file's targets.
                var mainDisks=config.MainRoots.Select(r=>volumes.Inspect(r).PhysicalDiskId).ToHashSet();
                ValidateStores(config);
                main=allocator.Reserve(config.Id+":"+config.Revision+":main",config.MainRoots,checked(entry.Length+4096),forbidden);
                forbidden.UnionWith(mainDisks);
                backup=allocator.Reserve(config.Id+":"+config.Revision+":backup",config.BackupRoots,checked(entry.Length+Math.Max(1024*1024,entry.Length/50)),forbidden);
                var original=Path.GetFileName(entry.RelativePath);Paths.SafeName(original);
                var name=original;var index=1;string relative;
                do
                {
                    name=Paths.CollisionName(original,index++);
                    relative=device is not null && day is not null ? Paths.Relative(day.Number,device.Name,name)
                        : Path.Combine("_DIT",Paths.IsMedia(file)?"Do przypisania":"Metadane źródłowe",batch.Id,entry.RelativePath);
                    if(index>2 && !Paths.IsMedia(file)) throw new SafetyException("Kolizja danych technicznych.");
                }
                while(config.MainRoots.Any(r=>File.Exists(Paths.Under(r,relative))) || config.BackupRoots.Any(r=>File.Exists(Paths.Under(r,relative)+(config.CompressBackup?".7z":""))) || store.All<FilePlan>("file-plan").Any(p=>p.MainPath.EndsWith(Path.DirectorySeparatorChar+relative,StringComparison.OrdinalIgnoreCase)));
                if(index>2) Warn(config.Id,"collision:"+id,"collision",$"Kolizja nazwy: {original} → {name}");
                plan=new(id,entry.RelativePath,name,hash,Paths.Under(main.Root,relative),main.Volume,Paths.Under(backup.Root,relative)+(config.CompressBackup?".7z":""),backup.Volume,deviceId,day?.Id,media,late);
                store.Put("file-plan",id,plan);
            }
        }
        finally {planning.Release();}
        try
        {
            Check(ct);ValidateVolume(plan.MainPath,plan.MainVolume);ValidateVolume(plan.BackupPath,plan.BackupVolume);
            if(File.Exists(plan.MainPath)) {if(await FileSafety.HashAsync(plan.MainPath,ct)!=hash) throw new SafetyException("Istniejąca kopia główna niezgodna z manifestem.");}
            else if(await FileSafety.CopyVerifiedAsync(file,plan.MainPath,new CheckedFaults(faults,()=>Check(ct)),ct,bytes=>Progress(batch,entry.RelativePath,bytes,false))!=hash) throw new SafetyException("Źródło zmieniło się w trakcie odczytu.");
            faults.Hit("after-main");Check(ct);
            if(config.CompressBackup)
            {
                if(!File.Exists(plan.BackupPath)) await archive.Create(plan.MainPath,plan.BackupPath,ct);
                await archive.Verify(plan.BackupPath,plan.LogicalName,hash,ct);
            }
            else if(File.Exists(plan.BackupPath)) {if(await FileSafety.HashAsync(plan.BackupPath,ct)!=hash) throw new SafetyException("Nieprawidłowa kopia backupowa.");}
            else if(await FileSafety.CopyVerifiedAsync(plan.MainPath,plan.BackupPath,new CheckedFaults(faults,()=>Check(ct)),ct,bytes=>Progress(batch,entry.RelativePath,bytes,true))!=hash) throw new SafetyException("Niezgodny backup.");
            faults.Hit("after-backup");
            var planDay=days.SingleOrDefault(d=>d.Id==plan.DayId);
            var position=planDay is not null&&device is not null&&Days.Resolve([planDay],captured) is not null ? Timecodes.Position(plan.Media,planDay,config,device):(null,"unresolved");
            var asset=new Asset(id,batch.Id,config.Id,batch.OwnerId,plan.DeviceId,plan.DayId,entry.RelativePath,plan.LogicalName,hash,entry.Length,Paths.IsMedia(file),plan.Media,
                new(plan.MainPath,plan.MainVolume.Id,plan.MainVolume.PhysicalDiskId,hash,entry.Length,true),
                new(plan.BackupPath,plan.BackupVolume.Id,plan.BackupVolume.PhysicalDiskId,hash,new FileInfo(plan.BackupPath).Length,true,config.CompressBackup),PositionTicks:position.Item1,SyncMethod:position.Item2,Late:plan.Late);
            store.Put("asset",id,asset);Audit(batch,"file-verified",entry.RelativePath+" SHA256="+hash);
            Progress(batch,null,0,false,true);
        }
        finally {main?.Dispose();backup?.Dispose();}
    }
    private void ValidateVolume(string path,VolumeInfo expected)
    {
        FileSafety.NoLinks(path);var now=volumes.Inspect(path);
        if(now.Id!=expected.Id||now.PhysicalDiskId!=expected.PhysicalDiskId||!now.StableIdentity) throw new SafetyException("Podmieniono wolumin docelowy.");
    }
    private void ValidateSnapshot(CardSnapshot source)
    {
        var now=FileSafety.Snapshot(source.Root,volumes);
        if(now.VolumeId!=source.VolumeId||now.PhysicalDiskId!=source.PhysicalDiskId || !now.Entries.SequenceEqual((source.FullEntries??source.Entries).Select(e=>e with {Hash=null}))) throw new SafetyException("Podmieniono źródło lub zmieniła się jego zawartość.");
    }
    public async Task Verify(Asset asset,CancellationToken ct)
    {
        foreach(var replica in new[]{asset.Main,asset.Backup})
        {
            var now=volumes.Inspect(replica.Path);
            if(now.Id!=replica.VolumeId||now.PhysicalDiskId!=replica.PhysicalDiskId) throw new SafetyException("Podmieniono dysk kopii.");
            if(replica.Archive) await archive.Verify(replica.Path,asset.LogicalName,asset.Hash,ct);
            else if(await FileSafety.HashAsync(replica.Path,ct)!=asset.Hash) throw new SafetyException("Kopia niezgodna z SHA-256.");
        }
    }
    public async Task PersistReports(Batch batch,CancellationToken ct)
    {
        foreach(var root in batch.Config.MainRoots.Concat(batch.Config.BackupRoots))
        {
            Check(ct);
            FileSafety.DurableJson(Paths.Under(root,Path.Combine("_DIT","Manifesty",batch.Id+".json")),new { Batch=batch, Assets=BatchAssets(batch.Id) });
            FileSafety.DurableJson(Paths.Under(root,Path.Combine("_DIT","Raporty",batch.Id+".json")),store.All<AuditEvent>("audit").Where(a=>a.ProjectId==batch.ProjectId).ToArray());
            store.Backup(Paths.Under(root,Path.Combine("_DIT","baza-"+batch.Id+".sqlite")));
            await Task.CompletedTask;
        }
    }
    public async Task ScheduleAdobe(Batch batch,CancellationToken ct)
    {
        Check(ct);var assets=BatchAssets(batch.Id).Where(a=>a.IsMedia).ToArray();
        if(assets.Any(a=>a.DayId is null||a.DeviceId is null)) {Stage(ref batch,BatchStage.WaitingAssignment);return;}
        if(assets.Any(a=>a.Media?.DurationTicks is not >0)){Warn(batch.ProjectId,"duration:"+batch.Id,"metadata","Kopie zabezpieczone. Nie można przygotować montażu bez czasu trwania nagrania.");Stage(ref batch,BatchStage.WaitingMetadata);return;}
        if(assets.Any(a=>a.Late) && store.Get<bool>("approval",batch.Id)!=true) {Stage(ref batch,BatchStage.WaitingApproval);return;}
        foreach(var dayId in assets.Select(a=>a.DayId!).Distinct())
        {
            var day=store.Get<ShootingDay>("day",dayId)!;
            var config=store.Get<ProjectConfig>("project",batch.ProjectId)!;
            var all=Unique(store.All<Asset>("asset").Where(a=>a.ProjectId==batch.ProjectId&&a.DayId==dayId&&a.IsMedia&&(!a.Late||store.Get<bool>("approval",a.BatchId))).ToArray());
            var placements=Timeline.Build(config,all);
            var commandId=batch.Id+":"+dayId;
            if(store.Get<AdobeCommand>("adobe",commandId)?.State=="complete") continue;
            store.Put("adobe",commandId,new AdobeCommand(commandId,batch.ProjectId,dayId,PremiereProjects.PathFor(config),PremiereProjects.SequenceFor(day),config,all,placements,"analyzing"));
        }
        Stage(ref batch,assets.Length==0?BatchStage.Completed:BatchStage.WaitingAdobe);
        await Task.CompletedTask;
    }
    public void UpdateNumbering(string projectId)
    {
        var config=store.Get<ProjectConfig>("project",projectId)!;
        var all=store.All<Asset>("asset").Where(a=>a.ProjectId==projectId&&a.IsMedia).ToArray();
        foreach(var device in config.Devices)
        {
            var key="gaps:"+projectId+":"+device.Id;
            var matching=all.Where(a=>a.DeviceId==device.Id).ToArray();
            var epochs=device.NumberingResets??[];
            var gaps=matching.GroupBy(a=>epochs.Count(t=>a.Media?.CapturedAt>=t))
                .SelectMany(g=>Numbering.Gaps(g.Select(a=>Path.GetFileName(a.OriginalPath)),device.NumberPattern).Select(n=>n with {Series=$"{n.Series} [seria {g.Key+1}] "})).ToArray();
            var regex=new System.Text.RegularExpressions.Regex(device.NumberPattern,System.Text.RegularExpressions.RegexOptions.None,TimeSpan.FromMilliseconds(100));
            if(matching.Any(a=>!regex.IsMatch(Path.GetFileNameWithoutExtension(a.OriginalPath)))) Warn(projectId,"number-profile:"+device.Id,"number-profile","Nie rozpoznano numeracji części nagrań: "+device.Name);
            if(gaps.Length>0) Warn(projectId,key,"number-gap","Brakujące numery nagrań — do wyjaśnienia: "+device.Name+" "+string.Join(", ",gaps.Select(g=>$"{g.Series}{g.First}–{g.Last}")));
            else if(store.Get<Alert>("alert",key) is {} alert && !alert.Resolved) {store.Put("alert",key,alert with {Resolved=true,ResolvedAt=clock.UtcNow,ResolvedBy="system",Note="Brakujące numery zostały dostarczone."});WriteAudit(projectId,"system","number-gap-resolved",device.Name);}
        }
    }
    public void Warn(string projectId,string key,string kind,string message)
    {
        var previous=store.Get<Alert>("alert",key);
        var config=store.Get<ProjectConfig>("project",projectId)!;
        var alert=ActivityContext.Enrich(new Alert(key,projectId,kind,message,previous?.CreatedAt??clock.UtcNow,Note:previous?.Note),config,store.All<Batch>("batch").Where(b=>b.ProjectId==projectId).ToArray());
        store.Put("alert",key,alert);
        if(previous is null||previous.Resolved||previous.Message!=message)
        {
            var item=new AuditEvent(Guid.NewGuid().ToString("N"),clock.UtcNow,projectId,"system",kind=="critical"?"critical":"warning",message,kind=="critical"?"error":"warning",alert.TabId,alert.BatchId,alert.SourceRoot,alert.CardVolumeId);store.Put("audit",item.Id,item);
        }
    }
    public void Audit(Batch b,string action,string detail)=>WriteAudit(b.ProjectId,b.ActorId,action,$"{b.Id}: {detail}");
    public void WriteAudit(string projectId,string actor,string action,string detail)
    {var item=new AuditEvent(Guid.NewGuid().ToString("N"),clock.UtcNow,projectId,actor,action,detail);var config=store.Get<ProjectConfig>("project",projectId);if(config is not null)item=ActivityContext.Enrich(item,config,store.All<Batch>("batch").Where(b=>b.ProjectId==projectId).ToArray());store.Put("audit",item.Id,item);}
    private void Progress(Batch batch,string? file,long bytes,bool backup,bool force=false)
    {
        var old=store.Get<TransferProgress>("progress",batch.Id);
        if(!force&&old is not null&&clock.UtcNow-old.UpdatedAt<TimeSpan.FromMilliseconds(500))return;
        var done=BatchAssets(batch.Id).Sum(a=>a.Bytes);var length=batch.Source.Entries.FirstOrDefault(e=>e.RelativePath==file)?.Length??0;
        store.Put("progress",batch.Id,new TransferProgress(batch.Id,file,batch.Source.Entries.Sum(e=>e.Length),done+(backup?length:bytes),done+(backup?bytes:0),batch.CreatedAt,clock.UtcNow));
    }
    private sealed class CheckedFaults(IFaultInjector inner,Action check):IFaultInjector {public void Hit(string point){check();inner.Hit(point);}}
}
