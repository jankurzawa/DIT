using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class DayAndLayoutTests
{
    private sealed class ObservedStore(IStore inner,List<TransferProgress> changes):IStore
    {
        public void Put<T>(string kind,string id,T value){inner.Put(kind,id,value);if(value is TransferProgress progress)changes.Add(progress);}
        public T? Get<T>(string kind,string id)=>inner.Get<T>(kind,id);
        public T[] All<T>(string kind)=>inner.All<T>(kind);
        public void Backup(string destination)=>inner.Backup(destination);
    }
    private sealed class AdvancingClock(FakeClock clock):IFaultInjector
    {public void Hit(string point){if(point=="copy-chunk")clock.Advance(TimeSpan.FromSeconds(1));}}
    [Fact] public async Task TransferProgressNeverRegressesWhenBackupStartsAndCompletesBothCopies()
    {
        using var f=new Fixture();f.Probe.Info=f.Probe.Info! with {CapturedAt=null};f.File("C0001.MOV",new byte[2*1024*1024]);f.File("C0002.MOV",new byte[2*1024*1024]);List<TransferProgress> changes=[];
        var engine=new IngestEngine(new ObservedStore(f.Store,changes),f.Volumes,f.Probe,f.Clock,new SevenZip(),new AdvancingClock(f.Clock));var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        Assert.False(engine.Paused);Assert.True(changes.Count>=8);Assert.Contains(changes,p=>p.MainBytes>p.BackupBytes&&p.BackupBytes>0);
        var combined=changes.Select(p=>p.MainBytes+p.BackupBytes).ToArray();Assert.Equal(combined.Order().ToArray(),combined);
        var final=changes.Last();Assert.Equal(4*1024*1024,final.TotalBytes);Assert.Equal(final.TotalBytes,final.MainBytes);Assert.Equal(final.TotalBytes,final.BackupBytes);
    }
    [Fact] public async Task OldCameraDateUsesOpenDayAndFlatMirrorWithoutChangingOriginal()
    {
        using var f=new Fixture();f.Probe.Info=f.Probe.Info! with {CapturedAt=f.Clock.UtcNow.AddYears(-1)};f.File("DCIM/C0001.MOV");f.File("META/C0001.XML",[1]);
        var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        Assert.All(engine.BatchAssets(batch.Id),a=>{Assert.Equal(f.Day.Id,a.DayId);Assert.Equal("cam",a.DeviceId);Assert.Equal(Path.Combine(f.Main,"Dzień 1","Urządzenie 1",Path.GetFileName(a.OriginalPath)),a.Main.Path);Assert.True(File.Exists(Paths.Under(f.Source,a.OriginalPath)));});
        var media=Assert.Single(engine.BatchAssets(batch.Id),a=>a.IsMedia);Assert.Null(media.PositionTicks);Assert.Equal(BatchStage.WaitingAdobe,f.Store.Get<Batch>("batch",batch.Id)!.Stage);Assert.Contains(f.Store.All<Alert>("alert"),a=>a.Kind=="day-fallback");Assert.False(engine.Paused);
    }
    [Fact] public async Task DefaultDayRemainsFrozenWhenAdministratorChangesDayBeforeCopy()
    {
        using var f=new Fixture();f.Probe.Info=f.Probe.Info! with {CapturedAt=null};f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);
        f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow});f.Store.Put("day","next",new ShootingDay("next",f.Config.Id,2,f.Clock.UtcNow.AddSeconds(1)));
        await engine.Run(batch.Id,default);Assert.Equal(f.Day.Id,Assert.Single(engine.BatchAssets(batch.Id)).DayId);Assert.Equal(BatchStage.WaitingApproval,f.Store.Get<Batch>("batch",batch.Id)!.Stage);
    }
    [Fact] public async Task MatchingEarlierDayWinsOverCurrentDay()
    {
        using var f=new Fixture();f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow.AddMinutes(1)});f.Store.Put("day","next",new ShootingDay("next",f.Config.Id,2,f.Clock.UtcNow.AddDays(1)));f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        Assert.Equal(f.Day.Id,Assert.Single(engine.BatchAssets(batch.Id)).DayId);Assert.Equal(BatchStage.WaitingApproval,f.Store.Get<Batch>("batch",batch.Id)!.Stage);
    }
    private static async Task<Batch> LegacyStaging(Fixture f,IngestEngine engine)
    {
        f.Probe.Info=f.Probe.Info! with {CapturedAt=null};f.File("DCIM/C0001.MOV");var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        var relative=Path.Combine("_DIT","Do przypisania",batch.Id,"C0001.MOV");var main=Paths.Under(f.Main,relative);var backup=Paths.Under(f.Backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(main)!);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Move(asset.Main.Path,main);File.Move(asset.Backup.Path,backup);
        f.Store.Put("asset",asset.Id,asset with {DayId=null,Main=asset.Main with {Path=main},Backup=asset.Backup with {Path=backup}});batch=batch with {Stage=BatchStage.WaitingAssignment};f.Store.Put("batch",batch.Id,batch);return batch;
    }
    [Fact] public async Task LegacyLayoutIsRepairedVerifiedAndRestorableWithoutSourceDeletion()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=await LegacyStaging(f,engine);var service=new RecoveryService(f.Store,engine,f.Volumes,new SevenZip());Assert.Equal(1,await service.OrganizeLegacy(default));
        var asset=Assert.Single(engine.BatchAssets(batch.Id));Assert.Equal(Path.Combine(f.Main,"Dzień 1","Urządzenie 1","C0001.MOV"),asset.Main.Path);await engine.Verify(asset,default);Assert.False(Directory.Exists(Path.Combine(f.Main,"_DIT","Do przypisania")));Assert.True(File.Exists(Paths.Under(f.Source,"DCIM/C0001.MOV")));
        var restored=Path.Combine(f.Root,"offline-restored");await OfflineRestore.Restore(Path.Combine(f.Backup,"_DIT","Manifesty",batch.Id+".json"),restored,[],new SevenZip(),default);Assert.Equal(asset.Hash,await FileSafety.HashAsync(Paths.Under(restored,asset.OriginalPath)));Assert.Equal(0,await service.OrganizeLegacy(default));
    }
    [Fact] public async Task InterruptedLayoutCopyReusesItsPersistedDestination()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=await LegacyStaging(f,engine);var asset=Assert.Single(engine.BatchAssets(batch.Id));var relative=Paths.Relative(1,"Urządzenie 1","C0001.MOV");var target=Paths.Under(f.Main,relative);
        f.Store.Put("layout-plan",asset.Id,new AssignmentPlan(f.Day.Id,"cam",relative,"C0001.MOV"));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(asset.Main.Path,target);
        await new RecoveryService(f.Store,engine,f.Volumes,new SevenZip()).OrganizeLegacy(default);Assert.Equal(target,Assert.Single(engine.BatchAssets(batch.Id)).Main.Path);Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target)!));
    }
    [Fact] public async Task PausedLayoutResumesFromItsJournalWithoutAnotherProcessRestart()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=await LegacyStaging(f,engine);var service=new RecoveryService(f.Store,engine,f.Volumes,new SevenZip());engine.Pause("Inspekcja nośników");
        Assert.True(service.HasPendingOrganization);Assert.Equal(0,await service.OrganizeLegacy(default));engine.Resume();Assert.Equal(1,await service.OrganizeLegacy(default));Assert.False(service.HasPendingOrganization);await engine.Verify(Assert.Single(engine.BatchAssets(batch.Id)),default);
    }
    [Fact] public async Task LayoutRefusesCorruptDestinationAndKeepsBothStagingCopies()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=await LegacyStaging(f,engine);var asset=Assert.Single(engine.BatchAssets(batch.Id));var relative=Paths.Relative(1,"Urządzenie 1","C0001.MOV");var target=Paths.Under(f.Main,relative);
        f.Store.Put("layout-plan",asset.Id,new AssignmentPlan(f.Day.Id,"cam",relative,"C0001.MOV"));Directory.CreateDirectory(Path.GetDirectoryName(target)!);await File.WriteAllBytesAsync(target,[0]);
        await Assert.ThrowsAsync<SafetyException>(()=>new RecoveryService(f.Store,engine,f.Volumes,new SevenZip()).OrganizeLegacy(default));Assert.True(File.Exists(asset.Main.Path));Assert.True(File.Exists(asset.Backup.Path));Assert.Null(f.Store.Get<Asset>("asset",asset.Id)!.DayId);
    }
    [Fact] public async Task DuplicateLegacyBatchesShareVerifiedOrganizedReplicas()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=await LegacyStaging(f,engine);var asset=Assert.Single(engine.BatchAssets(batch.Id));var duplicate=batch with {Id="second"};f.Store.Put("batch",duplicate.Id,duplicate);f.Store.Put("asset","second-asset",asset with {Id="second-asset",BatchId="second"});
        Assert.Equal(2,await new RecoveryService(f.Store,engine,f.Volumes,new SevenZip()).OrganizeLegacy(default));var unique=Assert.Single(IngestEngine.Unique(f.Store.All<Asset>("asset")));await engine.Verify(unique,default);Assert.Single(Directory.GetFiles(Path.GetDirectoryName(unique.Main.Path)!));
    }
}
