using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class ProductionFeaturesTests
{
    [Fact] public async Task LegacyPendingVideoQueuesSeparateDayCreationWithoutProxyPreset()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("cam","operator","cam",false),f.Source);await engine.Run(batch.Id,default);
        var pending=Assert.Single(f.Store.All<AdobeCommand>("adobe"));f.Store.Put("adobe",pending.Id,pending with {State="pending"});
        AdobeSetup.EnsureDayProjects(f.Store);AdobeSetup.EnsureDayProjects(f.Store);
        var creation=Assert.Single(f.Store.All<AdobeCommand>("adobe"),c=>c.Id.StartsWith("setup-day:"));
        Assert.Empty(creation.Assets);Assert.Empty(creation.Placements);Assert.Equal(pending.ProjectPath,creation.ProjectPath);Assert.Null(AdobeSetup.WaitingReason(creation));
        Assert.Equal("pending",f.Store.Get<AdobeCommand>("adobe",pending.Id)!.State);
    }
    [Fact] public async Task ExistingProductionFileStillQueuesMissingDaySequence()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("cam","operator","cam",false),f.Source);await engine.Run(batch.Id,default);
        var pending=Assert.Single(f.Store.All<AdobeCommand>("adobe"));f.Store.Put("adobe",pending.Id,pending with {State="pending"});
        Directory.CreateDirectory(Path.GetDirectoryName(pending.ProjectPath)!);File.WriteAllText(pending.ProjectPath,"existing project");
        AdobeSetup.EnsureDayProjects(f.Store);Assert.Equal(2,f.Store.All<AdobeCommand>("adobe").Length);Assert.Empty(f.Store.Get<AdobeCommand>("adobe","setup-day:"+pending.DayId)!.Assets);Assert.Equal("existing project",File.ReadAllText(pending.ProjectPath));
    }
    [Fact] public void MissingAdobePresetCanBeSuppliedWithoutChangingFrozenDestinations()
    {
        using var f=new Fixture();var service=Service(f);f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow});service.OpenDay(f.Config.Id,f.Clock.UtcNow,new("admin","admin","admin",true));
        var command=Assert.Single(f.Store.All<AdobeCommand>("adobe"));
        var refreshed=AdobeSetup.RefreshPresets(command,f.Config with {ProxyRoot="different",PremiereRoot="different",ProxyPreset="proxy.epr",SequencePreset="sequence.sqpreset"});
        Assert.Equal(command.Config.ProxyRoot,refreshed.Config.ProxyRoot);Assert.Equal(command.ProjectPath,refreshed.ProjectPath);Assert.Equal(command.Config.PremiereRoot,refreshed.Config.PremiereRoot);
        Assert.Equal("proxy.epr",refreshed.Config.ProxyPreset);Assert.Equal("sequence.sqpreset",refreshed.Config.SequencePreset);Assert.Null(AdobeSetup.WaitingReason(refreshed));
    }
    [Fact] public void SqliteTransactionRollsBackCredentialsAndConfigurationTogether()
    {
        using var f=new Fixture();
        Assert.Throws<IOException>(()=>f.Store.Atomic(()=>{f.Store.Put("users","atomic",new[]{"secret"});f.Store.Put("project",f.Config.Id,f.Config with {Name="Changed"});throw new IOException("power loss");}));
        Assert.Null(f.Store.Get<string[]>("users","atomic"));Assert.Equal("Film",f.Store.Get<ProjectConfig>("project",f.Config.Id)!.Name);
        f.Store.Atomic(()=>f.Store.Put("project",f.Config.Id,f.Config with {Name="Saved"}));Assert.Equal("Saved",f.Store.Get<ProjectConfig>("project",f.Config.Id)!.Name);
    }
    [Fact] public void CalendarUsesSelectedZoneAndRejectsDstGap()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        Assert.Equal(TimeSpan.FromHours(2),WallClock.Resolve(new(2026,10,11),14,15,16,zone).Offset);
        Assert.Throws<ArgumentException>(()=>WallClock.Resolve(new(2026,3,29),2,30,0,zone));
        Assert.Throws<ArgumentException>(()=>WallClock.Resolve(new(2026,10,25),2,30,0,zone));
        var first=WallClock.Resolve(new(2026,10,25),2,30,0,zone,TimeSpan.FromHours(2));
        var second=WallClock.Resolve(new(2026,10,25),2,30,0,zone,TimeSpan.FromHours(1));Assert.Equal(TimeSpan.FromHours(1),second-first);
        Assert.Throws<ArgumentException>(()=>WallClock.Resolve(new(2026,10,11),24,0,0,zone));
    }
    private static ProjectService Service(Fixture f)
    {
        f.Store.Put("users",f.Config.Id,new[]{new UserAccount("admin","Admin",Passwords.Hash(""),true),new UserAccount("operator","Operator",Passwords.Hash("old"),false),new UserAccount("audio","Audio",Passwords.Hash(""),false)});
        return new(f.Store,new Sessions(f.Clock),f.Engine());
    }
    [Fact] public void ConfigurationChangesPasswordsExplicitlyAndRevokesSessions()
    {
        using var f=new Fixture();var service=Service(f);var actor=service.Login(new(f.Config.Id,"admin","admin",""));var old=service.Login(new(f.Config.Id,"cam","operator","old"));
        service.Configure(new(f.Config,[],new(){["operator"]=""}),actor);
        Assert.Throws<AccessException>(()=>service.Require(old.Token,f.Config.Id));
        Assert.Equal(actor,service.Require(actor.Token,f.Config.Id,true));
        Assert.NotNull(service.Login(new(f.Config.Id,"cam","operator","")));
        Assert.Throws<AccessException>(()=>service.Login(new(f.Config.Id,"cam","operator","old")));
        Assert.True(Passwords.Verify("",service.Users(f.Config.Id).Single(u=>u.Id=="admin").PasswordHash));
    }
    [Fact] public void OrdinaryConfigurationPreservesAdminAndOperatorSessions()
    {
        using var f=new Fixture();var service=Service(f);var admin=service.Login(new(f.Config.Id,"admin","admin",""));var camera=service.Login(new(f.Config.Id,"cam","operator","old"));var audio=service.Login(new(f.Config.Id,"audio","audio",""));
        var updated=f.Config with {Name="Renamed",Tabs=f.Config.Tabs.Select(t=>t.Id=="cam"?t with {Name="Camera renamed"}:t).ToArray(),AudioSources=[..f.Config.AudioSources,new("mic2","",1,["Mikroport 2"])],Devices=[..f.Config.Devices,new("mic2","Mikroport 2",true)]};
        service.Configure(new(updated,[]),admin);
        Assert.Equal(admin,service.Require(admin.Token,f.Config.Id,true));Assert.Equal(camera,service.Require(camera.Token,f.Config.Id));Assert.Equal(audio,service.Require(audio.Token,f.Config.Id));
    }
    [Fact] public void RemovingTabRevokesItsSessionAndPreservesOthers()
    {
        using var f=new Fixture();var service=Service(f);var admin=service.Login(new(f.Config.Id,"admin","admin",""));var camera=service.Login(new(f.Config.Id,"cam","operator","old"));
        service.Configure(new(f.Config with {Tabs=f.Config.Tabs.Where(t=>t.Id!="cam").ToArray()},[]),admin);
        Assert.Throws<AccessException>(()=>service.Require(camera.Token,f.Config.Id));Assert.Equal(admin,service.Require(admin.Token,f.Config.Id,true));
    }
    [Fact] public void InvalidPasswordTargetDoesNotWriteUsersOrConfig()
    {
        using var f=new Fixture();var service=Service(f);var actor=service.Login(new(f.Config.Id,"admin","admin",""));var revision=f.Config.Revision;
        Assert.Throws<ArgumentException>(()=>service.Configure(new(f.Config,[],new(){["operator"]="new",["missing"]=""}),actor));
        Assert.Equal(revision,f.Store.Get<ProjectConfig>("project",f.Config.Id)!.Revision);Assert.NotNull(service.Login(new(f.Config.Id,"cam","operator","old")));
    }
    [Fact] public void OpeningDayQueuesSequenceInProductionPremiereProject()
    {
        using var f=new Fixture();var service=Service(f);f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow});
        var day=service.OpenDay(f.Config.Id,f.Clock.UtcNow,new("admin","admin","admin",true));var command=Assert.Single(f.Store.All<AdobeCommand>("adobe"));
        Assert.Equal(day.Id,command.DayId);Assert.Equal(Path.Combine(f.Config.PremiereRoot,"Film.prproj"),command.ProjectPath);Assert.Equal("Dzień 2",command.SequenceName);Assert.Empty(command.Assets);Assert.Equal("pending",command.State);
    }
    [Fact] public void ConsecutiveDaysShareOnePremiereFileAndHaveDistinctSequences()
    {
        using var f=new Fixture();var service=Service(f);var admin=new SessionView("admin","admin","admin",true);
        f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow});
        var second=service.OpenDay(f.Config.Id,f.Clock.UtcNow,admin);service.CloseDay(second.Id,f.Clock.UtcNow.AddHours(1),admin);
        service.OpenDay(f.Config.Id,f.Clock.UtcNow.AddHours(2),admin);
        var commands=f.Store.All<AdobeCommand>("adobe");Assert.Equal(2,commands.Length);Assert.Single(commands.Select(c=>c.ProjectPath).Distinct());
        Assert.Equal(new[]{"Dzień 2","Dzień 3"},commands.Select(c=>c.SequenceName).Order().ToArray());
    }
    [Fact] public void SameNamedProductionsAndExistingFilesNeverSharePremiereDestination()
    {
        using var f=new Fixture();var service=Service(f);Directory.CreateDirectory(f.Config.PremiereRoot);
        var occupied=Path.Combine(f.Config.PremiereRoot,"Film(2).prproj");File.WriteAllText(occupied,"montaż");
        NewUser[] users=[new("admin","Admin","",true),new("operator","Operator","",false),new("audio","Audio","",false)];
        var second=service.Create(new(f.Config with {Id="second"},users));var third=service.Create(new(f.Config with {Id="third"},users));
        Assert.Equal(Path.Combine(f.Config.PremiereRoot,"Film(3).prproj"),second.PremiereProjectPath);
        Assert.Equal(Path.Combine(f.Config.PremiereRoot,"Film(4).prproj"),third.PremiereProjectPath);Assert.Equal("montaż",File.ReadAllText(occupied));
    }
    [Fact] public void RenamingProductionKeepsItsReservedPremiereProject()
    {
        using var f=new Fixture();var service=Service(f);var admin=service.Login(new(f.Config.Id,"admin","admin",""));
        var updated=service.Configure(new(f.Config with {Name="Nowa nazwa",PremiereProjectPath="injected.prproj"},[]),admin);
        Assert.Equal(Path.Combine(f.Config.PremiereRoot,"Film.prproj"),updated.PremiereProjectPath);
        f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow});service.OpenDay(f.Config.Id,f.Clock.UtcNow,admin);
        Assert.Equal(updated.PremiereProjectPath,Assert.Single(f.Store.All<AdobeCommand>("adobe")).ProjectPath);
    }
    [Fact] public async Task IngestOnDifferentDaysTargetsSameProductionFile()
    {
        using var f=new Fixture();var engine=f.Engine();f.File("C0001.MOV");var first=f.Queue(engine);await engine.Run(first.Id,default);
        System.IO.File.Delete(Path.Combine(f.Source,"C0001.MOV"));
        f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow.AddMinutes(1)});f.Clock.Advance(TimeSpan.FromHours(3));
        var next=new ShootingDay("next",f.Config.Id,2,f.Clock.UtcNow.AddMinutes(-1));f.Store.Put("day",next.Id,next);
        f.Probe.Info=f.Probe.Info! with {CapturedAt=f.Clock.UtcNow};f.File("C0002.MOV",[4,5,6]);var second=f.Queue(engine);await engine.Run(second.Id,default);
        var commands=f.Store.All<AdobeCommand>("adobe");Assert.Contains(commands,c=>c.DayId==f.Day.Id);Assert.Contains(commands,c=>c.DayId==next.Id);
        Assert.Single(commands.Select(c=>c.ProjectPath).Distinct());Assert.Equal(Path.Combine(f.Config.PremiereRoot,"Film.prproj"),commands[0].ProjectPath);
    }
    [Fact] public async Task ExplicitMicroportDoesNotDependOnNamesAndRoutesFlatMirroredFolders()
    {
        using var f=new Fixture();f.File("generic001.wav");f.Probe.Info=f.Probe.Info! with {HasVideo=false,AudioChannels=1};
        f.Config=f.Config with {Devices=[..f.Config.Devices,new("rec2","Mikroport Jan",true)],AudioSources=[..f.Config.AudioSources,new("rec2","",1,["Jan"])]};f.Store.Put("project",f.Config.Id,f.Config);
        var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("audio","audio","audio",false),f.Source,"rec2");await engine.Run(batch.Id,default);
        var asset=Assert.Single(engine.BatchAssets(batch.Id));Assert.Equal("rec2",asset.DeviceId);Assert.Equal(Path.Combine(f.Main,"Dzień 1","Mikroport Jan","generic001.wav"),asset.Main.Path);Assert.Equal(Path.Combine(f.Backup,"Dzień 1","Mikroport Jan","generic001.wav"),asset.Backup.Path);
        var command=Assert.Single(f.Store.All<AdobeCommand>("adobe"));var placement=Assert.Single(command.Placements);Assert.True(placement.Audio);Assert.Equal("Mikroport Jan",placement.TrackName);Assert.Equal(1,placement.AudioChannels);
        Assert.False(engine.Paused);
    }
    [Fact] public void AudioOverrideCannotBeUsedFromCameraOrForUnknownSource()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();
        Assert.Throws<ArgumentException>(()=>engine.Enqueue(f.Config,new("x","operator","cam",false),f.Source,"rec"));
        Assert.Throws<ArgumentException>(()=>engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"missing"));
        Assert.Throws<AudioSelectionException>(()=>engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec"));Assert.Empty(f.Store.All<Batch>("batch"));
    }
    [Fact] public async Task SelectedAudioFilesAreCopiedWithoutTouchingOtherFilesOrClearingCard()
    {
        using var f=new Fixture();f.File("one.wav");f.File("two.wav",[4,5]);f.Probe.Info=f.Probe.Info! with {HasVideo=false,AudioChannels=1};f.Config=f.Config with {AutoClear=true};f.Store.Put("project",f.Config.Id,f.Config);
        var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec",["one.wav"]);await engine.Run(batch.Id,default);
        Assert.Single(engine.BatchAssets(batch.Id));Assert.NotNull(batch.Source.FullEntries);Assert.True(File.Exists(Path.Combine(f.Source,"one.wav")));Assert.True(File.Exists(Path.Combine(f.Source,"two.wav")));Assert.False(f.Store.Get<Batch>("batch",batch.Id)!.ClearStarted);Assert.Contains(f.Store.All<Alert>("alert"),a=>a.Key=="partial-source:"+batch.Id);
        var restored=Path.Combine(f.Root,"restored");await OfflineRestore.Restore(Path.Combine(f.Backup,"_DIT","Manifesty",batch.Id+".json"),restored,[],new SevenZip(),default);Assert.True(File.Exists(Path.Combine(restored,"one.wav")));Assert.False(File.Exists(Path.Combine(restored,"two.wav")));
    }
    [Fact] public async Task ChangeInUnselectedSourceFileStillBlocksPartialIngest()
    {
        using var f=new Fixture();f.File("one.wav");f.File("two.wav");var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec",["one.wav"]);
        File.WriteAllBytes(Path.Combine(f.Source,"two.wav"),[1]);await engine.Run(batch.Id,default);Assert.True(engine.Paused);Assert.Empty(engine.BatchAssets(batch.Id));Assert.True(File.Exists(Path.Combine(f.Source,"one.wav")));
    }
    [Fact] public async Task DayStatisticsDeduplicateAndCountAudioDurationOnce()
    {
        using var f=new Fixture();f.File("one.wav");f.Probe.Info=f.Probe.Info! with {HasVideo=false,AudioChannels=20};var engine=f.Engine();var batch=f.Queue(engine,"audio","audio");await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        f.Store.Put("asset","duplicate",asset with {Id="duplicate"});var service=Service(f);var stats=service.DayStats(f.Config.Id,f.Day.Id,f.Clock);
        Assert.Equal(1,stats.Clips);Assert.Equal(asset.Bytes,stats.Bytes);Assert.Equal(TimeSpan.FromSeconds(2).Ticks,stats.DurationTicks);Assert.Equal(TimeSpan.FromHours(1).Ticks,stats.ElapsedTicks);Assert.Equal(batch.Id,Assert.Single(stats.Cards).BatchId);
        f.Store.Put("day",f.Day.Id,f.Day with {Closed=true,End=f.Clock.UtcNow.AddHours(2)});f.Clock.Advance(TimeSpan.FromDays(1));Assert.Equal(TimeSpan.FromHours(3).Ticks,service.DayStats(f.Config.Id,f.Day.Id,f.Clock).ElapsedTicks);
        Assert.Throws<AccessException>(()=>service.DayStats("wrong",f.Day.Id,f.Clock));
    }
    [Fact] public async Task WorkflowRequiresSavedAdobeResultAndPreciseMatchingPosition()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var service=Service(f);var actor=new SessionView("x","operator","cam",false);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        var before=Assert.Single(service.Workflow(f.Config.Id,actor));Assert.True(before.Ingested&&before.BackedUp);Assert.False(before.InPremiere||before.Synchronized||before.Proxy);
        var command=Assert.Single(f.Store.All<AdobeCommand>("adobe"));f.Store.Put("adobe-timeline",command.Id,command);Assert.True(Assert.Single(service.Workflow(f.Config.Id,actor)).InPremiere);Assert.False(Assert.Single(service.Workflow(f.Config.Id,actor)).Synchronized);
        f.Store.Put("adobe",command.Id,command with {State="complete"});Assert.True(Assert.Single(service.Workflow(f.Config.Id,actor)).InPremiere);
        asset=asset with {SyncMethod="native-timecode",PositionTicks=0};f.Store.Put("asset",asset.Id,asset);f.Store.Put("adobe",command.Id,command with {State="complete",Placements=[command.Placements[0] with {Unresolved=false,StartTicks=0}]});Assert.True(Assert.Single(service.Workflow(f.Config.Id,actor)).Synchronized);
        f.Store.Put("asset",asset.Id,asset with {PositionTicks=100});Assert.False(Assert.Single(service.Workflow(f.Config.Id,actor)).Synchronized);
        Assert.Empty(service.Workflow(f.Config.Id,new("x","audio","audio",false)));
    }
    [Fact] public void AutoSequencePresetPreservesUhdAndBuildsNamedMonoMicroportTracks()
    {
        using var f=new Fixture();var c=f.Config with {AudioSources=[new("rec","",1,["Jan"])]};
        var source="""<PremiereData><SequencePreset><VideoFrameSize>0,0,3840,2160</VideoFrameSize><VideoFrameRate>5080320000</VideoFrameRate><InitialNumberOfVideoTracks>3</InitialNumberOfVideoTracks><AudioTracks>[{"mName":"","mChannelType":1}]</AudioTracks></SequencePreset></PremiereData>""";
        var xml=System.Xml.Linq.XDocument.Parse(AdobeSetup.BuildSequencePreset(source,c));var tracks=System.Text.Json.Nodes.JsonNode.Parse(xml.Descendants("AudioTracks").Single().Value)!.AsArray();Assert.Equal(2,tracks.Count);Assert.Equal("Rejestrator 1",tracks[1]!["mName"]!.GetValue<string>());Assert.Equal(0,tracks[1]!["mChannelType"]!.GetValue<int>());Assert.Equal("1",xml.Descendants("InitialNumberOfVideoTracks").Single().Value);
        Assert.Throws<SafetyException>(()=>AdobeSetup.BuildSequencePreset(source.Replace("3840,2160","1920,1080"),c));
        Assert.Throws<SafetyException>(()=>AdobeSetup.BuildSequencePreset(source,c with {AudioSources=[new("rec","",20,Enumerable.Range(1,20).Select(i=>i.ToString()).ToArray())]}));
    }
}
