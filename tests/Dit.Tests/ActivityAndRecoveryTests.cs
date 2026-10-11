using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class ActivityAndRecoveryTests
{
    private const string Legacy="Magazyny nie są niezależne od źródła i siebie nawzajem.";
    private static Batch LegacyFailure(Fixture f,IngestEngine engine)
    {
        f.File("C0001.MOV");var batch=f.Queue(engine) with {Stage=BatchStage.Failed,Error=Legacy};
        f.Store.Put("batch",batch.Id,batch);f.Store.Put("alert","critical:"+batch.Id,new Alert("critical:"+batch.Id,f.Config.Id,"critical",Legacy,f.Clock.UtcNow));engine.Pause(Legacy);return batch;
    }
    [Fact] public async Task LegacyPauseRecoversOnlyAfterValidationAndCopiesWithIndependentBackup()
    {
        using var f=new Fixture();f.Volumes.SourceOnMain=true;var engine=f.Engine();var batch=LegacyFailure(f,engine);
        Assert.Equal(1,engine.RecoverLegacyStoragePause());Assert.False(engine.Paused);Assert.Equal(0,engine.RecoverLegacyStoragePause());
        await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));Assert.True(asset.Main.Verified);Assert.True(asset.Backup.Verified);
        Assert.True(File.Exists(Path.Combine(f.Source,"C0001.MOV")));Assert.False(f.Store.Get<Batch>("batch",batch.Id)!.ClearStarted);
        Assert.True(f.Store.Get<Alert>("alert","critical:"+batch.Id)!.Resolved);
        Assert.Contains(f.Store.All<AuditEvent>("audit"),e=>e.Action=="legacy-storage-recovered");
    }
    [Theory][InlineData("manual")][InlineData("changed")][InlineData("missing")][InlineData("shared")][InlineData("autoclear")][InlineData("erasing")][InlineData("other-failure")]
    public void LegacyMigrationNeverBypassesUnrelatedPauseOrSafetyChecks(string reason)
    {
        using var f=new Fixture();var engine=f.Engine();var batch=LegacyFailure(f,engine);
        switch(reason)
        {
            case "manual":engine.Pause("Administrator");break;
            case "changed":f.File("new.MOV");break;
            case "missing":File.Delete(Path.Combine(f.Source,"C0001.MOV"));break;
            case "shared":f.Volumes.Shared=true;break;
            case "autoclear":f.Store.Put("batch",batch.Id,batch with {Config=batch.Config with {AutoClear=true}});break;
            case "erasing":f.Store.Put("batch",batch.Id,batch with {ClearStarted=true});break;
            case "other-failure":f.Store.Put("batch","other",batch with {Id="other",Error="Uszkodzona kopia"});break;
        }
        Assert.Equal(0,engine.RecoverLegacyStoragePause());Assert.True(engine.Paused);Assert.Equal(BatchStage.Failed,f.Store.Get<Batch>("batch",batch.Id)!.Stage);
        Assert.False(f.Store.Get<Alert>("alert","critical:"+batch.Id)!.Resolved);
    }
    [Fact] public async Task SecuredBatchWaitingForAdobeDoesNotLockSourceAndDuplicateDoesNotInflateStats()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var first=f.Queue(engine);await engine.Run(first.Id,default);
        Assert.Equal(BatchStage.WaitingAdobe,f.Store.Get<Batch>("batch",first.Id)!.Stage);
        var next=f.Queue(engine);await engine.Run(next.Id,default);Assert.Single(IngestEngine.Unique(f.Store.All<Asset>("asset")));
    }
    [Theory][InlineData(BatchStage.Queued)][InlineData(BatchStage.Copying)][InlineData(BatchStage.BackingUp)][InlineData(BatchStage.Secured)][InlineData(BatchStage.Erasing)][InlineData(BatchStage.Paused)]
    public void ActiveCopyStillLocksSameSource(BatchStage stage)
    {using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);f.Store.Put("batch",batch.Id,batch with {Stage=stage});Assert.Throws<SafetyException>(()=>f.Queue(engine));}
    [Fact] public void ExistingAlertsAndHistoryGainCardAndTabContext()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=LegacyFailure(f,engine);
        var old=f.Store.Get<Alert>("alert","critical:"+batch.Id)!;var alert=ActivityContext.Enrich(old,f.Config,[batch]);
        Assert.Equal(old.CreatedAt,alert.CreatedAt);Assert.Equal(batch.Id,alert.BatchId);Assert.Equal("cam",alert.TabId);Assert.Equal(f.Source,alert.SourceRoot);Assert.Equal(batch.Source.VolumeId,alert.CardVolumeId);
        var audit=ActivityContext.Enrich(new AuditEvent("event",f.Clock.UtcNow,f.Config.Id,"operator","critical",batch.Id+": błąd"),f.Config,[batch]);
        Assert.Equal("error",audit.Level);Assert.Equal(f.Source,audit.SourceRoot);Assert.Equal("cam",audit.TabId);
        engine.Warn(f.Config.Id,"metadata:"+batch.Id,"metadata","Brak metadanych");var warning=Assert.Single(f.Store.All<AuditEvent>("audit"),e=>e.Action=="warning");Assert.Equal("warning",warning.Level);Assert.Equal(f.Source,warning.SourceRoot);
    }
    [Fact] public void ResolveAndReopenRequireAdminAndReasonAndNeverResumeQueue()
    {
        using var f=new Fixture();var engine=f.Engine();var batch=LegacyFailure(f,engine);var service=new ProjectService(f.Store,new Sessions(f.Clock),engine);var key="critical:"+batch.Id;
        var admin=new SessionView("token","admin","admin",true);var operatorSession=new SessionView("token","operator","cam",false);
        Assert.Throws<AccessException>(()=>service.ResolveAlert(f.Config.Id,key,new(true,"Sprawdzone"),operatorSession,f.Clock));
        Assert.Throws<AccessException>(()=>service.ResolveAlert("other",key,new(true,"Sprawdzone"),admin,f.Clock));
        Assert.Throws<ArgumentException>(()=>service.ResolveAlert(f.Config.Id,key,new(true," "),admin,f.Clock));
        var resolved=service.ResolveAlert(f.Config.Id,key,new(true,"  Wyjaśniono  "),admin,f.Clock);Assert.True(resolved.Resolved);Assert.Equal("Wyjaśniono",resolved.Note);Assert.Equal(f.Clock.UtcNow,resolved.ResolvedAt);Assert.Equal("admin",resolved.ResolvedBy);Assert.True(engine.Paused);
        f.Clock.Advance(TimeSpan.FromMinutes(1));var reopened=service.ResolveAlert(f.Config.Id,key,new(false,"Dodatkowe sprawdzenie"),admin,f.Clock);Assert.False(reopened.Resolved);Assert.Null(reopened.ResolvedAt);Assert.Null(reopened.ResolvedBy);
        Assert.Equal(2,f.Store.All<AuditEvent>("audit").Count(e=>e.Action is "alert-resolved" or "alert-reopened"));Assert.True(engine.Paused);
    }
    [Fact] public void DayStatusShowsLatestManuallyNumberedDayEvenWhenClosed()
    {
        using var f=new Fixture();Assert.Null(ActivityContext.DayStatus([],false).Number);
        var closed=f.Day with {Id="latest",Number=3,End=f.Clock.UtcNow,Closed=true};var status=ActivityContext.DayStatus([closed,f.Day],true);
        Assert.Equal(3,status.Number);Assert.False(status.Open);Assert.True(status.Paused);Assert.Equal(closed.End,status.End);
        Assert.True(ActivityContext.DayStatus([closed with {Closed=false,End=null}],false).Open);
    }
    [Fact] public void HistoryDateBoundsAreInclusiveAndSearchIncludesSourceAndDisplayNames()
    {
        var at=new DateTimeOffset(2026,10,11,23,59,0,TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026,10,11)));
        var item=new AuditEvent("event",at,"project","operator","file-verified","C0001.MOV",SourceRoot:@"E:\DCIM",CardVolumeId:"card-42");
        Assert.True(ActivityContext.Matches(item,new(2026,10,11),new(2026,10,11),"CARD-42"));
        Assert.True(ActivityContext.Matches(item,null,null,"kuczi","Kuczi"));Assert.False(ActivityContext.Matches(item,null,new(2026,10,10),null));
        Assert.False(ActivityContext.Matches(item,new(2026,10,12),null,null));Assert.False(ActivityContext.Matches(item,null,null,"nieistniejąca"));
    }
}
