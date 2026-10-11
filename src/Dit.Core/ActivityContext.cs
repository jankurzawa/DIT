namespace Dit.Core;

public static class ActivityContext
{
    public static Batch? FindBatch(string text,IEnumerable<Batch> batches)=>batches.FirstOrDefault(b=>text.Contains(b.Id,StringComparison.Ordinal));
    public static Alert Enrich(Alert alert,ProjectConfig config,Batch[] batches)
    {
        var batch=alert.BatchId is {} id?batches.FirstOrDefault(b=>b.Id==id):FindBatch(alert.Key,batches);
        var tab=alert.TabId??batch?.TabId;
        if(tab is null)
        {
            var device=config.Devices.FirstOrDefault(d=>alert.Key.EndsWith(":"+d.Id,StringComparison.Ordinal));
            if(device is not null)tab=device.Audio?config.Tabs.FirstOrDefault(t=>t.Kind==TabKind.Audio)?.Id:config.Tabs.FirstOrDefault(t=>t.DeviceId==device.Id)?.Id;
        }
        return alert with {BatchId=alert.BatchId??batch?.Id,TabId=tab,SourceRoot=alert.SourceRoot??batch?.Source.Root,CardVolumeId=alert.CardVolumeId??batch?.Source.VolumeId};
    }
    public static AuditEvent Enrich(AuditEvent item,ProjectConfig config,Batch[] batches)
    {
        var batch=item.BatchId is {} id?batches.FirstOrDefault(b=>b.Id==id):FindBatch(item.Detail,batches);
        return item with {Level=Level(item.Action,item.Level),TabId=item.TabId??batch?.TabId??config.Tabs.FirstOrDefault(t=>t.UserId==item.ActorId)?.Id,
            BatchId=item.BatchId??batch?.Id,SourceRoot=item.SourceRoot??batch?.Source.Root,CardVolumeId=item.CardVolumeId??batch?.Source.VolumeId};
    }
    public static string Level(string action,string? level=null)=>action is "critical" or "adobe-failed"?"error":action is "warning" or "number-gap" or "clear-blocked"?"warning":level??"info";
    public static ProjectDayStatus DayStatus(ShootingDay[] days,bool paused)
    {
        var day=days.OrderByDescending(d=>d.Number).FirstOrDefault();return new(day?.Number,day is {Closed:false},day?.Start,day?.End,paused);
    }
    public static bool Matches(AuditEvent item,DateTime? from,DateTime? to,string? search,string extra="")
    {
        var day=item.At.ToLocalTime().Date;
        if(from is {} start&&day<start.Date||to is {} end&&day>end.Date)return false;
        var text=$"{item.Detail} {item.ActorId} {item.Action} {item.TabId} {item.SourceRoot} {item.CardVolumeId} {item.BatchId} {extra}";
        return string.IsNullOrWhiteSpace(search)||text.Contains(search.Trim(),StringComparison.CurrentCultureIgnoreCase);
    }
}
