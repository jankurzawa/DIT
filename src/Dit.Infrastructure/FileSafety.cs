using System.Security.Cryptography;
using System.Text.Json;
using Dit.Core;

namespace Dit.Infrastructure;

public static class FileSafety
{
    public static void NoLinks(string path)
    {
        for(var p=Path.GetFullPath(path); !string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
            if((Directory.Exists(p)||File.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new SafetyException("Dowiązania i punkty montowania w ścieżce nie są dozwolone: " + p);
    }
    public static CardSnapshot Snapshot(string root, IVolumeInspector volumes)
    {
        root=Path.GetFullPath(root);NoLinks(root);
        var volume=volumes.Inspect(root);List<SourceEntry> entries=[];
        void Walk(string directory)
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                NoLinks(path);
                if(Directory.Exists(path)) Walk(path);
                else { var f=new FileInfo(path); entries.Add(new(Path.GetRelativePath(root,path),f.Length,f.LastWriteTimeUtc.Ticks)); }
            }
        }
        Walk(root);
        if(entries.Count==0) throw new ArgumentException("Źródło nie zawiera plików.");
        if(entries.Select(e=>e.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Count) throw new SafetyException("Niejednoznaczne nazwy źródłowe.");
        return new(root,volume.Id,volume.PhysicalDiskId,entries.ToArray());
    }
    public static async Task<string> HashAsync(string path,CancellationToken ct=default)
    {
        NoLinks(path);
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
    }
    public static async Task<string> CopyVerifiedAsync(string source,string destination,IFaultInjector faults,CancellationToken ct,Action<long>? progress=null)
    {
        NoLinks(source);NoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary=destination+".partial-"+Guid.NewGuid().ToString("N");
        string hash;
        await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan))
        await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.Asynchronous|FileOptions.WriteThrough))
        using(var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer=new byte[1024*1024];int n;long written=0;
            while((n=await input.ReadAsync(buffer,ct))>0) { faults.Hit("copy-chunk");digest.AppendData(buffer,0,n);await output.WriteAsync(buffer.AsMemory(0,n),ct);written+=n;progress?.Invoke(written); }
            await output.FlushAsync(ct);output.Flush(true);hash=Convert.ToHexString(digest.GetHashAndReset());
        }
        faults.Hit("before-readback");
        if(await HashAsync(temporary,ct)!=hash) throw new SafetyException("Błąd SHA-256 po ponownym odczycie kopii.");
        faults.Hit("before-publish");
        File.Move(temporary,destination,false);
        return hash;
    }
    public static void DurableJson<T>(string destination,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);NoLinks(destination);
        var temporary=destination+".partial-"+Guid.NewGuid().ToString("N");
        using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
        { JsonSerializer.Serialize(file,value,SqliteStore.Json);file.Flush(true); }
        File.Move(temporary,destination,true);
    }
}
