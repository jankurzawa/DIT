using Dit.Core;

namespace Dit.Infrastructure;

public sealed record PreviewJob(string Id,string AssetId,string Path,bool Thumbnail,string State="pending",string? Error=null);
public sealed class PreviewService(IStore store,string ffmpeg="ffmpeg")
{
    private readonly SemaphoreSlim thumbnails=new(1,1);
    public async Task<object> Thumbnail(Asset asset,ProjectConfig config,CancellationToken ct)
    {
        await thumbnails.WaitAsync(ct);
        try{var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asset.Id+":thumb")));if(store.Get<PreviewJob>("preview",id) is {State:"failed"} failed)store.Put("preview",id,failed with {State="pending",Error=null});Request(asset,config,true,false);var job=store.Get<PreviewJob>("preview",id)!;if(job.State!="ready"||!File.Exists(job.Path))await Build(job,ct);return new {Path=job.Path,State="ready"};}
        finally{thumbnails.Release();}
    }
    public object Request(Asset asset,ProjectConfig config,bool thumbnail,bool lightweight)
    {
        if(!thumbnail&&asset.ProxyPath is {} proxy&&File.Exists(proxy))return new {Path=(string?)proxy,State="ready"};
        if(!thumbnail&&!lightweight)return new {Path=(string?)(asset.PreviewPath is {} ready&&File.Exists(ready)?ready:asset.Main.Path),State="ready"};
        var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asset.Id+(thumbnail?":thumb":":preview"))));
        var previous=store.Get<PreviewJob>("preview",id);
        if(previous?.State=="ready"&&File.Exists(previous.Path))return new {Path=(string?)previous.Path,State="ready"};
        if(previous?.State=="failed")throw new SafetyException(previous.Error??"Nie udało się przygotować podglądu.");
        if(previous is null)
        {
            var path=Paths.Under(config.ProxyRoot,Path.Combine("_Podgląd",id+(thumbnail?".jpg":asset.Media?.HasVideo==true?".mp4":".m4a")));
            store.Put("preview",id,new PreviewJob(id,asset.Id,path,thumbnail));
        }
        return new {Path=(string?)null,State="pending"};
    }
    public async Task Build(PreviewJob job,CancellationToken ct)
    {
        var asset=store.Get<Asset>("asset",job.AssetId)??throw new ArgumentException("Nieznany plik podglądu.");
        if(!asset.Main.Verified)throw new SafetyException("Niezweryfikowane nagranie.");
        FileSafety.NoLinks(job.Path);Directory.CreateDirectory(Path.GetDirectoryName(job.Path)!);
        var extension=Path.GetExtension(job.Path);var temporary=job.Path+".partial"+extension;
        List<string> args=["-v","error","-y","-threads","1","-i",asset.Main.Path];
        if(job.Thumbnail)
        {
            if(asset.Media?.HasVideo!=true)throw new ArgumentException("Nagranie audio nie ma miniatury obrazu.");
            args.AddRange(["-frames:v","1","-vf","scale=960:540:force_original_aspect_ratio=decrease","-q:v","3","-threads","1"]);
        }
        else
        {
            if(asset.Media?.HasVideo==true)args.AddRange(["-map","0:v:0","-vf","scale=1280:720:force_original_aspect_ratio=decrease:force_divisible_by=2","-c:v","libx264","-preset","ultrafast","-crf","25","-pix_fmt","yuv420p"]);
            args.AddRange(["-map","0:a:0?","-c:a","aac","-ac","2","-b:a","128k","-movflags","+faststart"]);
        }
        args.Add(temporary);await Processes.Run(ffmpeg,args,ct);
        using(var output=new FileStream(temporary,FileMode.Open,FileAccess.ReadWrite,FileShare.None))output.Flush(true);
        File.Move(temporary,job.Path,true);store.Put("preview",job.Id,job with {State="ready"});
        if(!job.Thumbnail)store.Put("asset",asset.Id,asset with {PreviewPath=job.Path});
    }
}
