using System.Text.RegularExpressions;

namespace Dit.Core;

public static class Paths
{
    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl) ||
            Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase))
            throw new ArgumentException("Niedozwolona nazwa folderu lub pliku: " + name);
        return name;
    }
    public static string Under(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new SafetyException("Ścieżka nie może być absolutna.");
        var canonical = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(canonical, relative));
        if(!result.StartsWith(canonical, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Ścieżka wychodzi poza folder główny.");
        return result;
    }
    public static string CollisionName(string original, int index) => index < 2 ? original : $"{Path.GetFileNameWithoutExtension(original)}({index}){Path.GetExtension(original)}";
    public static bool IsMedia(string path) => new[] { ".mov", ".mp4", ".mxf", ".wav", ".bwf", ".rf64", ".avi", ".mts", ".m2ts", ".mp3", ".flac", ".aac", ".aif", ".aiff", ".m4a" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsAudio(string path)=>new[]{".wav",".bwf",".rf64",".mp3",".flac",".aac",".aif",".aiff",".m4a"}.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static string Relative(int day, string device, string filename) => Path.Combine($"Dzień {day}", SafeName(device), SafeName(filename));
}

public static class PremiereProjects
{
    public static string PathFor(ProjectConfig config)=>config.PremiereProjectPath??Path.Combine(config.PremiereRoot,Paths.SafeName(config.Name)+".prproj");
    public static string SequenceFor(ShootingDay day)=>$"Dzień {day.Number}";
}

public static class Configuration
{
    public static void Validate(ProjectConfig c)
    {
        Paths.SafeName(c.Name);
        if(c.MainRoots.Length == 0 || c.BackupRoots.Length == 0) throw new ArgumentException("Wymagane są lokalizacje główne i backupowe.");
        if(c.Fps != 50 || c.Width != 3840 || c.Height != 2160) throw new ArgumentException("Ta wersja obsługuje timeline UHD 50 fps.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZone);
        if(c.Tabs.Count(t=>t.Kind == TabKind.Admin) != 1 || c.Tabs.Count(t=>t.Kind == TabKind.Audio) != 1) throw new ArgumentException("Projekt wymaga jednej zakładki Admin i jednej Audio.");
        if(c.Devices.Select(d=>d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Devices.Length || c.Devices.Select(d=>d.Id).Distinct().Count()!= c.Devices.Length || c.Tabs.Select(t=>t.Id).Distinct().Count()!=c.Tabs.Length) throw new ArgumentException("Powielone urządzenia lub zakładki.");
        foreach(var d in c.Devices) { Paths.SafeName(d.Name); _ = new Regex(d.NumberPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
        foreach(var t in c.Tabs.Where(t=>t.Kind == TabKind.Camera))
            if(!c.Devices.Any(d=>d.Id==t.DeviceId && !d.Audio)) throw new ArgumentException("Zakładka kamery wymaga urządzenia.");
        foreach(var s in c.AudioSources)
        {
            if(!c.Devices.Any(d=>d.Id==s.DeviceId && d.Audio) || s.Channels < 1 || s.ChannelNames.Length != s.Channels) throw new ArgumentException("Niepoprawne źródło audio.");
            if(!string.IsNullOrEmpty(s.Pattern))_ = new Regex(s.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }
        var roots = c.MainRoots.Concat(c.BackupRoots).Append(c.ProxyRoot).Append(c.PremiereRoot).Select(Path.GetFullPath).ToArray();
        for(var i=0;i<roots.Length;i++) for(var j=i+1;j<roots.Length;j++)
            if(IsWithin(roots[i],roots[j]) || IsWithin(roots[j],roots[i])) throw new ArgumentException("Lokalizacje nie mogą się pokrywać.");
    }
    public static bool IsWithin(string candidate, string root) => (Path.GetFullPath(candidate).TrimEnd('\\','/') + Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(root).TrimEnd('\\','/') + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    public static string? ResolveDevice(ProjectConfig c, TabProfile tab, string path)
    {
        if(tab.Kind == TabKind.Camera) return tab.DeviceId;
        // Existing projects keep their stored matching rules. New projects need
        // no technical pattern: a sole source owns all audio, otherwise use names.
        bool Matches(AudioSource source)
        {
            if(!string.IsNullOrEmpty(source.Pattern))return Regex.IsMatch(path,source.Pattern,RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
            if(c.AudioSources.Length==1)return true;
            var name=c.Devices.Single(d=>d.Id==source.DeviceId).Name;
            var parts=path.Replace('\\','/').Split('/');
            var file=Path.GetFileNameWithoutExtension(parts[^1]);
            return parts.SkipLast(1).Any(p=>p.Equals(name,StringComparison.OrdinalIgnoreCase))||file.Equals(name,StringComparison.OrdinalIgnoreCase)||
                (file.StartsWith(name,StringComparison.OrdinalIgnoreCase)&&file.Length>name.Length&&" _-.".Contains(file[name.Length]));
        }
        var matches = c.AudioSources.Where(Matches).Select(s=>s.DeviceId).Distinct().ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

public static class Days
{
    public static ShootingDay? Resolve(IEnumerable<ShootingDay> days, DateTimeOffset? capture)
    {
        if(capture is null) return null;
        var matches = days.Where(d=>capture >= d.Start && (d.End is null || capture < d.End)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

public static class Timecodes
{
    public static long? Parse(string? tc, int numerator, int denominator)
    {
        if(tc is null || numerator <= 0 || denominator <= 0 || tc.Contains(';')) return null;
        var parts = tc.Split(':');
        if(parts.Length != 4 || parts.Any(p=>!int.TryParse(p,out _))) return null;
        var p=parts.Select(int.Parse).ToArray();
        if(p[0] is <0 or >23 || p[1] is <0 or >59 || p[2] is <0 or >59 || p[3]<0 || (long)p[3]*denominator>=numerator) return null;
        return ((p[0]*3600L+p[1]*60L+p[2])*TimeSpan.TicksPerSecond) + checked((long)p[3]*denominator*TimeSpan.TicksPerSecond/numerator);
    }
    public static (long? Position, string Method) Position(MediaInfo? media, ShootingDay day, ProjectConfig config, DeviceProfile device)
    {
        if(media?.CapturedAt is not {} captured) return (null,"unresolved");
        var corrected = captured.AddTicks(device.ClockOffsetTicks);
        var native=media.LtcStartTicks??Parse(media.Timecode,media.FpsNumerator,media.FpsDenominator);
        if(native is {} time)
        {
            var zone=TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
            var local=TimeZoneInfo.ConvertTime(captured,zone).Date;
            var possible=Enumerable.Range(-1,3).Select(delta=>DateTime.SpecifyKind(local.AddDays(delta).AddTicks(time),DateTimeKind.Unspecified))
                .Where(t=>!zone.IsAmbiguousTime(t)&&!zone.IsInvalidTime(t))
                .Select(t=>new DateTimeOffset(t,zone.GetUtcOffset(t))).OrderBy(t=>Math.Abs((t-captured).Ticks)).ToArray();
            if(possible.FirstOrDefault() is var candidate && possible.Length>0 && Math.Abs((candidate-captured).Ticks)<=TimeSpan.FromMinutes(5).Ticks)
            {
                var nativePosition=(candidate.AddTicks(device.ClockOffsetTicks)-day.Start).Ticks;
                if(nativePosition>=0)return(nativePosition,media.LtcStartTicks is null?"native-timecode-clock-consistent":"native-ltc-clock-consistent");
            }
        }
        var position=(corrected-day.Start).Ticks;
        if(position<0) return (null,"unresolved");
        // Container timestamps are explicitly approximate unless a trusted clock profile is qualified.
        return (position, "capture-time-approximate");
    }
}

public sealed record NumberGap(string Series, long First, long Last);
public static class Numbering
{
    public static NumberGap[] Gaps(IEnumerable<string> originalNames, string pattern)
    {
        var regex = new Regex(pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var parsed=originalNames.Select(n=>regex.Match(Path.GetFileNameWithoutExtension(n))).Where(m=>m.Success && m.Groups["number"].Success)
            .Select(m=>(Series:m.Groups["series"].Value, Number:long.TryParse(m.Groups["number"].Value,out var n)?n:-1)).Where(x=>x.Number>=0);
        List<NumberGap> result=[];
        foreach(var group in parsed.GroupBy(x=>x.Series))
        {
            var values=group.Select(x=>x.Number).Distinct().Order().ToArray();
            for(var i=1;i<values.Length;i++) if(values[i]-values[i-1]>1) result.Add(new(group.Key,values[i-1]+1,values[i]-1));
        }
        return result.ToArray();
    }
}

public static class Timeline
{
    public static TimelinePlacement[] Build(ProjectConfig config, Asset[] assets)
    {
        List<TimelinePlacement> result=[];
        var videoIndex=0; var audioIndex=0;
        foreach(var device in config.Devices)
        {
            var group=assets.Where(a=>a.IsMedia && a.DeviceId==device.Id).ToArray();
            var known=group.Where(a=>a.PositionTicks.HasValue).OrderBy(a=>a.PositionTicks).ToArray();
            long end=0;
            foreach(var asset in known)
            {
                var duration=asset.Media?.DurationTicks ?? throw new SafetyException("Nieznany czas nagrania: " + asset.LogicalName);
                if(duration<=0 || asset.PositionTicks<end) throw new SafetyException("Nakładające się lub niepoprawne nagrania: " + device.Name);
                result.Add(new(asset.Id,device.Id,device.Name,device.Audio,device.Audio?audioIndex:videoIndex,asset.PositionTicks!.Value,duration,false,asset.Media?.AudioChannels??0));
                end=checked(asset.PositionTicks.Value+duration);
            }
            foreach(var asset in group.Where(a=>!a.PositionTicks.HasValue).OrderBy(a=>a.BatchId,StringComparer.Ordinal).ThenBy(a=>a.OriginalPath,StringComparer.Ordinal))
            {
                var duration=asset.Media?.DurationTicks ?? throw new SafetyException("Nieznany czas nagrania: " + asset.LogicalName);
                if(duration<=0) throw new SafetyException("Niepoprawny czas nagrania.");
                result.Add(new(asset.Id,device.Id,device.Name,device.Audio,device.Audio?audioIndex:videoIndex,end,duration,true,asset.Media?.AudioChannels??0));
                end=checked(end+duration);
            }
            if(!device.Audio) videoIndex++;
            audioIndex++;
        }
        return result.ToArray();
    }
}
