using Dit.Core;

namespace Dit.Infrastructure;

public sealed record AssignmentPlan(string DayId,string DeviceId,string Relative,string Name);
public sealed class RecoveryService(IStore store,IngestEngine engine,IVolumeInspector volumes,SevenZip archive)
{
    public bool HasPendingOrganization=>store.All<Batch>("batch").Any(CanOrganize);
    private bool CanOrganize(Batch batch)=>batch.Stage==BatchStage.WaitingAssignment&&
        ((store.Get<string[]>("layout-cleanup",batch.Id)?.Length??0)>0||engine.BatchAssets(batch.Id).Any(a=>a.DayId is null&&(a.DeviceId is not null||batch.Config.Tabs.Single(t=>t.Id==batch.TabId).DeviceId is not null)));
    public async Task Restore(string batchId,string destination,CancellationToken ct)
    {
        var batch=store.Get<Batch>("batch",batchId)??throw new ArgumentException("Nieznane zgrywanie.");
        destination=Path.GetFullPath(destination);FileSafety.NoLinks(destination);
        if(Configuration.IsWithin(destination,batch.Source.Root)||batch.Config.MainRoots.Concat(batch.Config.BackupRoots).Any(r=>Configuration.IsWithin(destination,r)||Configuration.IsWithin(r,destination))) throw new SafetyException("Odtwarzaj do osobnego katalogu poza kartą i magazynami.");
        var assets=engine.BatchAssets(batchId);
        foreach(var asset in assets)
        {
            var target=Paths.Under(destination,asset.OriginalPath);
            if(File.Exists(target)){if(await FileSafety.HashAsync(target,ct)!=asset.Hash)throw new SafetyException("Docelowy plik już istnieje z inną zawartością.");continue;}
            var now=volumes.Inspect(asset.Backup.Path);if(now.Id!=asset.Backup.VolumeId)throw new SafetyException("Podmieniono backup.");
            if(asset.Backup.Archive)await archive.Restore(asset.Backup.Path,asset.LogicalName,target,asset.Hash,ct);
            else if(await FileSafety.CopyVerifiedAsync(asset.Backup.Path,target,new NoFaults(),ct)!=asset.Hash)throw new SafetyException("Odtworzenie niezgodne z manifestem.");
        }
    }
    public async Task<Asset> Assign(string assetId,string dayId,string deviceId,string actor,CancellationToken ct,bool schedule=true)
    {
        var asset=store.Get<Asset>("asset",assetId)??throw new ArgumentException("Nieznane nagranie.");
        var config=store.Get<ProjectConfig>("project",asset.ProjectId)!;var day=store.Get<ShootingDay>("day",dayId)??throw new ArgumentException("Nieznany dzień.");
        if(day.ProjectId!=config.Id)throw new SafetyException("Dzień z innego projektu.");
        var device=config.Devices.Single(d=>d.Id==deviceId);
        if(asset.DayId is not null&&asset.DeviceId is not null)throw new SafetyException("Ta operacja służy do rozstrzygnięcia nieprzypisanych nagrań.");
        await engine.Verify(asset,ct);
        var same=store.All<Asset>("asset").FirstOrDefault(a=>a.Id!=asset.Id&&a.ProjectId==asset.ProjectId&&a.OwnerId==asset.OwnerId&&a.DayId==dayId&&a.DeviceId==deviceId&&a.Hash==asset.Hash&&Path.GetFileName(a.OriginalPath)==Path.GetFileName(asset.OriginalPath));
        if(same is not null)
        {
            await engine.Verify(same,ct);var reused=asset with {DayId=dayId,DeviceId=deviceId,LogicalName=same.LogicalName,Main=same.Main,Backup=same.Backup,PositionTicks=same.PositionTicks,SyncMethod=same.SyncMethod,Late=day.Closed};store.Put("asset",asset.Id,reused);ResolveAssignment(asset.Id,actor);return reused;
        }
        var plan=store.Get<AssignmentPlan>("layout-plan",asset.Id);
        if(plan is not null&&(plan.DayId!=dayId||plan.DeviceId!=deviceId))throw new SafetyException("Dokończ rozpoczęte porządkowanie przed zmianą dnia lub urządzenia.");
        if(plan is null)
        {
            var index=1;string candidate;string candidateName;
            do{candidateName=Paths.CollisionName(Path.GetFileName(asset.OriginalPath),index++);candidate=Paths.Relative(day.Number,device.Name,candidateName);}while(config.MainRoots.Any(r=>File.Exists(Paths.Under(r,candidate)))||config.BackupRoots.Any(r=>File.Exists(Paths.Under(r,candidate)+(asset.Backup.Archive?".7z":""))));
            plan=new(dayId,deviceId,candidate,candidateName);store.Put("layout-plan",asset.Id,plan);
        }
        var relative=plan.Relative;var name=plan.Name;
        string RootFor(Replica replica,string[] roots)=>roots.Single(r=>Configuration.IsWithin(replica.Path,r));
        var main=Paths.Under(RootFor(asset.Main,config.MainRoots),relative);var backup=Paths.Under(RootFor(asset.Backup,config.BackupRoots),relative)+(asset.Backup.Archive?".7z":"");
        if(!File.Exists(main)&&await FileSafety.CopyVerifiedAsync(asset.Main.Path,main,new NoFaults(),ct)!=asset.Hash)throw new SafetyException("Niezgodna kopia po porządkowaniu.");
        if(await FileSafety.HashAsync(main,ct)!=asset.Hash)throw new SafetyException("Niezgodny plik docelowy porządkowania.");
        if(asset.Backup.Archive){if(!File.Exists(backup))await archive.Create(main,backup,ct);await archive.Verify(backup,name,asset.Hash,ct);}
        else{if(!File.Exists(backup)&&await FileSafety.CopyVerifiedAsync(asset.Backup.Path,backup,new NoFaults(),ct)!=asset.Hash)throw new SafetyException("Niezgodny backup po porządkowaniu.");if(await FileSafety.HashAsync(backup,ct)!=asset.Hash)throw new SafetyException("Niezgodny backup docelowy porządkowania.");}
        // Keep the secured staging copies so already-issued offline manifests remain valid.
        var position=Days.Resolve([day],asset.Media?.CapturedAt?.AddTicks(device.ClockOffsetTicks)) is not null?Timecodes.Position(asset.Media,day,config,device):(null,"unresolved");
        var updated=asset with {LogicalName=name,DayId=dayId,DeviceId=deviceId,Main=asset.Main with {Path=main},Backup=asset.Backup with {Path=backup,Bytes=new FileInfo(backup).Length},PositionTicks=position.Position,SyncMethod=position.Method,Late=day.Closed};
        store.Put("asset",assetId,updated);engine.WriteAudit(config.Id,actor,"asset-assigned",assetId+" → "+relative);
        ResolveAssignment(assetId,actor);
        foreach(var root in config.MainRoots.Concat(config.BackupRoots))FileSafety.DurableJson(Paths.Under(root,Path.Combine("_DIT","Manifesty",asset.BatchId+"-assignment.json")),engine.BatchAssets(asset.BatchId));
        engine.UpdateNumbering(config.Id);
        var batch=store.Get<Batch>("batch",asset.BatchId)!;if(schedule)await engine.ScheduleAdobe(batch,ct);return updated;
    }
    private void ResolveAssignment(string id,string actor)
    {if(store.Get<Alert>("alert","assignment:"+id) is {} alert)store.Put("alert",alert.Key,alert with {Resolved=true,ResolvedBy=actor,ResolvedAt=DateTimeOffset.UtcNow,Note="Przywrócono automatyczną organizację dnia i zakładki."});}
    public async Task<int> OrganizeLegacy(CancellationToken ct)
    {
        var count=0;
        foreach(var batch in store.All<Batch>("batch").Where(CanOrganize))
        {
            if(engine.Paused)break;
            var config=store.Get<ProjectConfig>("project",batch.ProjectId)!;var days=store.All<ShootingDay>("day").Where(d=>d.ProjectId==config.Id).ToArray();
            var fallback=days.FirstOrDefault(d=>d.Id==batch.DefaultDayId)??Days.Resolve(days,batch.CreatedAt)??days.OrderByDescending(d=>d.Number).FirstOrDefault();if(fallback is null)continue;
            var oldAssets=engine.BatchAssets(batch.Id);
            var cleanup=(store.Get<string[]>("layout-cleanup",batch.Id)??[]).Concat(oldAssets.SelectMany(a=>new[]{a.Main.Path,a.Backup.Path})).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();store.Put("layout-cleanup",batch.Id,cleanup);
            foreach(var asset in oldAssets.Where(a=>a.DayId is null))
            {
                var device=asset.DeviceId??config.Tabs.Single(t=>t.Id==batch.TabId).DeviceId;if(device is null)continue;
                var profile=config.Devices.Single(d=>d.Id==device);var matched=Days.Resolve(days,asset.Media?.CapturedAt?.AddTicks(profile.ClockOffsetTicks));var day=matched??fallback;
                await Assign(asset.Id,day.Id,device,"system",ct,false);count++;
                if(matched is null&&asset.IsMedia)engine.Warn(config.Id,"day-fallback:"+asset.Id,"day-fallback",$"Zapisano {asset.LogicalName} w dniu {day.Number}, zgodnie z zakładką zgrywania. Data pliku nie pasuje do dni produkcji.");
            }
            await engine.PersistReports(batch,ct);
            // Only obsolete application-owned staging replicas are removed, after verified replacements and durable reports.
            var referenced=store.All<Asset>("asset").SelectMany(a=>new[]{a.Main.Path,a.Backup.Path}).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var old in cleanup)
            {
                var staging=config.MainRoots.Concat(config.BackupRoots).Select(r=>Paths.Under(r,Path.Combine("_DIT","Do przypisania"))).FirstOrDefault(r=>Configuration.IsWithin(old,r));
                if(staging is null||referenced.Contains(old)||!File.Exists(old))continue;
                FileSafety.NoLinks(old);File.Delete(old);
                var dir=Path.GetDirectoryName(old);while(dir is not null&&Configuration.IsWithin(dir,staging)&&Directory.Exists(dir)&&!Directory.EnumerateFileSystemEntries(dir).Any()){Directory.Delete(dir);dir=Path.GetDirectoryName(dir);}
            }
            store.Put<string[]>("layout-cleanup",batch.Id,[]);
            engine.WriteAudit(config.Id,"system","layout-repaired",batch.Id+": przywrócono foldery Dzień / urządzenie");
            await engine.ScheduleAdobe(batch,ct);
        }
        return count;
    }
}
