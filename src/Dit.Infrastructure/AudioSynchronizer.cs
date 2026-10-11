using System.Globalization;
using Dit.Core;

namespace Dit.Infrastructure;

public sealed class AudioSynchronizer(IStore store,IngestEngine engine,string ffmpeg="ffmpeg")
{
    public async Task Run(AdobeCommand command,CancellationToken ct)
    {
        // Commands are durable snapshots; a later manual correction has precedence.
        var assets=command.Assets.Select(a=>store.Get<Asset>("asset",a.Id)??a).ToArray();
        foreach(var asset in assets.Where(a=>a.Media?.AudioChannels>0&&!a.SyncMethod.StartsWith("manual",StringComparison.Ordinal)))
        {
            var device=command.Config.Devices.Single(d=>d.Id==asset.DeviceId);
            if(device.LtcChannel is not {} channel)continue;
            using var output=new MemoryStream();
            await Processes.Run(ffmpeg,["-v","error","-i",asset.Main.Path,"-t","5","-vn","-af",$"pan=mono|c0=c{channel}","-ar","48000","-f","f32le","-"],ct,output);
            var bytes=output.ToArray();var samples=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);
            if(LtcDecoder.Decode(samples,48000,device.LtcFps) is not {} ltc){engine.Warn(command.ProjectId,"ltc:"+asset.Id,"sync","Nie potwierdzono LTC na skonfigurowanym kanale: "+asset.LogicalName);continue;}
            var media=asset.Media! with {LtcStartTicks=ltc.StartTimeOfDayTicks};var day=store.Get<ShootingDay>("day",asset.DayId!)!;
            var position=Timecodes.Position(media,day,command.Config,device);store.Put("asset",asset.Id,asset with {Media=media,PositionTicks=position.Position,SyncMethod=position.Method});
        }
        assets=assets.Select(a=>store.Get<Asset>("asset",a.Id)!).ToArray();
        foreach(var asset in assets.Where(a=>a.Media?.AudioChannels>0&&a.SyncMethod is "unresolved" or "capture-time-approximate"))
        {
            if(engine.Paused)return;
            var anchors=assets.Where(a=>a.Id!=asset.Id&&a.DeviceId!=asset.DeviceId&&a.PositionTicks is not null&&a.Media?.AudioChannels>0&&a.Media.DurationTicks>=TimeSpan.FromSeconds(10).Ticks)
                .OrderByDescending(a=>a.SyncMethod.StartsWith("native",StringComparison.Ordinal)).ThenBy(a=>Math.Abs((a.PositionTicks??0)-(asset.PositionTicks??0))).Take(3);
            foreach(var anchor in anchors)
            {
                var expected=asset.PositionTicks is {} position?position-anchor.PositionTicks!.Value:0;
                var windowStart=Math.Max(0,expected-TimeSpan.FromSeconds(30).Ticks);
                if(windowStart>=anchor.Media!.DurationTicks)continue;
                var duration=Math.Min(TimeSpan.FromSeconds(10).Ticks,asset.Media!.DurationTicks??0);
                if(duration<TimeSpan.FromSeconds(2).Ticks)continue;
                var query=await Read(asset.Main.Path,0,duration,ct);
                var reference=await Read(anchor.Main.Path,windowStart,Math.Min(TimeSpan.FromSeconds(70).Ticks,anchor.Media.DurationTicks!.Value-windowStart),ct);
                var match=AudioCorrelation.Find(reference,query);
                if(match is null)continue;
                var start=checked(anchor.PositionTicks!.Value+windowStart+match.OffsetSamples*TimeSpan.TicksPerSecond/100);
                var method=anchor.SyncMethod.StartsWith("native",StringComparison.Ordinal)?"audio-to-native-timecode":"audio-to-approximate-clock";
                var updated=asset with {PositionTicks=start,SyncMethod=method};store.Put("asset",asset.Id,updated);
                engine.WriteAudit(command.ProjectId,"system","audio-synchronized",$"{asset.Id} ↔ {anchor.Id}; confidence={match.Confidence:F4}; offset={start}");break;
            }
        }
        var current=assets.Select(a=>store.Get<Asset>("asset",a.Id)!).ToArray();
        foreach(var asset in current)
        {
            var day=store.Get<ShootingDay>("day",asset.DayId!)!;
            foreach(var root in command.Config.MainRoots.Concat(command.Config.BackupRoots))FileSafety.DurableJson(Paths.Under(root,Path.Combine("_DIT","Metadane synchronizacji",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asset.Id)))+".json")),new {asset.Id,asset.Hash,asset.PositionTicks,asset.SyncMethod,asset.Media,Epoch=day.Start});
        }
        store.Put("adobe",command.Id,command with {Assets=current,Placements=Timeline.Build(command.Config,current)});
    }
    private async Task<double[]> Read(string path,long start,long duration,CancellationToken ct)
    {
        using var output=new MemoryStream();
        string Seconds(long ticks)=>(ticks/(decimal)TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture);
        await Processes.Run(ffmpeg,["-v","error","-ss",Seconds(start),"-i",path,"-t",Seconds(duration),"-vn","-ac","1","-ar","4000","-f","f32le","-"],ct,output);
        var bytes=output.ToArray();var floats=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,floats,0,bytes.Length);return AudioCorrelation.Envelope(floats);
    }
}
