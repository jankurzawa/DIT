using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Dit.Core;

namespace Dit.Infrastructure;

public static class Processes
{
    public static async Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct,Stream? stdout=null)
    {
        var start=new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in args) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Nie można uruchomić "+exe);
        using var registration=ct.Register(()=> { try {process.Kill(true);}catch(InvalidOperationException){} });
        var error=process.StandardError.ReadToEndAsync(ct);
        Task<string>? output=stdout is null ? process.StandardOutput.ReadToEndAsync(ct) : null;
        if(stdout is not null) await process.StandardOutput.BaseStream.CopyToAsync(stdout,ct);
        await process.WaitForExitAsync(ct);
        var detail=await error;
        if(process.ExitCode!=0) throw new IOException($"{Path.GetFileName(exe)} zakończył się błędem {process.ExitCode}: {detail}");
        return output is null ? "" : await output;
    }
}

public sealed class Ffprobe(string executable="ffprobe") : IMediaProbe
{
    public async Task<bool> CompleteVideoAsync(string path,CancellationToken ct)
    {
        var text=await Processes.Run(executable,["-v","error","-count_packets","-select_streams","v:0","-show_entries","stream=nb_frames,nb_read_packets","-of","json",path],ct);
        using var json=JsonDocument.Parse(text);var streams=json.RootElement.GetProperty("streams");
        if(streams.GetArrayLength()!=1)return false;var stream=streams[0];
        return stream.TryGetProperty("nb_frames",out var frames)&&long.TryParse(frames.GetString(),out var expected)&&expected>0&&stream.TryGetProperty("nb_read_packets",out var packets)&&long.TryParse(packets.GetString(),out var read)&&read==expected;
    }
    public async Task<MediaInfo?> ProbeAsync(string path,CancellationToken ct)
    {
        if(!Paths.IsMedia(path)) return null;
        var text=await Processes.Run(executable,["-v","error","-show_format","-show_streams","-of","json",path],ct);
        return Parse(text);
    }
    public static MediaInfo Parse(string text)
    {
        using var json=JsonDocument.Parse(text);var root=json.RootElement;
        long? duration=null,videoDuration=null,audioDuration=null;DateTimeOffset? capture=null;string? tcValue=null;var channels=0;var fpsN=0;var fpsD=1;var video=false;var width=0;var height=0;string? codec=null;string? profile=null;
        static long? StreamDuration(JsonElement stream)=>stream.TryGetProperty("duration",out var d)&&decimal.TryParse(d.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)&&seconds>0?checked((long)(seconds*TimeSpan.TicksPerSecond)):null;
        void Tags(JsonElement item)
        {
            if(!item.TryGetProperty("tags",out var tags)) return;
            foreach(var tag in tags.EnumerateObject())
            {
                if(tag.Name.Equals("timecode",StringComparison.OrdinalIgnoreCase)) tcValue??=tag.Value.GetString();
                if(tag.Name.Equals("creation_time",StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(tag.Value.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var t)) capture??=t;
            }
        }
        if(root.TryGetProperty("format",out var format))
        {
            Tags(format);
            if(format.TryGetProperty("duration",out var d) && decimal.TryParse(d.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)) duration=checked((long)(seconds*TimeSpan.TicksPerSecond));
        }
        if(root.TryGetProperty("streams",out var streams)) foreach(var stream in streams.EnumerateArray())
        {
            Tags(stream);var type=stream.TryGetProperty("codec_type",out var ty)?ty.GetString():null;
            if(type=="audio" && stream.TryGetProperty("channels",out var ch)){channels+=ch.GetInt32();var length=StreamDuration(stream);if(length is not null)audioDuration=Math.Max(audioDuration??0,length.Value);}
            if(type=="video")
            {
                video=true;
                videoDuration??=StreamDuration(stream);
                width=stream.TryGetProperty("width",out var w)?w.GetInt32():0;height=stream.TryGetProperty("height",out var h)?h.GetInt32():0;
                codec=stream.TryGetProperty("codec_name",out var c)?c.GetString():null;profile=stream.TryGetProperty("profile",out var pr)?pr.GetString():null;
                if(stream.TryGetProperty("avg_frame_rate",out var rate)) {var p=rate.GetString()!.Split('/'); if(p.Length==2 && int.TryParse(p[0],out var n)&&int.TryParse(p[1],out var den)&&den>0) {fpsN=n;fpsD=den;}}
            }
        }
        // Data, captions and timecode tracks can outlast the actual picture (e.g. DJI).
        duration=video?videoDuration??duration:audioDuration??duration;
        return new(duration,capture,tcValue,fpsN,fpsD,channels,video,capture is null?null:"container-creation-time",Width:width,Height:height,VideoCodec:codec,VideoProfile:profile);
    }
}

public sealed class SevenZip(string executable="7z")
{
    public async Task Create(string input,string archive,CancellationToken ct)
    {
        var temporary=archive+".partial-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        await Processes.Run(executable,["a","-t7z","-m0=lzma2","-mx=1","-ms=off","-y","--",temporary,input],ct);
        await Verify(temporary,Path.GetFileName(input),await FileSafety.HashAsync(input,ct),ct);
        File.Move(temporary,archive,false);
    }
    public async Task Verify(string archive,string entry,string hash,CancellationToken ct)
    {
        var listing=await Processes.Run(executable,["l","-slt","--",archive],ct);
        var section=listing.Split("----------",2);
        if(section.Length!=2) throw new SafetyException("Niepoprawny indeks archiwum.");
        var paths=section[1].Split('\n').Select(x=>x.TrimEnd('\r')).Where(x=>x.StartsWith("Path = ",StringComparison.Ordinal)).Select(x=>x[7..]).ToArray();
        if(paths.Length!=1 || paths[0]!=entry) throw new SafetyException("Niepełna lub nadmiarowa zawartość archiwum.");
        using var output=new HashSink();
        await Processes.Run(executable,["x","-so","--",archive,entry],ct,output);
        if(output.Digest()!=hash) throw new SafetyException("SHA-256 rozpakowanego backupu jest niezgodne.");
    }
    public async Task Restore(string archive,string entry,string destination,string hash,CancellationToken ct)
    {
        await Verify(archive,entry,hash,ct);FileSafety.NoLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp=destination+".partial-"+Guid.NewGuid().ToString("N");
        await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.WriteThrough))
        { await Processes.Run(executable,["x","-so","--",archive,entry],ct,output);output.Flush(true); }
        if(await FileSafety.HashAsync(temp,ct)!=hash) throw new SafetyException("Odtworzenie niezgodne z manifestem.");
        File.Move(temp,destination,false);
    }
    private sealed class HashSink : Stream
    {
        private readonly System.Security.Cryptography.IncrementalHash hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        public string Digest()=>Convert.ToHexString(hash.GetHashAndReset());
        public override void Write(byte[] buffer,int offset,int count)=>hash.AppendData(buffer,offset,count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default) {ct.ThrowIfCancellationRequested();hash.AppendData(buffer.Span);return ValueTask.CompletedTask;}
        public override bool CanRead=>false; public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){} public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing) {if(disposing)hash.Dispose();base.Dispose(disposing);}
    }
}
