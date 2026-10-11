using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Dit.Core;
using Microsoft.Win32.SafeHandles;

namespace Dit.Infrastructure;

public sealed class CardEraser(IVolumeInspector volumes,SevenZip archive,IFaultInjector faults)
{
    public async Task Erase(Batch batch,Asset[] assets,Func<bool> enabled,Action<Batch> checkpoint,CancellationToken ct)
    {
        ErasurePolicy.Require(batch,assets,enabled(),false);
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var root=Path.GetFullPath(batch.Source.Root);
        if(!string.Equals(root.TrimEnd('\\'),Path.GetPathRoot(root)!.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
            throw new SafetyException("Automatyczne czyszczenie wymaga wskazania całego nośnika, nie podfolderu.");
        if(new DriveInfo(root).DriveType!=DriveType.Removable) throw new SafetyException("Nośnik nie jest oznaczony jako wymienny. Czyszczenie zablokowane.");
        if(batch.Source.Entries.Length!=assets.Length || assets.Select(a=>a.OriginalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=assets.Length)
            throw new SafetyException("Niekompletny manifest przed usuwaniem.");
        List<(SourceEntry Entry,SafeFileHandle Handle)> handles=[];
        try
        {
            foreach(var entry in batch.Source.Entries)
            {
                ct.ThrowIfCancellationRequested();if(!enabled()) throw new SafetyException("Czyszczenie zostało wyłączone.");
                var path=Paths.Under(root,entry.RelativePath);FileSafety.NoLinks(path);
                var handle=CreateFile(path,0x80000000|0x00010000,1,IntPtr.Zero,3,0,IntPtr.Zero);
                if(handle.IsInvalid) {handle.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),"Nie uzyskano wyłącznego dostępu do źródła.");}
                handles.Add((entry,handle));
                using var borrowed=new SafeFileHandle(handle.DangerousGetHandle(),false);
                await using var input=new FileStream(borrowed,FileAccess.Read,1024*1024,false);
                if(input.Length!=entry.Length || Convert.ToHexString(await SHA256.HashDataAsync(input,ct))!=entry.Hash) throw new SafetyException("Źródło zmieniło się przed usuwaniem.");
                var asset=assets.Single(a=>a.OriginalPath==entry.RelativePath);
                if(!asset.Main.Verified||!asset.Backup.Verified||asset.Hash!=entry.Hash || asset.Main.PhysicalDiskId==asset.Backup.PhysicalDiskId || asset.Main.PhysicalDiskId==batch.Source.PhysicalDiskId || asset.Backup.PhysicalDiskId==batch.Source.PhysicalDiskId) throw new SafetyException("Brak dwóch niezależnych kopii.");
                foreach(var replica in new[]{asset.Main,asset.Backup})
                {
                    var now=volumes.Inspect(replica.Path);
                    if(!now.StableIdentity||now.Id!=replica.VolumeId||now.PhysicalDiskId!=replica.PhysicalDiskId) throw new SafetyException("Podmieniono dysk kopii.");
                    if(replica.Archive) await archive.Verify(replica.Path,asset.LogicalName,asset.Hash,ct);
                    else if(await FileSafety.HashAsync(replica.Path,ct)!=asset.Hash) throw new SafetyException("Kopia uszkodzona przed usunięciem.");
                }
            }
            var snapshot=FileSafety.Snapshot(root,volumes);
            if(snapshot.VolumeId!=batch.Source.VolumeId||snapshot.PhysicalDiskId!=batch.Source.PhysicalDiskId || !snapshot.Entries.SequenceEqual(batch.Source.Entries.Select(e=>e with {Hash=null}))) throw new SafetyException("Źródło zmienione lub zawiera nowe pliki.");
            faults.Hit("before-erase");
            batch=batch with {ClearStarted=true,Stage=BatchStage.Erasing};checkpoint(batch);
            List<string> deleted=[];
            foreach(var (entry,handle) in handles)
            {
                ct.ThrowIfCancellationRequested();if(!enabled()) throw new SafetyException("Czyszczenie zostało wyłączone.");
                var now=volumes.Inspect(root);
                if(now.Id!=batch.Source.VolumeId||now.PhysicalDiskId!=batch.Source.PhysicalDiskId) throw new SafetyException("Podmieniono źródło.");
                faults.Hit("erase-file");
                var disposition=new Disposition {DeleteFile=1};
                if(!SetFileInformationByHandle(handle,4,ref disposition,4)) throw new Win32Exception(Marshal.GetLastWin32Error());
                handle.Dispose();deleted.Add(entry.RelativePath);batch=batch with {DeletedPaths=deleted.ToArray()};checkpoint(batch);
            }
            checkpoint(batch with {ClearDone=true,Stage=BatchStage.Secured});
        }
        finally {foreach(var (_,handle) in handles) handle.Dispose();}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition {public int DeleteFile;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string file,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file,int info,ref Disposition buffer,uint size);
}
