using Dit.Core;
using Dit.Infrastructure;
using Xunit;

public sealed class MacVolumesTests
{
    private static Dictionary<string,object> Info()=>new(){{"VolumeUUID","volume-a"},{"ParentWholeDisk","disk4"},{"VolumeFreeSpace",1000000000L}};
    [Fact] public void PhysicalPartitionsShareOneDiskIdentity()
    {
        var first=MacVolumes.Describe(Info(),null);var second=Info();second["VolumeUUID"]="volume-b";
        Assert.Equal(first.PhysicalDiskId,MacVolumes.Describe(second,null).PhysicalDiskId);
        Assert.NotEqual(first.Id,MacVolumes.Describe(second,null).Id);
    }
    [Fact] public void ApfsVolumesResolveToThePhysicalStore()
    {
        var info=Info();info["APFSContainerReference"]="disk5";info["ParentWholeDisk"]="disk5";info["APFSContainerFree"]=2000000000L;
        var apfs=new Dictionary<string,object>{{"Containers",new object[]{new Dictionary<string,object>{{"ContainerReference","disk5"},{"PhysicalStores",new object[]{new Dictionary<string,object>{{"DeviceIdentifier","disk0s2"}}}}}}}};
        var result=MacVolumes.Describe(info,apfs);Assert.Equal("mac:disk0",result.PhysicalDiskId);Assert.Equal(2000000000L,result.AvailableBytes);
        Assert.Throws<SafetyException>(()=>MacVolumes.Describe(info,null));
    }
    [Fact] public void NetworkImagesAndUnknownIdentitiesAreRejected()
    {
        foreach(var pair in new KeyValuePair<string,object>[] {new("Network",true),new("DiskImage",true),new("FilesystemType","smbfs"),new("ParentWholeDisk","unknown")})
        {var info=Info();info[pair.Key]=pair.Value;Assert.Throws<SafetyException>(()=>MacVolumes.Describe(info,null));}
        var missing=Info();missing.Remove("VolumeUUID");Assert.Throws<SafetyException>(()=>MacVolumes.Describe(missing,null));
    }
}
