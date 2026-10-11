namespace Dit.Core;

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public interface IVolumeInspector { VolumeInfo Inspect(string path); }
public interface IMediaProbe { Task<MediaInfo?> ProbeAsync(string path, CancellationToken ct); }
public interface IFaultInjector { void Hit(string point); }
public sealed class NoFaults : IFaultInjector { public void Hit(string point) { } }
public interface IStore
{
    void Atomic(Action action)=>action();
    void Put<T>(string kind, string id, T value);
    T? Get<T>(string kind, string id);
    T[] All<T>(string kind);
    void Backup(string destination);
}
