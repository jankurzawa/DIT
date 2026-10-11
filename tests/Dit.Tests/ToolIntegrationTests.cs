using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class ToolIntegrationTests
{
    [Fact]
    public async Task ManualCorrectionSurvivesAutomaticLtcAndAudioAnalysis()
    {
        using var f=new Fixture();f.Config=f.Config with {Devices=f.Config.Devices.Select(d=>d with {LtcChannel=0}).ToArray()};f.Store.Put("project",f.Config.Id,f.Config);f.File("C0001.MOV");
        var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        f.Store.Put("asset",asset.Id,asset with {PositionTicks=123456789,SyncMethod="manual-admin"});
        // Deliberately absent executable: protected manual positions must not launch analysis.
        await new AudioSynchronizer(f.Store,engine,"dit-test-missing-ffmpeg").Run(Assert.Single(f.Store.All<AdobeCommand>("adobe")),default);
        var updated=f.Store.Get<Asset>("asset",asset.Id)!;Assert.Equal(123456789,updated.PositionTicks);Assert.Equal("manual-admin",updated.SyncMethod);
    }
    private static string Repo()
    {
        var p=new DirectoryInfo(AppContext.BaseDirectory);while(p is not null&&!System.IO.File.Exists(Path.Combine(p.FullName,"DIT.sln")))p=p.Parent;
        return p?.FullName??throw new IOException("Nie znaleziono repozytorium testów.");
    }
    private static string Ffmpeg=>Directory.GetFiles(Path.Combine(Repo(),".tools","ffmpeg"),"ffmpeg.exe",SearchOption.AllDirectories).Single();
    private static string Probe=>Path.Combine(Path.GetDirectoryName(Ffmpeg)!,"ffprobe.exe");
    private static SevenZip Archive=>new(Path.Combine(Repo(),".tools","7zr.exe"));
    [Fact][Trait("Category","Tools")]
    public async Task RealRenamedVideoIsRejectedByAudioIngest()
    {
        using var f=new Fixture();var path=Path.Combine(f.Source,"clip.mov");
        await Processes.Run(Ffmpeg,["-v","error","-f","lavfi","-i","color=c=blue:s=160x90:r=25","-t","0.1","-c:v","mpeg4",path],default);File.Move(path,Path.Combine(f.Source,"fake.wav"));
        var engine=new IngestEngine(f.Store,f.Volumes,new Ffprobe(Probe),f.Clock,Archive,new NoFaults());var batch=engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec",["fake.wav"]);await engine.Run(batch.Id,default);
        Assert.Equal(BatchStage.Failed,f.Store.Get<Batch>("batch",batch.Id)!.Stage);Assert.Empty(engine.BatchAssets(batch.Id));Assert.False(engine.Paused);Assert.Empty(Directory.GetFiles(f.Main,"*",SearchOption.AllDirectories));
    }
    [Fact][Trait("Category","Tools")]
    public async Task CompressedLegacyLayoutIsVerifiedAndRestoresFromUpdatedOfflineManifest()
    {
        using var f=new Fixture();f.Config=f.Config with {CompressBackup=true};f.Store.Put("project",f.Config.Id,f.Config);f.Probe.Info=f.Probe.Info! with {CapturedAt=null};f.File("DCIM/C0001.MOV");var engine=f.Engine(archive:Archive);var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        var relative=Path.Combine("_DIT","Do przypisania",batch.Id,"C0001.MOV");var main=Paths.Under(f.Main,relative);var backup=Paths.Under(f.Backup,relative)+".7z";Directory.CreateDirectory(Path.GetDirectoryName(main)!);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Move(asset.Main.Path,main);File.Move(asset.Backup.Path,backup);
        f.Store.Put("asset",asset.Id,asset with {DayId=null,Main=asset.Main with {Path=main},Backup=asset.Backup with {Path=backup}});f.Store.Put("batch",batch.Id,batch with {Stage=BatchStage.WaitingAssignment});
        Assert.Equal(1,await new RecoveryService(f.Store,engine,f.Volumes,Archive).OrganizeLegacy(default));asset=Assert.Single(engine.BatchAssets(batch.Id));await engine.Verify(asset,default);Assert.EndsWith(Path.Combine("Dzień 1","Urządzenie 1","C0001.MOV.7z"),asset.Backup.Path);Assert.False(Directory.Exists(Path.Combine(f.Backup,"_DIT","Do przypisania")));
        var restore=Path.Combine(f.Root,"offline-layout");await OfflineRestore.Restore(Path.Combine(f.Backup,"_DIT","Manifesty",batch.Id+".json"),restore,[],Archive,default);Assert.Equal(asset.Hash,await FileSafety.HashAsync(Paths.Under(restore,"DCIM/C0001.MOV")));Assert.True(File.Exists(Paths.Under(f.Source,"DCIM/C0001.MOV")));
    }
    [Fact][Trait("Category","Tools")]
    public async Task RealArchiveRoundTripAndContentVerification()
    {
        using var f=new Fixture();f.File("C0001.MOV");var path=Path.Combine(f.Source,"C0001.MOV");var hash=await FileSafety.HashAsync(path);var archive=Path.Combine(f.Backup,"C0001.MOV.7z");await Archive.Create(path,archive,default);await Archive.Verify(archive,"C0001.MOV",hash,default);
        var restored=Path.Combine(f.Root,"restored","DCIM","C0001.MOV");await Archive.Restore(archive,"C0001.MOV",restored,hash,default);Assert.Equal(hash,await FileSafety.HashAsync(restored));
        await Assert.ThrowsAsync<SafetyException>(()=>Archive.Verify(archive,"C0001.MOV",new string('0',64),default));
    }
    [Fact][Trait("Category","Tools")]
    public async Task UnexpectedArchiveEntryRejected()
    {
        using var f=new Fixture();f.File("C0001.MOV");f.File("extra.txt",[1]);var archive=Path.Combine(f.Backup,"data.7z");await Archive.Create(Path.Combine(f.Source,"C0001.MOV"),archive,default);
        await Processes.Run(Path.Combine(Repo(),".tools","7zr.exe"),["a","--",archive,Path.Combine(f.Source,"extra.txt")],default);
        await Assert.ThrowsAsync<SafetyException>(()=>Archive.Verify(archive,"C0001.MOV",awaitHash(),default));
        string awaitHash()=>FileSafety.HashAsync(Path.Combine(f.Source,"C0001.MOV")).GetAwaiter().GetResult();
    }
    [Theory][InlineData(25)][InlineData(50)][Trait("Category","Tools")]
    public async Task ActualMovTimecodeDurationAndCaptureAreRead(int fps)
    {
        using var f=new Fixture();var path=Path.Combine(f.Source,"C0001.MOV");
        await Processes.Run(Ffmpeg,["-v","error","-f","lavfi","-i",$"color=c=blue:s=320x180:r={fps}","-f","lavfi","-i","sine=frequency=1000:sample_rate=48000","-t","1","-c:v","prores_ks","-profile:v","0","-pix_fmt","yuv422p10le","-c:a","pcm_s16le","-timecode","10:00:00:00","-metadata","creation_time=2026-10-10T10:00:00Z",path],default);
        var info=await new Ffprobe(Probe).ProbeAsync(path,default);Assert.NotNull(info);Assert.Equal(fps,info.FpsNumerator);Assert.Equal(TimeSpan.TicksPerSecond,info.DurationTicks);Assert.Equal("10:00:00:00",info.Timecode);Assert.True(info.HasVideo);Assert.Equal(1,info.AudioChannels);Assert.Equal(f.Clock.UtcNow,info.CapturedAt);
        Assert.Equal(320,info.Width);Assert.Equal(180,info.Height);Assert.Equal("prores",info.VideoCodec);Assert.Equal("Proxy",info.VideoProfile);Assert.True(ProxyPolicy.Matches(info,info));
    }
    [Fact][Trait("Category","Tools")]
    public async Task ActualMultichannelFloatAudioKeepsChannelsAndSingleDuration()
    {
        using var f=new Fixture();var path=Path.Combine(f.Source,"REC001.wav");await Processes.Run(Ffmpeg,["-v","error","-f","lavfi","-i","anullsrc=r=48000:cl=quad","-t","1","-c:a","pcm_f32le",path],default);
        var info=await new Ffprobe(Probe).ProbeAsync(path,default);Assert.NotNull(info);Assert.Equal(4,info.AudioChannels);Assert.False(info.HasVideo);Assert.Equal(TimeSpan.TicksPerSecond,info.DurationTicks);
    }
    [Fact][Trait("Category","Tools")]
    public async Task CompressedPipelineRestoresBothMediaAndCompanion()
    {
        using var f=new Fixture();f.Config=f.Config with {CompressBackup=true};f.Store.Put("project",f.Config.Id,f.Config);f.File("C0001.MOV");f.File("META/C0001.xml",[1,2]);var engine=f.Engine(archive:Archive);var batch=f.Queue(engine);await engine.Run(batch.Id,default);
        Assert.False(engine.Paused);Assert.All(engine.BatchAssets(batch.Id),a=>Assert.True(a.Backup.Archive));await new RecoveryService(f.Store,engine,f.Volumes,Archive).Restore(batch.Id,Path.Combine(f.Root,"restore"),default);
        foreach(var asset in engine.BatchAssets(batch.Id))Assert.Equal(asset.Hash,await FileSafety.HashAsync(Paths.Under(Path.Combine(f.Root,"restore"),asset.OriginalPath)));
    }
    [Fact][Trait("Category","Tools")]
    public async Task LightweightPreviewAndThumbnailAreBuiltFromSecuredCopy()
    {
        using var f=new Fixture();var source=Path.Combine(f.Source,"C0001.MOV");
        await Processes.Run(Ffmpeg,["-v","error","-f","lavfi","-i","color=c=red:s=640x360:r=50","-t","1","-c:v","prores_ks","-profile:v","0","-metadata","creation_time=2026-10-10T10:00:00Z",source],default);
        var engine=new IngestEngine(f.Store,f.Volumes,new Ffprobe(Probe),f.Clock,Archive,new NoFaults());var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        var previews=new PreviewService(f.Store,Ffmpeg);previews.Request(asset,f.Config,true,true);previews.Request(asset,f.Config,false,true);
        foreach(var job in f.Store.All<PreviewJob>("preview"))await previews.Build(job,default);
        Assert.All(f.Store.All<PreviewJob>("preview"),job=>{Assert.Equal("ready",job.State);Assert.True(System.IO.File.Exists(job.Path));});
        Assert.Equal(asset.Hash,await FileSafety.HashAsync(source));Assert.Equal(asset.Hash,await FileSafety.HashAsync(asset.Main.Path));
    }
    [Fact][Trait("Category","Tools")]
    public async Task RequestedThumbnailIsReadyImmediatelyAndConcurrentRequestsShareCache()
    {
        using var f=new Fixture();var source=Path.Combine(f.Source,"C0001.MOV");await Processes.Run(Ffmpeg,["-v","error","-f","lavfi","-i","testsrc2=s=640x360:r=25","-t","1","-c:v","libx264",source],default);
        var engine=new IngestEngine(f.Store,f.Volumes,new Ffprobe(Probe),f.Clock,Archive,new NoFaults());var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));var preview=new PreviewService(f.Store,Ffmpeg);
        await Task.WhenAll(preview.Thumbnail(asset,f.Config,default),preview.Thumbnail(asset,f.Config,default));var job=Assert.Single(f.Store.All<PreviewJob>("preview"));Assert.Equal("ready",job.State);Assert.True(File.Exists(job.Path));Assert.Equal(asset.Hash,await FileSafety.HashAsync(source));
    }
}
