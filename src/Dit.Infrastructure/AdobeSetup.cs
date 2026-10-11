using System.Text.Json.Nodes;
using System.Xml.Linq;
using Dit.Core;

namespace Dit.Infrastructure;

public static class AdobeSetup
{
    public static ProjectConfig AssignProjectPath(IStore store,ProjectConfig config)
    {
        if(config.PremiereProjectPath is not null)return config;
        var reserved=store.All<ProjectConfig>("project").Select(PremiereProjects.PathFor).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filename=Paths.SafeName(config.Name)+".prproj";
        for(var index=1;;index++)
        {
            var path=Path.GetFullPath(Path.Combine(config.PremiereRoot,Paths.CollisionName(filename,index)));
            if(!reserved.Contains(path)&&!File.Exists(path)&&!Directory.Exists(path))return config with {PremiereProjectPath=path};
        }
    }
    public static void EnsureDayProjects(IStore store)
    {
        foreach(var command in store.All<AdobeCommand>("adobe").Where(c=>c.State=="pending"&&c.Assets.Length>0).GroupBy(c=>c.DayId).Select(g=>g.First()))
        {
            var id="setup-day:"+command.DayId;
            if(store.Get<AdobeCommand>("adobe",id) is not null)continue;
            var current=store.Get<ProjectConfig>("project",command.ProjectId);
            var config=current is null?command.Config:command.Config with {Devices=current.Devices,AudioSources=current.AudioSources};
            store.Put("adobe",id,command with {Id=id,Config=config,Assets=[],Placements=[],Error=null});
        }
    }
    // Storage locations stay frozen; an administrator can supply missing Adobe presets later.
    public static AdobeCommand RefreshPresets(AdobeCommand command,ProjectConfig current)=>command with {Config=command.Config with {ProxyPreset=BundledPresets.ResolveProxy(current.ProxyPreset),SequencePreset=current.SequencePreset,PremiereTemplate=current.PremiereTemplate}};
    public static string? WaitingReason(AdobeCommand command)=>command.Assets.Any(a=>a.Media?.HasVideo==true)&&string.IsNullOrWhiteSpace(command.Config.ProxyPreset)?"Wskaż sprawdzony preset proxy AME w konfiguracji projektu.":null;
    public static string? PremiereExecutable()=>FindInstall("Adobe Premiere Pro", "Adobe Premiere Pro.exe");
    public static string? EncoderExecutable()=>FindInstall("Adobe Media Encoder", "Adobe Media Encoder.exe");
    private static string? FindInstall(string prefix,string executable)
    {
        var adobe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Adobe");
        return Directory.Exists(adobe)?Directory.GetDirectories(adobe,prefix+"*").OrderByDescending(p=>p,StringComparer.OrdinalIgnoreCase).Select(p=>Path.Combine(p,executable)).FirstOrDefault(File.Exists):null;
    }
    public static string? SequencePreset(AdobeCommand command)
    {
        if(command.Config.SequencePreset is {} chosen)return chosen;
        if(PremiereExecutable() is not {} executable)return null;
        var source=Path.Combine(Path.GetDirectoryName(executable)!,"Settings","SequencePresets","UHD (4K)","UHD (4K) 2160p 50 fps.sqpreset");
        if(!File.Exists(source))return null;
        var path=Path.Combine(command.Config.PremiereRoot,"_DIT","Presety",Paths.SafeName(command.DayId)+".sqpreset");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileSafety.NoLinks(path);var xml=BuildSequencePreset(File.ReadAllText(source),command.Config);File.WriteAllText(path,xml);return path;
    }
    public static string BuildSequencePreset(string source,ProjectConfig config)
    {
        var xml=XDocument.Parse(source);var preset=xml.Descendants("SequencePreset").Single(e=>e.Element("VideoFrameSize") is not null);
        if(preset.Element("VideoFrameSize")!.Value!="0,0,3840,2160"||preset.Element("VideoFrameRate")?.Value!="5080320000")throw new SafetyException("Fabryczny preset Adobe nie jest UHD 50 fps.");
        var tracks=JsonNode.Parse(preset.Element("AudioTracks")!.Value)!.AsArray();var seed=tracks[0]!.DeepClone();var result=new JsonArray();
        foreach(var device in config.Devices)
        {
            var channels=config.AudioSources.FirstOrDefault(s=>s.DeviceId==device.Id)?.Channels??2;
            if(channels>2)throw new SafetyException("Źródło wielokanałowe wymaga sprawdzonego szablonu lub presetu adaptacyjnego. Osobne mikroporty konfiguruj jako źródła mono.");
            var track=seed.DeepClone();track["mName"]=device.Name;track["mChannelType"]=channels==1?0:1;result.Add(track);
        }
        preset.Element("AudioTracks")!.Value=result.ToJsonString();preset.Element("InitialNumberOfVideoTracks")!.Value=Math.Max(1,config.Devices.Count(d=>!d.Audio)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return xml.ToString();
    }
}
