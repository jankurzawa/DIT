using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dit.Core;

namespace Dit.Infrastructure;

// macOS adapter only. WindowsVolumes and the Windows project remain untouched.
public sealed class MacVolumes : IVolumeInspector
{
    private static object Parse(XElement value) => value.Name.LocalName switch
    {
        "dict" => Dictionary(value), "array" => value.Elements().Select(Parse).ToArray(),
        "integer" => long.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture),
        "true" => true, "false" => false, _ => value.Value
    };
    private static Dictionary<string,object> Dictionary(XElement value)
    {
        var items=value.Elements().ToArray();var result=new Dictionary<string,object>();
        for(var i=0;i<items.Length;i+=2)result.Add(items[i].Value,Parse(items[i+1]));
        return result;
    }
    private static Dictionary<string,object> Diskutil(params string[] arguments)
    {
        var start=new ProcessStartInfo("/usr/sbin/diskutil"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Nie uruchomiono diskutil.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(15000)){process.Kill();throw new IOException("diskutil nie odpowiada.");}
        if(process.ExitCode!=0)throw new SafetyException("Nie udało się odczytać dysku: "+error.GetAwaiter().GetResult()+output.GetAwaiter().GetResult());
        return Dictionary(XDocument.Parse(output.GetAwaiter().GetResult()).Root!.Elements().Single());
    }
    public VolumeInfo Inspect(string path)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var full=Path.GetFullPath(path);
        while(!Directory.Exists(full)&&!File.Exists(full))full=Path.GetDirectoryName(full)??throw new IOException("Nie istnieje katalog nadrzędny.");
        // diskutil accepts devices or mount points, not arbitrary subfolders.
        // df resolves the filesystem containing the chosen folder (including
        // the separate APFS Data volume used by /Users on modern macOS).
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var df=Processes.Run("/bin/df",["-P",full],timeout.Token).GetAwaiter().GetResult();
        var device=Regex.Match(df,@"^(/dev/disk\d+(?:s\d+)*)\s",RegexOptions.Multiline);
        if(!device.Success)throw new SafetyException("Nie potwierdzono lokalnego urządzenia dla tego folderu.");
        var info=Diskutil("info","-plist",device.Groups[1].Value);
        var container=info.GetValueOrDefault("APFSContainerReference")?.ToString();
        Dictionary<string,object>? apfs=null;
        if(container is not null)apfs=Diskutil("apfs","list","-plist");
        return Describe(info,apfs);
    }
    public static VolumeInfo Describe(Dictionary<string,object> info,Dictionary<string,object>? apfs)
    {
        string? Text(string key)=>info.GetValueOrDefault(key)?.ToString();
        if(info.GetValueOrDefault("Network") is true||Text("BusProtocol")=="Disk Image"||info.GetValueOrDefault("DiskImage") is true||Text("FilesystemType") is "smbfs" or "nfs" or "afpfs")
            throw new SafetyException("Wybierz lokalny fizyczny dysk. Magazyny sieciowe i obrazy dysków nie są obsługiwane.");
        var physical=Text("ParentWholeDisk")??(info.GetValueOrDefault("Whole") is true?Text("DeviceIdentifier"):null);
        if(Text("APFSContainerReference") is {} reference)
        {
            var containers=apfs?.GetValueOrDefault("Containers") as object[]??[];
            var container=containers.OfType<Dictionary<string,object>>().SingleOrDefault(c=>c.GetValueOrDefault("ContainerReference")?.ToString()==reference);
            var stores=(container?.GetValueOrDefault("PhysicalStores") as object[]??[]).OfType<Dictionary<string,object>>().Select(s=>s.GetValueOrDefault("DeviceIdentifier")?.ToString()).ToArray();
            if(stores.Length!=1)throw new SafetyException("Nie potwierdzono jednego fizycznego dysku APFS. Fusion Drive i magazyny wielodyskowe nie są obsługiwane.");
            physical=stores[0] is {} store?Regex.Match(store,@"^disk\d+").Value:null;
        }
        if(physical is null||!Regex.IsMatch(physical,@"^disk\d+$"))throw new SafetyException("Nie potwierdzono fizycznej tożsamości dysku.");
        var free=info.GetValueOrDefault("APFSContainerFree")??info.GetValueOrDefault("VolumeFreeSpace")??info.GetValueOrDefault("FreeSpace");
        if(free is not long available||available<0)throw new SafetyException("Nie potwierdzono wolnego miejsca na dysku.");
        var volume=Text("VolumeUUID")??Text("DiskUUID")??throw new SafetyException("Brak identyfikatora woluminu.");
        return new(volume,"mac:"+physical,available,true);
    }
}
