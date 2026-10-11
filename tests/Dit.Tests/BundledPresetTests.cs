using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class BundledPresetTests
{
    [Theory]
    [InlineData(null,BundledPresets.Proxy720p)]
    [InlineData("",BundledPresets.Proxy720p)]
    [InlineData("custom.epr","custom.epr")]
    public void CreatingProductionDefaultsOnlyAnUnspecifiedPreset(string? selected,string expected)
    {
        using var f=new Fixture();
        var service=new ProjectService(f.Store,new Sessions(f.Clock),f.Engine());
        var config=f.Config with {Id="new-production",ProxyPreset=selected};
        var users=new[]{new NewUser("admin","Admin","",true),new NewUser("operator","Operator","",false),new NewUser("audio","Audio","",false)};
        var created=service.Create(new(config,users));
        Assert.Equal(expected,created.ProxyPreset);
        Assert.Equal(expected,f.Store.Get<ProjectConfig>("project",config.Id)!.ProxyPreset);
    }
    [Fact]
    public void AdobeReceivesARealPackagedFileWithoutFreezingInstallPathInProject()
    {
        using var f=new Fixture();var command=new AdobeCommand("command",f.Config.Id,f.Day.Id,"project.prproj","Dzień 1",f.Config,[],[],"pending");
        var current=f.Config with {ProxyPreset=BundledPresets.Proxy720p};
        var dispatched=AdobeSetup.RefreshPresets(command,current);
        Assert.Equal(BundledPresets.Proxy720p,current.ProxyPreset);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory,"Presets","DIT Proxy 720p.epr"),dispatched.Config.ProxyPreset);
        Assert.True(File.Exists(dispatched.Config.ProxyPreset));
        var xml=System.Xml.Linq.XDocument.Load(dispatched.Config.ProxyPreset!);
        Assert.Equal("DIT Proxy 720p",xml.Root!.Element("PresetName")!.Value);
        Assert.Equal("custom.epr",BundledPresets.ResolveProxy("custom.epr"));
    }
}
