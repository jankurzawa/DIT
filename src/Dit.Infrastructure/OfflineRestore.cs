using System.Text.Json;
using Dit.Core;

namespace Dit.Infrastructure;

public static class OfflineRestore
{
    public static async Task Restore(string manifest,string destination,Dictionary<string,string> rootMappings,SevenZip archive,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(manifest,ct));
        var assets=document.RootElement.GetProperty("assets").Deserialize<Asset[]>(SqliteStore.Json)??throw new SafetyException("Brak plików w manifeście.");
        foreach(var asset in assets)
        {
            var source=asset.Backup.Path;
            foreach(var (original,current) in rootMappings.OrderByDescending(x=>x.Key.Length))if(Configuration.IsWithin(source,original)){source=Paths.Under(current,Path.GetRelativePath(original,source));break;}
            if(rootMappings.Values.Any(r=>Configuration.IsWithin(destination,r)||Configuration.IsWithin(r,destination)))throw new SafetyException("Odtwarzanie wymaga osobnej lokalizacji.");
            var target=Paths.Under(destination,asset.OriginalPath);
            if(File.Exists(target)){if(await FileSafety.HashAsync(target,ct)!=asset.Hash)throw new SafetyException("Kolizja odtwarzania: "+target);continue;}
            if(asset.Backup.Archive)await archive.Restore(source,asset.LogicalName,target,asset.Hash,ct);
            else if(await FileSafety.CopyVerifiedAsync(source,target,new NoFaults(),ct)!=asset.Hash)throw new SafetyException("Kopia niezgodna z manifestem.");
        }
    }
}
