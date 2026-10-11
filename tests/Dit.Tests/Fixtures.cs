using Dit.Core;
using Dit.Infrastructure;

namespace Dit.Tests;

public sealed class FakeClock : IClock {public DateTimeOffset UtcNow {get;set;}=new(2026,10,10,10,0,0,TimeSpan.Zero);public void Advance(TimeSpan span)=>UtcNow+=span;}
public sealed class FakeVolumes(string source,string main,string backup) : IVolumeInspector
{
    public long Capacity {get;set;}=long.MaxValue/4;
    public bool Shared {get;set;}
    public bool SourceOnMain {get;set;}
    public string? Swap {get;set;}
    public VolumeInfo Inspect(string path)
    {
        var id=Configuration.IsWithin(path,main)?"main":Configuration.IsWithin(path,backup)?"backup":Configuration.IsWithin(path,source)?"source":"other";
        return new(Swap==id?"swapped":id,Shared?"same":SourceOnMain&&id=="source"?"main":id,Capacity,true);
    }
}
public sealed class FakeProbe(FakeClock clock):IMediaProbe
{
    public MediaInfo? Info {get;set;}=new(TimeSpan.FromSeconds(2).Ticks,clock.UtcNow,null,50,1,2,true);
    public Task<MediaInfo?> ProbeAsync(string path,CancellationToken ct)=>Task.FromResult(Info);
}
public sealed class OneFault(string point):IFaultInjector
{
    private bool fired;
    public void Hit(string name){if(!fired&&name==point){fired=true;throw new IOException("Injected fault: "+point);}}
}
public sealed class Fixture : IDisposable
{
    public string Root {get;}=Path.Combine(Path.GetTempPath(),"dit-tests-"+Guid.NewGuid().ToString("N"));
    public string Source=>Path.Combine(Root,"card");public string Main=>Path.Combine(Root,"main");public string Backup=>Path.Combine(Root,"backup");
    public FakeClock Clock {get;}=new();public SqliteStore Store {get;}public FakeVolumes Volumes {get;}public FakeProbe Probe {get;}
    public ProjectConfig Config {get;set;}
    public ShootingDay Day {get;}
    public Fixture()
    {
        foreach(var dir in new[]{Source,Main,Backup})Directory.CreateDirectory(dir);
        Store=new(Path.Combine(Root,"dit.sqlite"));Volumes=new(Source,Main,Backup);Probe=new(Clock);
        Config=new("project","Film",[Main],[Backup],Path.Combine(Root,"proxy"),Path.Combine(Root,"premiere"),
            [new("admin","Admin",TabKind.Admin,"admin",null),new("cam","Kamera 1",TabKind.Camera,"operator","cam"),new("audio","Audio",TabKind.Audio,"audio",null)],
            [new("cam","Urządzenie 1",false),new("rec","Rejestrator 1",true)], [new("rec",@"^REC.*\.wav$",2,["L","R"]) ]);
        Store.Put("project",Config.Id,Config);Day=new("day",Config.Id,1,Clock.UtcNow.AddHours(-1));Store.Put("day",Day.Id,Day);
    }
    public IngestEngine Engine(IFaultInjector? faults=null,SevenZip? archive=null)=>new(Store,Volumes,Probe,Clock,archive??new SevenZip(),faults??new NoFaults());
    public void File(string relative,byte[]? bytes=null)
    {var path=Paths.Under(Source,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);System.IO.File.WriteAllBytes(path,bytes??Enumerable.Range(0,4096).Select(i=>(byte)(i%251)).ToArray());}
    public Batch Queue(IngestEngine engine,string tab="cam",string actor="operator")=>engine.Enqueue(Config,new("token",actor,tab,false),Source);
    public void Dispose(){Store.Dispose();if(Directory.Exists(Root))Directory.Delete(Root,true);}
}
