using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class IntegrationTests
{
    [Fact] public async Task IngestCreatesFlatVerifiedMirrorsAndDurableManifests()
    {
        using var f=new Fixture();f.File("DCIM/C0001.MOV");f.File("DCIM/C0001.XML",[1,2,3]);var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        var assets=engine.BatchAssets(batch.Id);Assert.Equal(2,assets.Length);var media=assets.Single(a=>a.IsMedia);
        Assert.Equal(Path.Combine(f.Main,"Dzień 1","Urządzenie 1","C0001.MOV"),media.Main.Path);
        Assert.Equal(media.Hash,await FileSafety.HashAsync(media.Main.Path));Assert.Equal(media.Hash,await FileSafety.HashAsync(media.Backup.Path));Assert.Equal(2,Directory.GetFiles(f.Source,"*",SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(Path.Combine(f.Backup,"_DIT","Manifesty",batch.Id+".json")));Assert.Single(f.Store.All<AdobeCommand>("adobe"));Assert.False(engine.Paused);
        using var restoredDb=new SqliteStore(Path.Combine(f.Backup,"_DIT","baza-"+batch.Id+".sqlite"));Assert.Equal(2,restoredDb.All<Asset>("asset").Length);
    }
    [Theory][InlineData("stage:Copying")][InlineData("copy-chunk")][InlineData("before-readback")][InlineData("before-publish")][InlineData("after-main")][InlineData("after-backup")][InlineData("stage:Secured")][InlineData("after-reports")]
    public async Task EveryInjectedFailurePreservesSourceAndPausesGlobally(string point)
    {
        using var f=new Fixture();f.File("C0001.MOV");var original=await FileSafety.HashAsync(Path.Combine(f.Source,"C0001.MOV"));var engine=f.Engine(new OneFault(point));var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        Assert.True(engine.Paused);Assert.Equal(BatchStage.Failed,f.Store.Get<Batch>("batch",batch.Id)!.Stage);Assert.Equal(original,await FileSafety.HashAsync(Path.Combine(f.Source,"C0001.MOV")));Assert.False(f.Store.Get<Batch>("batch",batch.Id)!.ClearStarted);
    }
    [Fact] public async Task RecoveryAfterMainPublishIsIdempotent()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine(new OneFault("after-main"));var batch=f.Queue(engine);await engine.Run(batch.Id,default);engine=f.Engine();engine.Resume();await engine.Run(batch.Id,default);
        Assert.Single(engine.BatchAssets(batch.Id));Assert.Single(Directory.GetFiles(Path.Combine(f.Main,"Dzień 1","Urządzenie 1")));Assert.False(engine.Paused);
    }
    [Fact] public async Task ChangedSourceNeverResumesAsSameBatch()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);f.File("C0001.MOV",[7,8,9]);await engine.Run(batch.Id,default);Assert.True(engine.Paused);Assert.Empty(engine.BatchAssets(batch.Id));
    }
    [Fact] public async Task CorruptBackupIsDetectedOnRetry()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));System.IO.File.WriteAllBytes(asset.Backup.Path,[0]);await engine.Run(batch.Id,default);Assert.True(engine.Paused);Assert.True(System.IO.File.Exists(Path.Combine(f.Source,"C0001.MOV")));
    }
    [Fact] public async Task PhysicalIndependenceEnforcedBeforeCopy()
    {using var f=new Fixture();f.Volumes.Shared=true;f.File("C0001.MOV");var engine=f.Engine();Assert.Throws<SafetyException>(()=>f.Queue(engine));Assert.False(engine.Paused);Assert.Empty(f.Store.All<Batch>("batch"));await Task.CompletedTask;}
    [Fact] public async Task DifferentContentsSameNameAreKeptAndAlerted()
    {
        using var f=new Fixture();f.File("a/C0001.MOV",[1,2]);f.File("b/C0001.MOV",[3,4]);var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var assets=engine.BatchAssets(batch.Id);Assert.Equal(2,assets.Length);Assert.Contains(assets,a=>a.LogicalName=="C0001(2).MOV");Assert.Contains(f.Store.All<Alert>("alert"),a=>a.Kind=="collision");Assert.All(assets,a=>Assert.True(System.IO.File.Exists(a.Backup.Path)));
    }
    [Fact] public async Task ReimportReusesVerifiedReplicasAndDoesNotDoubleCount()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var first=f.Queue(engine);await engine.Run(first.Id,default);f.Store.Put("batch",first.Id,f.Store.Get<Batch>("batch",first.Id)! with {Stage=BatchStage.Completed});var second=f.Queue(engine);await engine.Run(second.Id,default);
        Assert.Single(IngestEngine.Unique(f.Store.All<Asset>("asset")));Assert.Equal(engine.BatchAssets(first.Id)[0].Main.Path,engine.BatchAssets(second.Id)[0].Main.Path);
    }
    [Fact] public async Task GapsAlertAndResolveAfterOutOfOrderArrival()
    {
        using var f=new Fixture();f.File("C0001.MOV",[1]);f.File("C0003.MOV",[3]);f.Probe.Info=null;var engine=f.Engine();var first=f.Queue(engine);await engine.Run(first.Id,default);Assert.Contains(f.Store.All<Alert>("alert"),a=>a.Kind=="number-gap"&&!a.Resolved);f.Store.Put("batch",first.Id,f.Store.Get<Batch>("batch",first.Id)! with {Stage=BatchStage.Completed});f.File("C0002.MOV",[2]);var second=f.Queue(engine);await engine.Run(second.Id,default);Assert.All(f.Store.All<Alert>("alert").Where(a=>a.Kind=="number-gap"),a=>Assert.True(a.Resolved));Assert.False(engine.Paused);
    }
    [Fact] public async Task UnqualifiedAutoClearDoesNotDeleteAndWarns()
    {using var f=new Fixture();f.Config=f.Config with {AutoClear=true};f.Store.Put("project",f.Config.Id,f.Config);f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);Assert.True(System.IO.File.Exists(Path.Combine(f.Source,"C0001.MOV")));Assert.Contains(f.Store.All<Alert>("alert"),a=>a.Kind=="clear-blocked");}
    [Fact] public async Task LateFootageIsCopiedToClosedDayAndWaitsForApproval()
    {using var f=new Fixture();f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow.AddHours(1)});f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);Assert.Equal(BatchStage.WaitingApproval,f.Store.Get<Batch>("batch",batch.Id)!.Stage);Assert.Empty(f.Store.All<AdobeCommand>("adobe"));Assert.Contains("Dzień 1",engine.BatchAssets(batch.Id)[0].Main.Path);f.Store.Put("approval",batch.Id,true);await engine.ScheduleAdobe(f.Store.Get<Batch>("batch",batch.Id)!,default);Assert.Single(f.Store.All<AdobeCommand>("adobe"));}
    [Fact] public async Task MissingCaptureTimeUsesDaySelectedAtIngestWithoutBlockingCopies()
    {using var f=new Fixture();f.Probe.Info=f.Probe.Info! with {CapturedAt=null};f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));Assert.Equal(f.Day.Id,asset.DayId);Assert.Contains("Dzień 1",asset.Main.Path);Assert.True(System.IO.File.Exists(asset.Main.Path));Assert.True(System.IO.File.Exists(asset.Backup.Path));Assert.Equal(BatchStage.WaitingAdobe,f.Store.Get<Batch>("batch",batch.Id)!.Stage);Assert.Null(asset.PositionTicks);Assert.False(engine.Paused);}
    [Fact] public async Task ApprovalOfOneLateBatchCannotImportAnotherUnapprovedBatch()
    {
        using var f=new Fixture();f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow.AddHours(1)});f.File("C0001.MOV");var engine=f.Engine();var first=f.Queue(engine);await engine.Run(first.Id,default);
        var firstAsset=Assert.Single(engine.BatchAssets(first.Id));var second=first with {Id="other-batch"};f.Store.Put("batch",second.Id,second);f.Store.Put("asset","other-asset",firstAsset with {Id="other-asset",BatchId=second.Id,Hash="other",LogicalName="C0002.MOV",PositionTicks=null});
        f.Store.Put("approval",first.Id,true);await engine.ScheduleAdobe(f.Store.Get<Batch>("batch",first.Id)!,default);var command=Assert.Single(f.Store.All<AdobeCommand>("adobe"));Assert.Equal(firstAsset.Id,Assert.Single(command.Assets).Id);
    }
    [Fact] public async Task RestoreReconstructsOriginalTreeAndHash()
    {
        using var f=new Fixture();f.File("DCIM/C0001.MOV");f.File("META/C0001.xml",[1,2]);var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var destination=Path.Combine(f.Root,"restore");await new RecoveryService(f.Store,engine,f.Volumes,new SevenZip()).Restore(batch.Id,destination,default);
        foreach(var asset in engine.BatchAssets(batch.Id))Assert.Equal(asset.Hash,await FileSafety.HashAsync(Paths.Under(destination,asset.OriginalPath)));
    }
    [Fact] public async Task UnknownClipsAreYellowTailAndKnownSameDeviceOverlapRejected()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));var unresolved=asset with {Id="unknown",PositionTicks=null};var placements=Timeline.Build(f.Config,[asset,unresolved]);Assert.True(placements[1].Unresolved);Assert.Equal(placements[0].StartTicks+placements[0].DurationTicks,placements[1].StartTicks);Assert.Throws<SafetyException>(()=>Timeline.Build(f.Config,[asset,asset with {Id="overlap"}]));
    }
}
