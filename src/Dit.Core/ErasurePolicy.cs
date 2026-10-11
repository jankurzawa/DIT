namespace Dit.Core;

public static class ErasurePolicy
{
    public static void Require(Batch batch,Asset[] assets,bool currentlyEnabled,bool globallyPaused)
    {
        if(!batch.Config.AutoClear||!currentlyEnabled||globallyPaused)throw new SafetyException("Automatyczne czyszczenie jest wyłączone lub proces wstrzymany.");
        if(batch.Source.Entries.Length==0||batch.Source.Entries.Length!=assets.Length)throw new SafetyException("Niekompletny manifest źródła.");
        if(assets.Select(a=>a.OriginalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=assets.Length)throw new SafetyException("Powielone wpisy źródła.");
        foreach(var entry in batch.Source.Entries)
        {
            var asset=assets.SingleOrDefault(a=>a.OriginalPath==entry.RelativePath)??throw new SafetyException("Brak kopii pliku źródłowego.");
            if(entry.Hash is null||asset.Hash!=entry.Hash||asset.Bytes!=entry.Length||!asset.Main.Verified||!asset.Backup.Verified||asset.Main.Hash!=entry.Hash||asset.Backup.Hash!=entry.Hash)throw new SafetyException("Niezgodny lub niezweryfikowany plik.");
            if(asset.Main.VolumeId==asset.Backup.VolumeId||asset.Main.PhysicalDiskId==asset.Backup.PhysicalDiskId||asset.Main.PhysicalDiskId==batch.Source.PhysicalDiskId||asset.Backup.PhysicalDiskId==batch.Source.PhysicalDiskId)throw new SafetyException("Kopie nie są niezależne.");
        }
    }
}
