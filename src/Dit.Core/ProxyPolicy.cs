namespace Dit.Core;

public static class ProxyPolicy
{
    public static string PathFor(AdobeCommand command,ShootingDay day,Asset asset)
    {
        var device=command.Config.Devices.Single(d=>d.Id==asset.DeviceId);
        // Keep old, unwatermarked outputs intact; a new default must not reuse them.
        var suffix=BundledPresets.IsDefaultOrResolved(command.Config.ProxyPreset)?"_proxy_wm.mov":"_proxy.mov";
        return Paths.Under(command.Config.ProxyRoot,Paths.Relative(day.Number,device.Name,Path.GetFileNameWithoutExtension(asset.LogicalName)+suffix));
    }
    public static bool Matches(MediaInfo? original,MediaInfo? proxy)
        =>Mismatch(original,proxy) is null;
    public static string? Mismatch(MediaInfo? original,MediaInfo? proxy)
    {
        if(original is not {HasVideo:true,DurationTicks:>0,FpsNumerator:>0,FpsDenominator:>0}||proxy is not {HasVideo:true,DurationTicks:>0,Width:>0 and <=1280,Height:>0 and <=720,FpsNumerator:>0,FpsDenominator:>0})return "Brak poprawnych metadanych obrazu lub proxy przekracza 1280×720.";
        if(proxy.VideoCodec!="prores"||!string.Equals(proxy.VideoProfile,"Proxy",StringComparison.OrdinalIgnoreCase))return "Proxy musi używać ProRes Proxy.";
        if(Math.Abs(proxy.DurationTicks.Value-original.DurationTicks.Value)>TimeSpan.TicksPerSecond/25)return $"Różna długość obrazu: oryginał {original.DurationTicks.Value/(decimal)TimeSpan.TicksPerSecond:F3} s, proxy {proxy.DurationTicks.Value/(decimal)TimeSpan.TicksPerSecond:F3} s.";
        if(proxy.AudioChannels!=original.AudioChannels)return $"Różna liczba kanałów audio: oryginał {original.AudioChannels}, proxy {proxy.AudioChannels}.";
        if(proxy.FpsNumerator*(long)original.FpsDenominator!=original.FpsNumerator*(long)proxy.FpsDenominator)return "Proxy zmienia klatkaż oryginału.";
        return original.Width<=0||original.Height<=0||Math.Abs(proxy.Width*(long)original.Height-proxy.Height*(long)original.Width)<=2L*Math.Max(original.Width,original.Height)?null:"Proxy zmienia proporcje obrazu.";
    }
    public static void RequireComplete(AdobeCommand command,IReadOnlyDictionary<string,string>? proxies)
    {
        var required=command.Assets.Where(a=>a.Media?.HasVideo==true).Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
        if(proxies is null?required.Count>0:!required.SetEquals(proxies.Keys))throw new SafetyException("Niekompletna lista zweryfikowanych proxy.");
    }
}
