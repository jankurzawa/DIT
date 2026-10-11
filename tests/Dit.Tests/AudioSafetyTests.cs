using Dit.Core;
using Xunit;

namespace Dit.Tests;

public sealed class AudioSafetyTests
{
    [Fact] public void LegacyCountBecomesStableNamedDevicesWithoutMovingHistory()
    {
        using var f=new Fixture();var legacy=f.Config with {Devices=f.Config.Devices.Select(d=>d.Id=="rec"?d with {Name="Audio"}:d).ToArray(),AudioSources=[new("rec","obsolete",20,Enumerable.Range(1,20).Select(i=>"Kanał "+i).ToArray())]};
        var updated=LegacyAudioDevices.Upgrade(legacy);Configuration.Validate(updated);
        Assert.Equal(20,updated.AudioSources.Length);Assert.Equal(1,updated.Tabs.Count(t=>t.Kind==TabKind.Audio));Assert.Equal(legacy.Devices.Single(d=>d.Id=="rec"),updated.Devices.Single(d=>d.Id=="rec"));
        Assert.All(updated.AudioSources,s=>{Assert.Equal(1,s.Channels);Assert.Equal("",s.Pattern);Assert.StartsWith("Mikroport ",updated.Devices.Single(d=>d.Id==s.DeviceId).Name);});
        Assert.Same(updated,LegacyAudioDevices.Upgrade(updated));Assert.Equal(updated.AudioSources.Select(s=>(s.DeviceId,s.Pattern,s.Channels,string.Join(",",s.ChannelNames))),LegacyAudioDevices.Upgrade(legacy).AudioSources.Select(s=>(s.DeviceId,s.Pattern,s.Channels,string.Join(",",s.ChannelNames))));Assert.Equal(20,legacy.AudioSources.Single().Channels);
        Assert.Same(f.Config,LegacyAudioDevices.Upgrade(f.Config));
    }
    [Theory][InlineData("clip.mp4")][InlineData("clip.MOV")][InlineData("notes.txt")][InlineData("file.exe")]
    public void ExplicitNonAudioSelectionIsRejectedBeforeQueuing(string file)
    {
        using var f=new Fixture();f.File(file);var engine=f.Engine();
        Assert.Throws<Dit.Infrastructure.AudioSelectionException>(()=>engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec",[file]));Assert.Empty(f.Store.All<Batch>("batch"));Assert.False(engine.Paused);
    }
    [Fact] public async Task MixedFolderCopiesOnlyAudioAndNeverClearsUnselectedContents()
    {
        using var f=new Fixture();f.File("take.WAV");f.File("clip.mp4");f.File("notes.txt");f.Probe.Info=f.Probe.Info! with {HasVideo=false,AudioChannels=1};f.Config=f.Config with {AutoClear=true};f.Store.Put("project",f.Config.Id,f.Config);
        var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec");await engine.Run(batch.Id,default);
        Assert.Equal("take.WAV",Assert.Single(engine.BatchAssets(batch.Id)).LogicalName);Assert.Equal(3,batch.Source.FullEntries!.Length);Assert.False(f.Store.Get<Batch>("batch",batch.Id)!.ClearStarted);Assert.True(File.Exists(Path.Combine(f.Source,"clip.mp4")));Assert.False(engine.Paused);
    }
    [Fact] public async Task VideoDisguisedAsWavCannotProduceCopiesOrBlockOtherJobs()
    {
        using var f=new Fixture();f.File("fake.wav");var engine=f.Engine();var batch=engine.Enqueue(f.Config,new("x","audio","audio",false),f.Source,"rec",["fake.wav"]);await engine.Run(batch.Id,default);
        Assert.Equal(BatchStage.Failed,f.Store.Get<Batch>("batch",batch.Id)!.Stage);Assert.Empty(engine.BatchAssets(batch.Id));Assert.Empty(Directory.GetFiles(f.Main,"*",SearchOption.AllDirectories));Assert.Empty(Directory.GetFiles(f.Backup,"*",SearchOption.AllDirectories));Assert.False(engine.Paused);Assert.False(f.Store.Get<Batch>("batch",batch.Id)!.ClearStarted);
    }
    [Fact] public void VideoOnlyFolderIsRejectedEvenWithoutExplicitDeviceOverride()
    {
        using var f=new Fixture();f.File("clip.mp4");Assert.Throws<Dit.Infrastructure.AudioSelectionException>(()=>f.Queue(f.Engine(),"audio","audio"));Assert.Empty(f.Store.All<Batch>("batch"));
    }
}
