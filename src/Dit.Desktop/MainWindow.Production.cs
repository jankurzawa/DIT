using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dit.Core;
using Microsoft.Win32;

namespace Dit.Desktop;

public sealed record DayRow(ShootingDay Day)
{
    public int Dzień=>Day.Number;
    public string Stan=>Day.Closed?"Zamknięty":"Otwarty";
    public string Początek=>Day.Start.ToString("dd.MM.yyyy HH:mm");
    public string Koniec=>Day.End?.ToString("dd.MM.yyyy HH:mm")??"—";
}
public sealed record AudioInputProfile(string DeviceId,string Name,int Channels,string[] ChannelNames);
public partial class MainWindow
{
    private readonly List<string> navigation=new();
    private int navigationIndex=-1,daySelectionVersion;

    private void ResetNavigation(){navigation.Clear();navigationIndex=-1;}
    private void RememberView(string key)
    {
        if(navigationIndex>=0&&navigation[navigationIndex]==key)return;
        navigation.RemoveRange(navigationIndex+1,navigation.Count-navigationIndex-1);navigation.Add(key);navigationIndex=navigation.Count-1;
    }
    private void NavigationMouse(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2))return;
        if(Workspace.Visibility!=Visibility.Visible||!adminView)return;
        var next=navigationIndex+(e.ChangedButton==MouseButton.XButton1?-1:1);if(next<0||next>=navigation.Count)return;
        e.Handled=true;navigationIndex=next;var key=navigation[next];
        if(key=="home")ShowAdminHome(false);else if(key=="config")
        {
            if(ConfigurationHost.Content is null)EditConfig(this,new());else ShowConfiguration(null,false);
        }
        else SelectAdminSection(key,false);
    }
    private void ShowConfiguration(ProjectWizard? editor,bool remember=true)
    {
        if(remember)RememberView("config");StopPlayer();AdminDashboard.Visibility=AdminDetail.Visibility=FootageWorkspace.Visibility=Visibility.Collapsed;
        ConfigurationHost.Visibility=Visibility.Visible;BackToTiles.Visibility=Visibility.Visible;ContextTitle.Text="Konfiguracja projektu";
        if(editor is not null)ConfigurationHost.Content=editor.Embed();
    }
    private AssetRow CreateAssetRow(Asset asset,AssetWorkflow? workflow)
    {
        string Mark(bool ready)=>ready?"✓":"○";
        var proxy=workflow?.ProxyRequired==false?"—":Mark(workflow?.Proxy==true);
        var stages=$"{Mark(workflow?.Ingested==true)} Zgrane   {Mark(workflow?.BackedUp==true)} Backup\n{proxy} Proxy   {Mark(workflow?.InPremiere==true)} Premiere   {Mark(workflow?.Synchronized==true)} Sync";
        var detail="✓ Potwierdzono · ○ Oczekuje · — Nie dotyczy\nSynchronizacja: "+asset.SyncMethod+"\nZgrane i backup oznaczają niezależną weryfikację SHA-256. Premiere oznacza zapisany projekt. Przybliżona pozycja nie otrzymuje potwierdzenia synchronizacji.";
        return new(asset,asset.LogicalName,SizeText(asset.Bytes),asset.Media?.DurationTicks is {} ticks?DurationText(ticks):"Nieznany",asset.SyncMethod,project?.Tabs.FirstOrDefault(t=>t.UserId==asset.OwnerId)?.Name??asset.OwnerId,stages,detail,workflow);
    }
    private async Task<DateTimeOffset?> PickDayTime(string title)
    {
        var config=await api.Get<ProjectConfig>("/projects/"+ProjectId()+"/config",Current().Token);
        var picker=new DayTimeDialog(title,config.TimeZone){Owner=this};picker.PreviewMouseDown+=Activity;picker.PreviewKeyDown+=Activity;picker.PreviewMouseMove+=Activity;
        return picker.ShowDialog()==true?picker.Value:null;
    }
    private void UpdateDays(ShootingDay[] days)
    {
        var selected=(DaysGrid.SelectedItem as DayRow)?.Day.Id;
        var rows=days.OrderByDescending(d=>d.Number).Select(d=>new DayRow(d)).ToArray();
        if(DaysGrid.ItemsSource is DayRow[] previous&&previous.SequenceEqual(rows))return;
        DaysGrid.ItemsSource=rows;DaysGrid.SelectedItem=rows.FirstOrDefault(r=>r.Day.Id==selected)??rows.FirstOrDefault();
    }
    private async void DaySelected(object sender,SelectionChangedEventArgs e)
    {
        if(DaysGrid.SelectedItem is not DayRow row)return;var version=++daySelectionVersion;var g=generation;
        if(project?.Id=="preview"){DayStatisticsSummary.Text="Dzień 3 · 68 nagrań · 00:36:30 materiału · 7,3 GB · czas dnia 08:14:00";return;}
        try
        {
            var result=await api.Get<DayStatistics>("/projects/"+ProjectId()+"/days/"+row.Day.Id+"/statistics",Current().Token);
            if(version!=daySelectionVersion||g!=generation||Workspace.Visibility!=Visibility.Visible)return;
            RenderDayStatistics(row.Day,result);
        }catch(Exception ex){if(g==generation)DayStatisticsSummary.Text=ex.Message;}
    }
    private async Task RefreshDayStatistics()
    {
        if(DaysSection.Visibility!=Visibility.Visible||DaysGrid.SelectedItem is not DayRow row)return;
        var version=daySelectionVersion;var g=generation;
        var result=await api.Get<DayStatistics>("/projects/"+ProjectId()+"/days/"+row.Day.Id+"/statistics",Current().Token);
        if(g==generation&&version==daySelectionVersion&&Workspace.Visibility==Visibility.Visible)RenderDayStatistics(row.Day,result);
    }
    private void RenderDayStatistics(ShootingDay day,DayStatistics result)
    {
        DayStatisticsSummary.Text=$"Dzień {day.Number} · {result.Clips} nagrań · {DurationText(result.DurationTicks)} materiału · {SizeText(result.Bytes)}\nCzas dnia: {DurationText(result.ElapsedTicks)} · {result.Cards.Length} zgrywań"+(result.UnknownDurations>0?$" · {result.UnknownDurations} nagrań z nieznanym czasem":"");
        DayCardsGrid.ItemsSource=result.Cards.Select(c=>new {c.BatchId,c.Tab,Source=c.Source+" ["+c.VolumeId+"]",c.Clips,Duration=DurationText(c.DurationTicks)+(c.UnknownDurations>0?" + nieznany":""),Size=SizeText(c.Bytes),Started=c.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")}).ToArray();
    }
    private string[]? selectedAudioFiles;
    private int audioInputVersion;
    private async void ConfigureAudioInputs()
    {
        var audio=tab?.Kind==TabKind.Audio;AudioTrack.Visibility=AudioFiles.Visibility=AudioSourceHint.Visibility=audio?Visibility.Visible:Visibility.Collapsed;
        AudioTrack.ItemsSource=null;selectedAudioFiles=null;StartIngestButton.IsEnabled=!audio;if(!audio)return;
        if(project?.Id=="preview"){SetAudioInputs([]);return;}
        SetAudioInputs([]);AudioSourceHint.Text="Wczytywanie urządzeń audio…";
        await LoadAudioInputs();
    }
    private async Task LoadAudioInputs()
    {
        if(tab?.Kind!=TabKind.Audio||project?.Id=="preview")return;var g=generation;var version=++audioInputVersion;
        try
        {
            var inputs=await api.Get<AudioInputProfile[]>("/projects/"+ProjectId()+"/audio-inputs",Current().Token);if(g!=generation||version!=audioInputVersion)return;
            SetAudioInputs(inputs);
        }catch(Exception ex){if(g==generation&&version==audioInputVersion){AudioSourceHint.Text="Nie wczytano urządzeń: "+ex.Message+" Rozwiń listę, aby ponowić.";StartIngestButton.IsEnabled=false;}}
    }
    private void SetAudioInputs(AudioInputProfile[] inputs)
    {
        var selected=(AudioTrack.SelectedItem as AudioInputProfile)?.DeviceId;
        var options=new[]{new AudioInputProfile("",inputs.Length==0?"Brak urządzeń audio":"Wybierz urządzenie audio…",0,[])}.Concat(inputs).ToArray();
        AudioTrack.ItemsSource=options;AudioTrack.SelectedItem=options.FirstOrDefault(i=>i.DeviceId==selected&&i.DeviceId.Length>0)??options[inputs.Length==1?1:0];UpdateAudioSourceHint();
    }
    private async void AudioSourcesOpened(object? sender,EventArgs e)=>await LoadAudioInputs();
    private void AudioSourceSelected(object sender,SelectionChangedEventArgs e)=>UpdateAudioSourceHint();
    private void UpdateAudioSourceHint()
    {
        if(tab?.Kind!=TabKind.Audio)return;
        var count=AudioTrack.Items.Count-1;var selected=AudioTrack.SelectedItem as AudioInputProfile;
        StartIngestButton.IsEnabled=selected is {DeviceId.Length:>0};
        AudioSourceHint.Text=count<=0?"Brak urządzeń. Administrator: Konfiguracja → Zakładki i audio → Dodaj źródło audio.":StartIngestButton.IsEnabled?$"Wybrane urządzenie: {selected!.Name}. Pliki trafią do Dzień / {selected.Name} i na jego ścieżkę w Premiere.":$"Dostępne urządzenia: {count}. Rozwiń listę i wybierz mikroport, z którego zgrywasz pliki.";
    }
    private IngestRequest CreateIngestRequest()
    {
        if(tab?.Kind!=TabKind.Audio)return new(Source.Text);
        if(AudioTrack.SelectedItem is not AudioInputProfile input||string.IsNullOrEmpty(input.DeviceId))throw new ArgumentException("Wybierz źródło / track audio przed rozpoczęciem zgrywania.");
        if(selectedAudioFiles?.Any(f=>!Paths.IsAudio(f))==true)throw new ArgumentException("Zakładka Audio przyjmuje wyłącznie pliki audio. Usuń z wyboru pliki wideo i inne pliki.");
        return new(Source.Text,input.DeviceId,selectedAudioFiles?.ToArray());
    }
    private void SelectAudioFiles(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Wybierz nagrania dla źródła / tracku audio",Multiselect=true,Filter="Nagrania audio|*.wav;*.bwf;*.rf64;*.mp3;*.flac;*.aac;*.aif;*.aiff;*.m4a"};
        if(dialog.ShowDialog()!=true)return;
        if(dialog.FileNames.Any(f=>!Paths.IsAudio(f))){Status.Text="Wybrano plik inny niż audio. Wybierz wyłącznie nagrania dźwiękowe.";return;}
        var root=System.IO.Path.GetDirectoryName(dialog.FileNames[0])!;
        if(dialog.FileNames.Any(f=>!string.Equals(System.IO.Path.GetDirectoryName(f),root,StringComparison.OrdinalIgnoreCase))){Status.Text="Wybierz pliki z jednego folderu.";return;}
        Source.Text=root;selectedAudioFiles=dialog.FileNames.Select(System.IO.Path.GetFileName).Cast<string>().ToArray();Source.ToolTip=string.Join("\n",selectedAudioFiles);
        Status.Text="Wybrano "+selectedAudioFiles.Length+" plików. Wskaż ich źródło / track i rozpocznij zgrywanie.";
    }
}
