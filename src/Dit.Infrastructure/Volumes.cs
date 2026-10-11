using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Dit.Core;
using Microsoft.Win32.SafeHandles;

namespace Dit.Infrastructure;

public sealed class WindowsVolumes : IVolumeInspector
{
    public VolumeInfo Inspect(string path)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\",StringComparison.Ordinal)) throw new SafetyException("Magazyny sieciowe nie są obsługiwane.");
        var mount=new StringBuilder(1024);var volume=new StringBuilder(1024);
        if(!GetVolumePathName(full,mount,mount.Capacity) || !GetVolumeNameForVolumeMountPoint(mount.ToString(),volume,volume.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var handle=CreateFile(volume.ToString().TrimEnd('\\'),0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid || !DeviceIoControl(handle,0x2D1080,IntPtr.Zero,0,out var number,12,out _,IntPtr.Zero))
            throw new SafetyException("Nie udało się potwierdzić fizycznej tożsamości dysku.");
        if(!GetDiskFreeSpaceEx(mount.ToString(),out var available,out _,out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(volume.ToString(),"disk:"+number.Number,checked((long)available),true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceNumber { public uint DeviceType; public uint Number; public uint PartitionNumber; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetVolumePathName(string fileName,StringBuilder volumePath,int length);
    [DllImport("kernel32.dll",EntryPoint="GetVolumeNameForVolumeMountPointW",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetVolumeNameForVolumeMountPoint(string mount,StringBuilder name,int length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string file,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,IntPtr input,uint inputSize,out DeviceNumber output,uint outputSize,out uint returned,IntPtr overlapped);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetDiskFreeSpaceEx(string directory,out ulong available,out ulong total,out ulong free);
}

public sealed class StorageAllocator(IVolumeInspector volumes)
{
    private readonly object gate = new();
    private readonly Dictionary<string,long> reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,int> cursors = new();
    public sealed class Reservation(string path, VolumeInfo volume, Action release) : IDisposable
    {
        public string Root { get; } = path;
        public VolumeInfo Volume { get; } = volume;
        public void Dispose()=>release();
    }
    public Reservation Reserve(string poolId,string[] roots,long bytes,HashSet<string> forbiddenDisks)
    {
        lock(gate)
        {
            var start=cursors.GetValueOrDefault(poolId);
            for(var i=start;i<roots.Length;i++)
            {
                Directory.CreateDirectory(roots[i]);
                FileSafety.NoLinks(roots[i]);
                var info=volumes.Inspect(roots[i]);
                if(!info.StableIdentity || forbiddenDisks.Contains(info.PhysicalDiskId)) throw new SafetyException("Źródło i magazyny muszą mieć niezależne fizyczne dyski.");
                var held=reserved.GetValueOrDefault(info.Id);
                if(info.AvailableBytes-held < bytes) { cursors[poolId]=i+1; continue; }
                cursors[poolId]=i;reserved[info.Id]=checked(held+bytes);
                var disposed=false;
                return new(roots[i],info,()=> { lock(gate) { if(!disposed) { reserved[info.Id]-=bytes; disposed=true; } } });
            }
            throw new SafetyException("Brak miejsca w magazynie: " + poolId);
        }
    }
}
