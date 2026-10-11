using Dit.Core;
using Xunit;

namespace Dit.Tests;

public sealed class ProxyTests
{
    private static MediaInfo Original=>new(TimeSpan.TicksPerSecond,null,null,50,1,4,true,Width:3840,Height:2160);
    private static MediaInfo Proxy=>Original with {Width=1280,Height=720,VideoCodec="prores",VideoProfile="Proxy"};
    [Fact] public void ValidProxyPreservesRateChannelsDurationAndAspect()=>Assert.True(ProxyPolicy.Matches(Original,Proxy));
    [Fact] public void RejectsMissingVideoOrMetadata()=>Assert.False(ProxyPolicy.Matches(Original,Proxy with {HasVideo=false}));
    [Fact] public void RejectsDownmixedAudio()=>Assert.False(ProxyPolicy.Matches(Original,Proxy with {AudioChannels=2}));
    [Fact] public void RejectsChangedRate()=>Assert.False(ProxyPolicy.Matches(Original,Proxy with {FpsNumerator=25}));
    [Fact] public void RejectsTrimmedDuration()=>Assert.False(ProxyPolicy.Matches(Original,Proxy with {DurationTicks=TimeSpan.TicksPerSecond*2}));
    [Fact] public void RejectsWrongCodecOrProfile(){Assert.False(ProxyPolicy.Matches(Original,Proxy with {VideoCodec="h264"}));Assert.False(ProxyPolicy.Matches(Original,Proxy with {VideoProfile="HQ"}));}
    [Fact] public void RejectsOversizeOrDistortedPicture(){Assert.False(ProxyPolicy.Matches(Original,Proxy with {Width=1920}));Assert.False(ProxyPolicy.Matches(Original,Proxy with {Width=1000}));}
    [Fact] public async Task CompletionRequiresExactlyAllVideoProxyIds()
    {
        using var f=new Fixture();f.File("C0001.MOV");var e=f.Engine();var b=f.Queue(e);await e.Run(b.Id,default);var a=Assert.Single(e.BatchAssets(b.Id));
        var command=new AdobeCommand("cmd",f.Config.Id,f.Day.Id,"day.prproj","day",f.Config,[a],[]);
        Assert.Throws<SafetyException>(()=>ProxyPolicy.RequireComplete(command,null));
        Assert.Throws<SafetyException>(()=>ProxyPolicy.RequireComplete(command,new Dictionary<string,string>()));
        Assert.Throws<SafetyException>(()=>ProxyPolicy.RequireComplete(command,new Dictionary<string,string>{{a.Id,"proxy"},{"foreign","x"}}));
        ProxyPolicy.RequireComplete(command,new Dictionary<string,string>{{a.Id,"proxy"}});
    }
}
