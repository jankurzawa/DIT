using Dit.Core;
using Dit.Infrastructure;
using System.Xml.Linq;
using Xunit;

namespace Dit.Tests;

public sealed class AdobeRecoveryTests
{
    [Fact]
    public void ProbeUsesPictureDurationRatherThanLongerDataTrack()
    {
        var info=Ffprobe.Parse("""{"format":{"duration":"18.000000"},"streams":[{"codec_type":"video","duration":"17.250567","avg_frame_rate":"30000/1001"},{"codec_type":"data","duration":"18.000000"}]}""");
        Assert.Equal(172505670,info.DurationTicks);Assert.Equal(0,info.AudioChannels);
    }
    [Fact]
    public async Task DefaultPresetPreservesSilentMonoAndStereoSources()
    {
        using var f=new Fixture();f.File("C0001.MOV");var engine=f.Engine();var batch=f.Queue(engine);await engine.Run(batch.Id,default);var asset=Assert.Single(engine.BatchAssets(batch.Id));
        var command=new AdobeCommand("cmd",f.Config.Id,f.Day.Id,"day.prproj","day",f.Config with {ProxyPreset=BundledPresets.ResolveProxy(BundledPresets.Proxy720p)},[asset],[]);
        var presetIds=new HashSet<string>();
        foreach(var channels in new[]{0,1,2})
        {
            var preset=ProxyPresets.ForAsset(command,asset with {Media=asset.Media! with {AudioChannels=channels}});var xml=XDocument.Load(preset);
            Assert.Equal(channels==0?"false":"true",xml.Root!.Element("DoAudio")!.Value);
            Assert.Equal(Math.Max(1,channels).ToString(),xml.Descendants("ExporterParam").Single(e=>e.Element("ParamIdentifier")?.Value=="ADBEAudioNumChannels").Element("ParamValue")!.Value);
            var overlay=Assert.Single(xml.Descendants("StandardFilterWrapperOverlay"));
            Assert.True(presetIds.Add(xml.Root.Element("PresetID")!.Value));
            Assert.Equal("true",overlay.Element("Enabled")!.Value);
            Assert.Equal(ProxyPresets.AdobeWatermark(),overlay.Element("SourceFilePath")!.Value);
            Assert.True(File.Exists(overlay.Element("SourceFilePath")!.Value));
            Assert.EndsWith("_proxy_wm.mov",ProxyPolicy.PathFor(command,f.Day,asset));
            Assert.EndsWith("_proxy.mov",ProxyPolicy.PathFor(command with {Config=command.Config with {ProxyPreset="custom.epr"}},f.Day,asset));
            Assert.Equal(preset,ProxyPresets.ForAsset(command,asset with {Media=asset.Media! with {AudioChannels=channels}}));
        }
        Assert.Throws<SafetyException>(()=>ProxyPresets.ForAsset(command,asset with {Media=asset.Media! with {AudioChannels=4}}));
        Assert.Equal("custom.epr",ProxyPresets.ForAsset(command with {Config=command.Config with {ProxyPreset="custom.epr"}},asset));
    }
    [Fact]
    public void OnlyExpiredClaimsAreReplayedAndOldRunIdIsRevoked()
    {
        using var f=new Fixture();var command=new AdobeCommand("cmd",f.Config.Id,f.Day.Id,"day.prproj","day",f.Config,[],[],"inflight",RunId:"run");
        f.Store.Put("adobe",command.Id,command);f.Store.Put("adobe-lease",command.Id,new AdobeLease("run",f.Clock.UtcNow));
        AdobeLeases.RecoverExpired(f.Store,f.Clock.UtcNow.AddSeconds(44));Assert.Equal("inflight",f.Store.Get<AdobeCommand>("adobe",command.Id)!.State);
        AdobeLeases.RecoverExpired(f.Store,f.Clock.UtcNow.AddSeconds(46));var recovered=f.Store.Get<AdobeCommand>("adobe",command.Id)!;Assert.Equal("pending",recovered.State);Assert.Null(recovered.RunId);
    }
}
