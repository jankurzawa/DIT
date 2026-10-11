using System.Security.Cryptography;
using System.Xml.Linq;
using Dit.Core;

namespace Dit.Infrastructure;

public static class ProxyPresets
{
    public static string ForAsset(AdobeCommand command,Asset asset)
    {
        var selected=command.Config.ProxyPreset??throw new SafetyException("Brak presetu proxy.");
        var bundled=BundledPresets.ResolveProxy(BundledPresets.Proxy720p)!;
        if(!string.Equals(Path.GetFullPath(selected),bundled,StringComparison.OrdinalIgnoreCase))return selected;
        var channels=asset.Media?.AudioChannels??throw new SafetyException("Brak informacji o kanałach audio.");
        if(channels>2)throw new SafetyException("Wideo wielokanałowe wymaga własnego presetu AME zachowującego wszystkie kanały audio.");
        var xml=XDocument.Load(bundled);xml.Root!.Element("DoAudio")!.Value=channels==0?"false":"true";
        xml.Descendants("StandardFilterWrapperOverlay").Single().Element("SourceFilePath")!.Value=AdobeWatermark();
        xml.Descendants("ExporterParam").Single(e=>e.Element("ParamIdentifier")?.Value=="ADBEAudioNumChannels").Element("ParamValue")!.Value=Math.Max(1,channels).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var digest=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml.ToString()));var hash=Convert.ToHexString(digest)[..16];
        // AME caches settings by PresetID, even when the EPR path is different.
        xml.Root.Element("PresetID")!.Value=new Guid(digest[..16]).ToString();
        var content=xml.ToString();
        var path=Paths.Under(command.Config.ProxyRoot,Path.Combine("_DIT","Presety",$"DIT Proxy 720p-{channels}ch-{hash}.epr"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);FileSafety.NoLinks(path);
        if(!File.Exists(path)){var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,content);File.Move(temporary,path,true);}
        return path;
    }
    public static string AdobeWatermark()
    {
        // Use Adobe's own installed asset, without redistributing it or tying DIT to a release year.
        var adobe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Adobe");
        if(Directory.Exists(adobe))
            foreach(var product in new[]{"Adobe Media Encoder*","Adobe Premiere Pro*"})
                foreach(var folder in Directory.EnumerateDirectories(adobe,product).OrderDescending(StringComparer.OrdinalIgnoreCase))
                {
                    var path=Path.Combine(folder,"png","ProxyWatermark.png");
                    if(File.Exists(path))return path;
                }
        throw new SafetyException("Nie znaleziono znaku wodnego Adobe ProxyWatermark.png. Zainstaluj Media Encoder lub wybierz własny preset proxy.");
    }
}
