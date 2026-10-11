using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class SafetyAndServicesTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task SourceMayShareMainDiskButIsNeverErased(bool autoClear)
    {
        using var f=new Fixture();f.Volumes.SourceOnMain=true;f.Config=f.Config with {AutoClear=autoClear,Devices=f.Config.Devices.Select(d=>d with {ClearQualified=true}).ToArray()};f.Store.Put("project",f.Config.Id,f.Config);
        f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        var asset=Assert.Single(engine.BatchAssets(batch.Id));Assert.True(asset.Main.Verified);Assert.True(asset.Backup.Verified);Assert.NotEqual(asset.Main.PhysicalDiskId,asset.Backup.PhysicalDiskId);
        Assert.True(File.Exists(Path.Combine(f.Source,"C0001.MOV")));Assert.False(f.Store.Get<Batch>("batch",batch.Id)!.ClearStarted);Assert.False(engine.Paused);
        if(autoClear)Assert.Contains(f.Store.All<Alert>("alert"),a=>a.Kind=="clear-blocked");
    }
    [Fact] public async Task AudioWithoutPatternIsCopiedAndBackedUpUnderItsSource()
    {
        using var f=new Fixture();f.Config=f.Config with {AudioSources=[f.Config.AudioSources[0] with {Pattern=""}]};f.Store.Put("project",f.Config.Id,f.Config);
        f.File("arbitrary.wav");f.Probe.Info=f.Probe.Info! with {HasVideo=false};var engine=f.Engine();var q=f.Queue(engine,"audio","audio");await engine.Run(q.Id,default);
        var asset=Assert.Single(engine.BatchAssets(q.Id));Assert.Equal("rec",asset.DeviceId);Assert.True(asset.Main.Verified);Assert.True(asset.Backup.Verified);
        Assert.Equal("arbitrary.wav",asset.LogicalName);Assert.Contains("Rejestrator 1",asset.Main.Path);
    }
    [Theory][InlineData("main-unverified")][InlineData("backup-unverified")][InlineData("shared")][InlineData("source-shared")][InlineData("hash")][InlineData("missing")][InlineData("disabled")][InlineData("paused")][InlineData("nonretroactive")]
    public async Task ErasureGateRefusesEveryBrokenInvariant(string broken)
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var queued=f.Queue(engine);await engine.Run(queued.Id,default);
        var batch=f.Store.Get<Batch>("batch",queued.Id)! with {Config=f.Config with {AutoClear=true}};var a=Assert.Single(engine.BatchAssets(batch.Id));Asset[] assets=[a];var enabled=true;var paused=false;
        switch(broken)
        {
            case "main-unverified":assets=[a with {Main=a.Main with {Verified=false}}];break;
            case "backup-unverified":assets=[a with {Backup=a.Backup with {Verified=false}}];break;
            case "shared":assets=[a with {Backup=a.Backup with {PhysicalDiskId=a.Main.PhysicalDiskId}}];break;
            case "source-shared":assets=[a with {Main=a.Main with {PhysicalDiskId=batch.Source.PhysicalDiskId}}];break;
            case "hash":assets=[a with {Hash=new string('0',64)}];break;
            case "missing":assets=[];break;
            case "disabled":enabled=false;break;
            case "paused":paused=true;break;
            case "nonretroactive":batch=batch with {Config=batch.Config with {AutoClear=false}};break;
        }
        Assert.Throws<SafetyException>(()=>ErasurePolicy.Require(batch,assets,enabled,paused));
    }
    [Fact] public async Task WarningDoesNotPreventErasurePrerequisites()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var q=f.Queue(engine);await engine.Run(q.Id,default);var batch=f.Store.Get<Batch>("batch",q.Id)! with {Config=f.Config with {AutoClear=true}};
        engine.Warn(f.Config.Id,"gap","number-gap","Brakujące numery nagrań — do wyjaśnienia");ErasurePolicy.Require(batch,engine.BatchAssets(batch.Id),true,false);
    }
    [Fact] public async Task OfflineRestoreWorksWithoutDatabase()
    {
        using var f=new Fixture();f.File("DCIM/C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var destination=Path.Combine(f.Root,"offline");
        await OfflineRestore.Restore(Path.Combine(f.Backup,"_DIT","Manifesty",batch.Id+".json"),destination,[],new SevenZip(),default);Assert.Equal(await FileSafety.HashAsync(Path.Combine(f.Source,"DCIM","C0001.MOV")),await FileSafety.HashAsync(Path.Combine(destination,"DCIM","C0001.MOV")));
    }
    [Fact] public async Task OwnerIsolationAndPasswordResetDoNotCancelJob()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        var users=new[]{new UserAccount("admin","Admin",Passwords.Hash("administrator-pass"),true),new UserAccount("operator","Operator",Passwords.Hash("operator-password"),false),new UserAccount("audio","Audio",Passwords.Hash("audio-password"),false)};f.Store.Put("users",f.Config.Id,users);
        var sessions=new Sessions(f.Clock);var service=new ProjectService(f.Store,sessions,engine);var actor=service.Login(new(f.Config.Id,"cam","operator","operator-password"));
        Assert.Single(service.Visible(f.Config.Id,actor));Assert.Empty(service.Visible(f.Config.Id,new("fake","audio","audio",false)));
        var admin=service.Login(new(f.Config.Id,"admin","admin","administrator-pass"));service.Password(f.Config.Id,admin,new("operator",null,"changed-password"));Assert.Throws<AccessException>(()=>service.Require(actor.Token,f.Config.Id));Assert.Equal(BatchStage.WaitingAdobe,f.Store.Get<Batch>("batch",batch.Id)!.Stage);
    }
    [Fact] public void ConfigCannotOverlapStorageRoots()
    {using var f=new Fixture();Assert.Throws<ArgumentException>(()=>Configuration.Validate(f.Config with {BackupRoots=[Path.Combine(f.Main,"backup")]}));}
}
