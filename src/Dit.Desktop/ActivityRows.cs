using System;
using System.Linq;
using System.Text.Json;
using Dit.Core;

namespace Dit.Desktop;

internal sealed record AlertRow(Alert Alert,string Tab)
{
    public string Key=>Alert.Key;
    public DateTimeOffset At=>Alert.CreatedAt;
    public string AtText=>At.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string Source=>Alert.SourceRoot??"Nie dotyczy / brak danych";
    public string SourceDetails=>$"Źródło: {Source}\nWolumin: {Alert.CardVolumeId??"—"}\nZgrywanie: {Alert.BatchId??"—"}";
    public string Message=>Alert.Message;
    public string Status=>Alert.Resolved?"Wyjaśniony":"Do wyjaśnienia";
    public string Note=>(Alert.Note??"—")+(Alert.ResolvedAt is {} at?$" · {at.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {Alert.ResolvedBy}":"");
}
internal sealed record HistoryRow(AuditEvent Event,string Tab,string Actor,string Action,string Detail)
{
    public string Id=>Event.Id;
    public DateTimeOffset At=>Event.At;
    public string AtText=>At.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string Level=>Event.Level switch {"error"=>"Błąd","warning"=>"Ostrzeżenie",_=>"Informacja"};
    public string Icon=>Event.Level switch {"error"=>"⊗","warning"=>"⚠",_=>"ⓘ"};
    public string Color=>Event.Level switch {"error"=>"#FF9292","warning"=>"#F5C26B",_=>"#65DFC3"};
    public string Source=>Event.SourceRoot??"—";
    public string SourceDetails=>$"Źródło: {Source}\nWolumin: {Event.CardVolumeId??"—"}\nZgrywanie: {Event.BatchId??"—"}";
}
internal sealed record BatchRow(string Id,string Owner,string Source,double Percent,bool Indeterminate,string Summary,string Error)
{
    public static BatchRow From(JsonElement batch,ProjectListItem project)
    {
        var state=batch.GetProperty("stage").GetString();var files=batch.GetProperty("files").GetInt32();var totalFiles=batch.TryGetProperty("totalFiles",out var count)?count.GetInt32():files;
        var owner=batch.GetProperty("ownerId").GetString();var name=project.Tabs.FirstOrDefault(t=>t.UserId==owner)?.Name??owner??"—";
        var source=batch.TryGetProperty("sourceRoot",out var root)?root.GetString()??"—":"—";
        var progress=batch.GetProperty("progress");var total=progress.ValueKind==JsonValueKind.Object?progress.GetProperty("totalBytes").GetInt64():0;
        var main=total>0?progress.GetProperty("mainBytes").GetInt64():0;var backup=total>0?progress.GetProperty("backupBytes").GetInt64():0;
        var secured=state is "Secured" or "WaitingAssignment" or "WaitingMetadata" or "WaitingApproval" or "WaitingAdobe" or "Completed";
        var percent=secured?100:total>0?Math.Clamp(100d*(main+backup)/(2d*total),0,100):0;
        var label=state switch {"Queued"=>"W kolejce","Copying" or "BackingUp"=>"Zgrywanie i backup","WaitingMetadata"=>"Kopie gotowe · analiza metadanych","WaitingAssignment"=>"Kopie gotowe · kontrola organizacji","WaitingApproval"=>"Kopie gotowe · zgoda na montaż","WaitingAdobe"=>"Kopie gotowe · oczekuje na Premiere","Completed"=>"Ukończone","Paused"=>"Wstrzymane","Failed"=>"Błąd","Erasing"=>"Kopie gotowe · czyszczenie",_=>"Kopie zweryfikowane"};
        return new(batch.GetProperty("id").GetString()!,name,source,percent,total==0&&!secured&&state is "Queued" or "Copying",$"{label} · {percent:F0}%\n{files}/{totalFiles} plików"+(total>0?$" · {Math.Min(total,main)/1e9:F2} GB / {total/1e9:F2} GB (główna)":""),batch.GetProperty("error").GetString()??"");
    }
}
