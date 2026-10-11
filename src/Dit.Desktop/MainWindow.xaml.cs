using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Dit.Core;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace Dit.Desktop;

public sealed record ProjectListItem(string Id,string Name,TabProfile[] Tabs);
public sealed record AssetRow(Asset Asset,string LogicalName,string Size,string Duration,string Sync,string Owner="",string Stages="",string StageDetails="",AssetWorkflow? Workflow=null,string Error="")
{
    public bool Processing=>Workflow is null||!Workflow.Ingested||!Workflow.BackedUp||(Workflow.ProxyRequired&&!Workflow.Proxy)||!Workflow.InPremiere;
    public int StatusOrder=>Error.Length>0?0:Processing?1:Workflow?.Synchronized!=true?2:3;
    public string Status=>Error.Length>0?"Błąd":Processing?"W toku":Workflow?.Synchronized!=true?"Kopie i Premiere gotowe · do synchronizacji":"Gotowe";
    public string StatusColor=>StatusOrder==0?"#FF9292":StatusOrder<3?"#F5C26B":"#65DFC3";
    public long SortDuration=>Asset.Media?.DurationTicks??-1;
}
public partial class MainWindow : Window
{
    private readonly string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DIT");
    private readonly ApiClient api;
    private readonly Dictionary<string,SessionView> sessions=new();
    private ProjectListItem? project;
    private TabProfile? tab;
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromSeconds(1)};
    private DateTimeOffset lastActivity=DateTimeOffset.UtcNow;
    private DateTimeOffset lastSent=DateTimeOffset.MinValue;
    private int generation;
    private bool refreshing;
    private bool loggingIn;
    private bool initializing;
    private bool changing;
    private Task transitions=Task.CompletedTask;
    private LibVLC? vlc;
    private MediaPlayer? player;
    private System.Windows.Forms.NotifyIcon? tray;
    private bool exit;
    public MainWindow(bool initializeWorker=true)
    {
        InitializeComponent();api=new(data);BuildDashboard();SetupPlayback();SetupAssetFilters();
        Source.TextChanged+=(_,_)=>{selectedAudioFiles=null;Source.ToolTip="Karta lub folder źródłowy";};
        if(initializeWorker)Loaded+=async(_,_)=>await Initialize();
        PreviewMouseMove+=Activity;PreviewMouseDown+=Activity;PreviewMouseDown+=NavigationMouse;PreviewKeyDown+=Activity;
        timer.Tick+=async(_,_)=> {if(sessions.Count>0&&DateTimeOffset.UtcNow-lastActivity>=TimeSpan.FromMinutes(5))await LockEverything();await Refresh();};
        SystemEvents.SessionSwitch+=SessionSwitch;
        Closing+=(_,e)=> {if(!exit) {e.Cancel=true;Hide();tray?.ShowBalloonTip(2000,"DIT pracuje w tle","Kolejka pozostaje aktywna. Kliknij ikonę, aby wrócić.",System.Windows.Forms.ToolTipIcon.Info);}};
        Closed+=(_,_)=> {timer.Stop();playbackTimer.Stop();scrubTimer.Stop();SystemEvents.SessionSwitch-=SessionSwitch;player?.Dispose();vlc?.Dispose();tray?.Dispose();};
    }
    private async Task Initialize()
    {
        if(initializing)return;initializing=true;Shell.IsEnabled=false;StartupOverlay.Visibility=Visibility.Visible;RetryStartupButton.Visibility=Visibility.Collapsed;
        try
        {
            StartupTitle.Text="Uruchamianie programu";StartupDetail.Text="Łączenie z procesem roboczym…";StartupProgress.Value=10;StartupProgress.IsIndeterminate=true;
            await Dispatcher.Yield(DispatcherPriority.Render);
            await StartWorker();
            StartupProgress.IsIndeterminate=false;StartupProgress.Value=35;StartupDetail.Text="Wczytywanie projektów…";
            await LoadProjects();
            StartupProgress.Value=65;StartupDetail.Text="Przygotowywanie podglądu nagrań…";
            string? previewWarning=null;
            try
            {
                var playback=await Task.Run(()=>{LibVLCSharp.Shared.Core.Initialize();var engine=new LibVLC("--no-video-title-show","--avcodec-hw=any");try{return (Engine:engine,Player:new MediaPlayer(engine));}catch{engine.Dispose();throw;}});
                vlc=playback.Engine;player=playback.Player;Video.MediaPlayer=player;ConfigurePlayer();
            }
            catch(Exception ex){previewWarning="Podgląd niedostępny: "+ex.Message;}
            tray=new System.Windows.Forms.NotifyIcon {Icon=System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","dit.ico"))?new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","dit.ico")) : System.Drawing.SystemIcons.Application,Visible=true,Text="DIT — kolejka materiału"};
            tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(()=> {Show();Activate();});
            var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("Otwórz",null,(_,_)=>Dispatcher.Invoke(()=> {Show();Activate();}));menu.Items.Add("Zamknij interfejs (kolejka działa dalej)",null,(_,_)=>Dispatcher.Invoke(()=> {exit=true;Close();}));tray.ContextMenuStrip=menu;
            StartupProgress.Value=100;timer.Start();Status.Text=previewWarning??"Gotowy do pracy. Zaloguj się do zakładki.";Shell.IsEnabled=true;StartupOverlay.Visibility=Visibility.Collapsed;
        }
        catch(Exception ex){StartupProgress.IsIndeterminate=false;StartupTitle.Text="Nie udało się uruchomić programu";StartupDetail.Text=ex.Message;RetryStartupButton.Visibility=Visibility.Visible;}
        finally{initializing=false;}
    }
    private async void RetryStartup(object sender,RoutedEventArgs e)=>await Initialize();
    internal async Task<string[]> RunStartupSmoke()
    {
        var ticks=0;var heartbeat=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};heartbeat.Tick+=(_,_)=>ticks++;heartbeat.Start();
        try
        {
            var startup=Initialize();
            if(StartupOverlay.Visibility!=Visibility.Visible||Shell.IsEnabled)throw new InvalidOperationException("Brak ekranu postępu przed inicjalizacją.");
            await startup;
            if(StartupOverlay.Visibility!=Visibility.Collapsed||!Shell.IsEnabled)throw new InvalidOperationException("Uruchomienie nie powiodło się: "+StartupDetail.Text);
            if(ticks==0)throw new InvalidOperationException("Wątek interfejsu nie odpowiadał podczas uruchamiania.");
            return ["Startup progress appears before initialization","UI dispatcher remains responsive during native playback initialization","Startup completes and enables the workspace"];
        }
        finally{heartbeat.Stop();exit=true;Close();}
    }
    private async Task StartWorker()
    {
        try{await api.Get<JsonElement>("/health");return;}catch(Exception ex) when(ex is HttpRequestException or IOException or TaskCanceledException){}
        var exe=Path.Combine(AppContext.BaseDirectory,"worker","Dit.Worker.exe");
        if(!File.Exists(exe))
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root is not null&&!File.Exists(Path.Combine(root.FullName,"DIT.sln")))root=root.Parent;
            if(root is not null) exe=Path.Combine(root.FullName,"src","Dit.Worker","bin","Debug","net10.0","Dit.Worker.exe");
        }
        if(!File.Exists(exe))throw new IOException("Brak procesu Dit.Worker. Uruchom scripts/run.ps1 lub zbuduj pakiet.");
        var start=new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--data");start.ArgumentList.Add(data);
        Process.Start(start);
        for(var i=0;i<40;i++){await Task.Delay(250);try{await api.Get<JsonElement>("/health");return;}catch(Exception ex)when(ex is HttpRequestException or IOException){}}
        throw new IOException("Proces roboczy nie odpowiada. Sprawdź port 47831.");
    }
    private async Task LoadProjects()
    {
        var id=project?.Id;var list=await api.Get<ProjectListItem[]>("/projects");Projects.ItemsSource=list;Projects.SelectedItem=list.FirstOrDefault(p=>p.Id==id)??list.FirstOrDefault();
    }
    private async void ProjectChanged(object sender,SelectionChangedEventArgs e)
    {
        if(Projects.SelectedItem is not ProjectListItem p)return;
        await LockEverything();project=p;DaySummary.Text="Wczytywanie dnia zdjęciowego…";DayRange.Text="";PublicQueueState.Text="";changing=true;Tabs.Items.Clear();foreach(var t in p.Tabs)Tabs.Items.Add(new TabItem {Header=t.Name,Tag=t});changing=false;Tabs.SelectedIndex=0;await RefreshOverview();
    }
    private async void TabChanged(object sender,SelectionChangedEventArgs e)
    {
        if(changing||e.Source!=Tabs||Tabs.SelectedItem is not TabItem item||item.Tag is not TabProfile selected)return;
        var previous=tab;tab=selected;generation++;HideWorkspace();
        var projectId=project?.Id;
        transitions=Transition(transitions,previous,selected,projectId);await transitions;
    }
    private async Task Transition(Task prior,TabProfile? previous,TabProfile next,string? projectId)
    {
        try
        {
            await prior;
            if(previous is not null&&sessions.TryGetValue(previous.Id,out var old))await api.Post("/session/leave",token:old.Token);
            if(sessions.TryGetValue(next.Id,out var current))
            {
                try{await api.Post("/session/enter",token:current.Token);if(tab?.Id==next.Id&&project?.Id==projectId)ShowWorkspace(current);}
                catch(ApiException){sessions.Remove(next.Id);}
            }
        }
        catch(Exception ex){Error(ex);}
    }
    private SessionView Current()=>tab is not null&&sessions.TryGetValue(tab.Id,out var s)?s:throw new AccessException("Najpierw odblokuj zakładkę.");
    private string ProjectId()=>project?.Id??throw new ArgumentException("Wybierz projekt.");
    private void HideWorkspace()
    {
        ResetNavigation();ConfigurationHost.Content=null;ConfigurationHost.Visibility=Visibility.Collapsed;AudioTrack.ItemsSource=null;selectedAudioFiles=null;Source.Clear();DayCardsGrid.ItemsSource=null;DayStatisticsSummary.Text="Wybierz dzień, aby zobaczyć podsumowanie.";StopPlayer();Workspace.Visibility=Visibility.Collapsed;Locked.Visibility=Visibility.Visible;AssetsGrid.ItemsSource=null;BatchesGrid.ItemsSource=null;StatsGrid.ItemsSource=null;AlertsGrid.ItemsSource=null;HistoryGrid.ItemsSource=null;VolumesGrid.ItemsSource=null;AdobeGrid.ItemsSource=null;AdminSummary.Text="";QueueSummary.Text="";LoginPassword.Clear();
        ClearAssetRows();ClearActivityData();UpdateEntryView();
    }
    private void UpdateEntryView()
    {
        var selected=project is not null&&tab is not null;
        AccountsBar.Visibility=project is null?Visibility.Collapsed:Visibility.Visible;
        LoginForm.Visibility=selected?Visibility.Visible:Visibility.Collapsed;
        NoProjectActions.Visibility=selected?Visibility.Collapsed:Visibility.Visible;
        EntryCaption.Text=selected?"DOSTĘP DO ZAKŁADKI":"TWOJA PRODUKCJA";
        LoginTitle.Text=selected?tab!.Name:"Zacznij od projektu";
        LoginDescription.Text=selected?"Wpisz hasło, jeśli zostało ustawione, i odblokuj zakładkę.":"Utwórz projekt lub wybierz istniejący z listy u góry.";
    }
    internal void RunPrivacySmoke()
    {
        if(Background is not System.Windows.Media.SolidColorBrush {Color:var color}||color!=System.Windows.Media.Color.FromRgb(15,19,25))throw new InvalidOperationException("Główne okno nie otrzymało ciemnego motywu.");
        AssetsGrid.ItemsSource=new[]{new {Secret="PRIVATE TEST"}};HistoryGrid.ItemsSource=new[]{new {Secret="PRIVATE HISTORY"}};VolumesGrid.ItemsSource=new[]{new {Secret="PRIVATE STORAGE"}};Workspace.Visibility=Visibility.Visible;Locked.Visibility=Visibility.Collapsed;
        HideWorkspace();
        if(Workspace.Visibility!=Visibility.Collapsed||Locked.Visibility!=Visibility.Visible||AssetsGrid.ItemsSource is not null||HistoryGrid.ItemsSource is not null||VolumesGrid.ItemsSource is not null||Thumbnail.Source is not null||Video.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Blokada nie wyczyściła prywatnego widoku.");
        Status.Text="Test blokady widoku: OK · wybierz lub utwórz projekt";
    }
    internal void PrepareDesignPreview(string scenario)
    {
        if(scenario=="startup"){Shell.IsEnabled=false;StartupOverlay.Visibility=Visibility.Visible;StartupProgress.Value=65;StartupDetail.Text="Przygotowywanie podglądu nagrań…";return;}
        if(scenario=="welcome"){Status.Text="Gotowy do pracy. Utwórz lub wybierz projekt.";return;}
        var adminPreview=scenario.StartsWith("admin",StringComparison.Ordinal);
        if(!adminPreview&&scenario is not ("login" or "workspace" or "audio"))throw new ArgumentException("Nieznany widok testowy.");
        // Render-only fixtures never log in, call the worker or modify real project data.
        Projects.SelectionChanged-=ProjectChanged;Tabs.SelectionChanged-=TabChanged;
        var profiles=new[]{new TabProfile("admin","Admin",TabKind.Admin,"admin",null),new TabProfile("cam1","Kamera 1",TabKind.Camera,"operator1","cam1"),new TabProfile("cam2","Kamera 2",TabKind.Camera,"operator2","cam2"),new TabProfile("cam3","Kamera 3",TabKind.Camera,"operator3","cam3"),new TabProfile("audio","Audio",TabKind.Audio,"audio",null)};
        project=new("preview","Produkcja / Dzień zdjęciowy",profiles);tab=profiles[adminPreview?0:1];
        Projects.ItemsSource=new[]{project};Projects.SelectedIndex=0;
        foreach(var profile in profiles)Tabs.Items.Add(new TabItem {Header=profile.Name,Tag=profile});Tabs.SelectedIndex=adminPreview?0:1;
        UpdateEntryView();SetDayStatus(new(3,true,new DateTimeOffset(2026,10,11,6,0,0,TimeSpan.FromHours(2)),null,false));Status.Text="Podgląd wyglądu · dane przykładowe";
        if(scenario=="login")return;
        ShowWorkspace(new("preview",tab.UserId,tab.Id,adminPreview));Source.Text="E:\\";
        AssetsGrid.ItemsSource=new[]{new {LogicalName="C0001.MOV",Size="2,4 GB",Duration="00:04:12",Sync="Timecode",Owner="Kamera 1",Stages="✓ Zgrane   ✓ Backup\n✓ Proxy   ✓ Premiere   ✓ Sync",StageDetails="Etapy potwierdzone",Status="Gotowe",StatusColor="#65DFC3"},new {LogicalName="C0002.MOV",Size="1,8 GB",Duration="00:03:06",Sync="Timecode",Owner="Kamera 1",Stages="✓ Zgrane   ✓ Backup\n✓ Proxy   ✓ Premiere   ✓ Sync",StageDetails="Etapy potwierdzone",Status="Gotowe",StatusColor="#65DFC3"},new {LogicalName="C0003.MOV",Size="3,1 GB",Duration="00:05:24",Sync="Do synchronizacji",Owner="Kamera 1",Stages="✓ Zgrane   ✓ Backup\n○ Proxy   ○ Premiere   ○ Sync",StageDetails="Oczekuje na Adobe",Status="W toku",StatusColor="#F5C26B"}};
        EmptyAssetsHint.Visibility=Visibility.Collapsed;AssetsSummary.Text="3 nagrania · Kamera 1";BatchesGrid.ItemsSource=new[]{new BatchRow("preview-transfer","Kamera 1",@"E:\DCIM",47,false,"Zgrywanie i backup · 47%\n18/40 plików · 24,8 GB / 52,0 GB",""),new BatchRow("preview-ready","Kamera 2",@"F:\DCIM",100,false,"Kopie gotowe · oczekuje na Premiere\n24/24 pliki","")};
        if(scenario=="audio")
        {
            tab=profiles[^1];Tabs.SelectedIndex=4;ContextTitle.Text="Audio";AudioTrack.Visibility=AudioFiles.Visibility=Visibility.Visible;
            SetAudioInputs(Enumerable.Range(1,20).Select(i=>new AudioInputProfile("mic"+i,"Mikroport "+i,1,["Kanał 1"])).ToArray());AudioTrack.SelectedIndex=1;
        }
        if(adminPreview)
        {
            using var fixture=JsonDocument.Parse("""
            {"statistics":[{"deviceId":"cam1","clips":12,"originalBytes":78500000000,"backupBytes":78500000000,"proxyBytes":3500000000,"durationTicks":155000000000,"unknownDurations":0},{"deviceId":"audio1","clips":8,"originalBytes":2500000000,"backupBytes":2500000000,"proxyBytes":0,"durationTicks":175000000000,"unknownDurations":0}],"deviceNames":{"cam1":"Kamera 1","audio1":"Audio"},"alerts":[],"pause":{"paused":false,"reason":null},"volumes":[{"path":"D:\\Główny","info":{"physicalDiskId":"disk:1","availableBytes":1200000000000},"error":null},{"path":"C:\\Backup","info":{"physicalDiskId":"disk:0","availableBytes":240000000000},"error":null}],"adobe":[{"id":"cykl-001","state":"pending","error":null}],"audit":[{"id":"fixture-1","projectId":"preview","tabId":"cam1","at":"2026-10-11T09:12:00+02:00","actorId":"operator1","action":"file-verified","detail":"C0001.MOV — kopie zweryfikowane"},{"id":"fixture-2","projectId":"preview","tabId":"admin","at":"2026-10-11T09:10:00+02:00","actorId":"admin","action":"day-opened","detail":"1"}]}
            """);SetAdminData(fixture.RootElement);
            var at=DateTimeOffset.Now;
            alertRows.Add(new(new Alert("preview-alert","preview","day-fallback","Data nagrania nie pasuje do dni produkcji. Zapisano w Dniu 3: DJI_0357.MP4",at,TabId:"cam1",BatchId:"zgrywanie-001",SourceRoot:@"E:\DCIM",CardVolumeId:"KARTA-A"),"Kamera 1"));
            alertRows.Add(new(new Alert("preview-alert-2","preview","critical","Naprawiono wcześniejszą blokadę magazynów",at.AddMinutes(-10),true,"Sprawdzono niezależność kopii",TabId:"cam1",BatchId:"zgrywanie-000",SourceRoot:@"E:\DCIM",CardVolumeId:"KARTA-A",ResolvedAt:at,ResolvedBy:"Admin"),"Kamera 1"));
            foreach(var i in Enumerable.Range(0,40))historyRows.Add(new(new AuditEvent("extra-"+i,at.AddMinutes(-i),"preview","operator1",i%3==0?"critical":"warning","Sprawdzenie materiału C0001.MOV",i%3==0?"error":"warning","cam1",SourceRoot:@"E:\DCIM",CardVolumeId:"KARTA-A"),"Kamera 1","Operator 1",i%3==0?"Błąd krytyczny":"Ostrzeżenie","Sprawdzenie materiału C0001.MOV"));
            SetOptions(HistoryLevel,["Informacja","Ostrzeżenie","Błąd"]);SetOptions(HistoryAction,historyRows.Select(r=>r.Action));SetOptions(HistoryTab,historyRows.Select(r=>r.Tab));ApplyHistoryFilter();
            if(scenario=="admin-config")
            {
                var config=new ProjectConfig("preview","Produkcja",[@"D:\Główny"],[@"C:\Backup"],@"D:\Proxy",@"D:\Premiere",profiles,[new("cam1","Kamera 1",false),new("cam2","Kamera 2",false),new("cam3","Kamera 3",false),new("mic1","Mikroport 1",true)],[new("mic1","",1,["Jan"])]);
                var editor=new ProjectWizard(config);editor.PrepareAccountsPreview();ShowConfiguration(editor);
            }
            else if(scenario=="admin-days"){SelectAdminSection("days");UpdateDays([new("preview-day","preview",3,DateTimeOffset.Now.AddHours(-8))]);RenderDayStatistics(((DayRow)DaysGrid.SelectedItem).Day,new("preview-day",68,7300000000,TimeSpan.FromMinutes(36.5).Ticks,0,TimeSpan.FromHours(8.2).Ticks,[new("zgrywanie-001","Kamera 1",@"E:\DCIM","KARTA-A",68,7300000000,TimeSpan.FromMinutes(36.5).Ticks,0,DateTimeOffset.Now.AddHours(-2),null)]));}
            else if(scenario=="admin-alerts")SelectAdminSection("alerts");else if(scenario=="admin-history")SelectAdminSection("history");else if(scenario=="admin-overview")SelectAdminSection("overview");else if(scenario=="admin-footage")SelectAdminSection("footage");
        }
    }
    private void ShowWorkspace(SessionView s)
    {
        Locked.Visibility=Visibility.Collapsed;Workspace.Visibility=Visibility.Visible;adminView=s.Admin;ContextTitle.Text=tab?.Name;
        ConfigureAudioInputs();IngestBar.Visibility=s.Admin?Visibility.Collapsed:Visibility.Visible;OwnerColumn.Visibility=s.Admin?Visibility.Visible:Visibility.Collapsed;AdminQueueButtons.Visibility=s.Admin?Visibility.Visible:Visibility.Collapsed;

        if(s.Admin)ShowAdminHome();else{AdminDashboard.Visibility=Visibility.Collapsed;AdminDetail.Visibility=Visibility.Collapsed;BackToTiles.Visibility=Visibility.Collapsed;FootageWorkspace.Visibility=Visibility.Visible;}
    }
    private static string SizeText(long bytes)=>bytes>=1_000_000_000?$"{bytes/1e9:N1} GB":$"{bytes/1e6:N1} MB";
    private static string DurationText(long ticks){var t=TimeSpan.FromTicks(ticks);return $"{(long)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";}
    private static string FriendlyState(string? value)=>value switch
    {
        "Queued"=>"W kolejce","Copying"=>"Zgrywanie","BackingUp"=>"Backup","Secured"=>"Kopie zweryfikowane","Erasing"=>"Czyszczenie karty",
        "WaitingMetadata"=>"Kopie gotowe · analiza metadanych","WaitingAssignment"=>"Kopie gotowe · rozpoznawanie źródła audio","WaitingApproval"=>"Oczekuje na zgodę admina","WaitingAdobe"=>"Oczekuje na Premiere",
        "Completed" or "complete"=>"Ukończone","Paused"=>"Wstrzymane","Failed" or "failed"=>"Błąd",
        "pending"=>"W kolejce Adobe","inflight"=>"Praca w Premiere","analyzing"=>"Analiza synchronizacji","superseded"=>"Zastąpione nowszą wersją",_=>value??"—"
    };
    private static string FriendlyAction(string? value)=>value switch
    {
        "login"=>"Logowanie","project-created"=>"Utworzenie projektu","configuration-changed"=>"Zmiana konfiguracji","password-changed"=>"Zmiana hasła",
        "day-opened"=>"Otwarcie dnia","day-closed"=>"Zamknięcie dnia","ingest-queued"=>"Przyjęcie zgrywania","file-verified"=>"Weryfikacja kopii",
        "duplicate-verified"=>"Sprawdzenie duplikatu","source-cleared"=>"Wyczyszczenie karty","stage"=>"Etap zgrywania","critical"=>"Błąd krytyczny",
        "resume"=>"Wznowienie kolejki","alert-note"=>"Notatka do alertu","alert-resolved"=>"Wyjaśnienie alertu","alert-reopened"=>"Ponowne otwarcie alertu","warning"=>"Ostrzeżenie","legacy-storage-recovered"=>"Naprawa blokady magazynów","layout-repaired"=>"Uporządkowanie folderów","asset-assigned"=>"Organizacja nagrania","late-approved"=>"Zatwierdzenie spóźnienia","sync-correction"=>"Korekta synchronizacji",_=>value??"—"
    };
    private static double AdobeProgressPercent(JsonElement item)=>item.GetProperty("state").GetString()=="complete"?100:item.TryGetProperty("progress",out var progress)&&progress.ValueKind==JsonValueKind.Object?progress.GetProperty("percent").GetDouble():0;
    private static string AdobeProgressInfo(JsonElement item)
    {
        if(item.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String)return error.GetString()??"";
        if(!item.TryGetProperty("progress",out var progress)||progress.ValueKind!=JsonValueKind.Object)return "";
        var stage=progress.GetProperty("stage").GetString() switch {"import"=>"Import nagrań","timeline"=>"Timeline zapisany","proxy"=>"Tworzenie proxy","complete"=>"Gotowe",_=>"Adobe"};
        return $"{stage}: {progress.GetProperty("completed").GetInt32()}/{progress.GetProperty("total").GetInt32()} · {AdobeProgressPercent(item):0}%";
    }
    private void SetAdminData(JsonElement admin)
    {
        var stats=JsonSerializer.Deserialize<DeviceStats[]>(admin.GetProperty("statistics"),ApiClient.Json)??[];
        string DeviceName(string id)=>admin.TryGetProperty("deviceNames",out var names)&&names.TryGetProperty(id,out var name)?name.GetString()??id:project?.Tabs.FirstOrDefault(t=>t.DeviceId==id)?.Name??id;
        StatsGrid.ItemsSource=stats.Select(s=>new {Device=DeviceName(s.DeviceId),s.Clips,Duration=DurationText(s.DurationTicks)+(s.UnknownDurations>0?" + nieznany":""),Original=SizeText(s.OriginalBytes),Backup=SizeText(s.BackupBytes),Proxy=SizeText(s.ProxyBytes)}).ToArray();
        AdminSummary.Text=$"{stats.Sum(s=>s.Clips)} nagrań   ·   {DurationText(stats.Sum(s=>s.DurationTicks))} materiału   ·   {SizeText(stats.Sum(s=>s.OriginalBytes))}";
        var pause=admin.GetProperty("pause");QueueSummary.Text=pause.ValueKind==JsonValueKind.Object&&pause.GetProperty("paused").GetBoolean()?"Kolejka wstrzymana: "+pause.GetProperty("reason").GetString():"Kolejka aktywna. Zgrywanie i backup działają w tle.";
        VolumesGrid.ItemsSource=admin.GetProperty("volumes").EnumerateArray().Select(v=>
        {
            var info=v.GetProperty("info");var valid=info.ValueKind==JsonValueKind.Object;
            return new {Folder=v.GetProperty("path").GetString(),Disk=valid?info.GetProperty("physicalDiskId").GetString():"—",Free=valid?SizeText(info.GetProperty("availableBytes").GetInt64()):"—",Status=valid?"Dostępny":v.GetProperty("error").GetString()};
        }).ToArray();
        AdobeGrid.ItemsSource=admin.GetProperty("adobe").EnumerateArray().Select(a=>new {Operation=a.GetProperty("id").GetString(),State=FriendlyState(a.GetProperty("state").GetString()),Info=AdobeProgressInfo(a),Percent=AdobeProgressPercent(a)}).ToArray();
        UpdateActivityData(admin);
    }
    private async Task Refresh()
    {
        await RefreshOverview();
        if(refreshing||project is null||tab is null||!sessions.TryGetValue(tab.Id,out var s))return;
        refreshing=true;var g=generation;var id=project.Id;var tabId=tab.Id;
        try
        {
            var assets=await api.Get<Asset[]>("/projects/"+id+"/assets",s.Token);
            var batches=await api.Get<JsonElement>("/projects/"+id+"/batches",s.Token);
            var workflow=(await api.Get<AssetWorkflow[]>("/projects/"+id+"/workflow",s.Token)).ToDictionary(w=>w.AssetId);
            JsonElement admin=default;if(s.Admin)admin=await api.Get<JsonElement>("/projects/"+id+"/admin",s.Token);
            JsonElement adobeStatus=default;if(s.Admin)adobeStatus=await api.Get<JsonElement>("/projects/"+id+"/adobe-status",s.Token);
            if(g!=generation||!sessions.ContainsKey(tabId))return;
            var failures=batches.EnumerateArray().Where(b=>b.GetProperty("stage").GetString()=="Failed").ToDictionary(b=>b.GetProperty("id").GetString()!,b=>b.GetProperty("error").GetString()??"Błąd zgrywania");
            SetAssetRows(assets.Where(a=>a.IsMedia).Select(a=>CreateAssetRow(a,workflow.GetValueOrDefault(a.Id)) with {Error=failures.GetValueOrDefault(a.BatchId)??""}).ToArray());
            BatchesGrid.ItemsSource=batches.EnumerateArray().Select(b=>BatchRow.From(b,project!)).ToArray();
            if(s.Admin)
            {
                SetAdminData(admin);AdobeConnectionStatus.Text=adobeStatus.GetProperty("connected").GetBoolean()?"● Połączono z panelem Premiere — kolejka działa automatycznie.":"○ Oczekiwanie na panel DIT w Premiere. Pierwsze uruchomienie: Skonfiguruj / otwórz Adobe.";
                var days=await api.Get<ShootingDay[]>("/projects/"+id+"/days",s.Token);if(g!=generation)return;UpdateDays(days);await RefreshDayStatistics();
                var count=admin.GetProperty("alerts").EnumerateArray().Count(x=>!x.GetProperty("resolved").GetBoolean());Status.Text=$"{assets.Count(a=>a.IsMedia)} nagrań · {count} aktywnych alertów · kolejka zabezpiecza dane w tle";
            }
        }
        catch(ApiException ex)when(ex.Status==403){if(sessions.TryGetValue(tabId,out var active)&&active.Token==s.Token){sessions.Remove(tabId);if(g==generation){generation++;HideWorkspace();Status.Text="Zakładka zablokowana.";}}}
        catch(Exception ex){Status.Text=ex.Message;}
        finally{refreshing=false;}
    }
    private async void Activity(object sender,InputEventArgs e)
    {
        lastActivity=DateTimeOffset.UtcNow;
        if(lastActivity-lastSent<TimeSpan.FromSeconds(1))return;lastSent=lastActivity;
        try{await api.Post("/session/activity");}catch(Exception ex)when(ex is HttpRequestException or IOException or ApiException){}
    }
    private async Task LockEverything(bool global=false)
    {
        var tokens=sessions.Values.Select(s=>s.Token).ToArray();generation++;sessions.Clear();HideWorkspace();
        try{if(global)await api.Post("/session/lock-all");else await Task.WhenAll(tokens.Select(token=>api.Post("/session/lock",token:token)));}catch(Exception ex)when(ex is HttpRequestException or IOException or ApiException){}
    }
    private async void SessionSwitch(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock)await Dispatcher.InvokeAsync(()=>LockEverything(true)).Task.Unwrap();}
    private async void LockAll(object sender,RoutedEventArgs e)=>await LockEverything(true);
    private async void Login(object sender,RoutedEventArgs e)
    {
        if(loggingIn||project is null||tab is null)return;loggingIn=true;var g=generation;var selected=tab;
        var id=project.Id;var password=LoginPassword.Password;
        try{await transitions;if(g!=generation)return;var s=await api.Send<SessionView>(HttpMethod.Post,"/login",new LoginRequest(id,selected.Id,selected.UserId,password));LoginPassword.Clear();if(g!=generation){await api.Post("/session/lock",token:s.Token);return;}lastActivity=DateTimeOffset.UtcNow;sessions[selected.Id]=s;ShowWorkspace(s);await Refresh();}catch(Exception ex){Error(ex);}finally{loggingIn=false;}
    }
    private void LoginKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter)Login(sender,e);}
    private async void NewProject(object sender,RoutedEventArgs e)
    {
        var wizard=new ProjectWizard {Owner=this,SubmitAsync=async request=>{await api.Send<ProjectConfig>(HttpMethod.Post,"/projects",request);}};
        TrackWizardActivity(wizard);if(wizard.ShowDialog()!=true||wizard.Request is null)return;
        try{await LoadProjects();Projects.SelectedItem=((ProjectListItem[])Projects.ItemsSource).First(p=>p.Id==wizard.Request.Config.Id);}catch(Exception ex){Error(ex);}
    }
    private void SelectSource(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog {Title="Wybierz kartę pamięci lub folder źródłowy"};if(dialog.ShowDialog()==true)Source.Text=dialog.FolderName;}
    private async void StartIngest(object sender,RoutedEventArgs e){try{await api.Post("/projects/"+ProjectId()+"/ingest",CreateIngestRequest(),Current().Token);Status.Text="Zgrywanie przyjęte do kolejki.";await Refresh();}catch(Exception ex){Error(ex);}}
    private async void EditConfig(object sender,RoutedEventArgs e)
    {
        try
        {
            var config=await api.Get<ProjectConfig>("/projects/"+ProjectId()+"/config",Current().Token);
            var dialog=new ProjectWizard(config);
            dialog.SubmitAsync=async request=>{await api.Send<ProjectConfig>(HttpMethod.Put,"/projects/"+config.Id+"/setup",request,Current().Token);};
            dialog.Saved+=async()=>
            {
                try
                {
                    ConfigurationHost.Content=null;
                    ApplyConfiguration(dialog.Request!);
                    if(tab is not null&&sessions.TryGetValue(tab.Id,out var active))ShowWorkspace(active);else HideWorkspace();
                    await Refresh();Status.Text="Konfiguracja zapisana.";
                }
                catch(Exception ex){Error(ex);}
            };
            dialog.Cancelled+=()=>{ConfigurationHost.Content=null;ShowAdminHome();};
            ShowConfiguration(dialog);
        }catch(Exception ex){Error(ex);}
    }
    private void ApplyConfiguration(CreateProjectRequest request)
    {
        var config=request.Config;var oldProject=project;var selected=tab?.Id;
        foreach(var session in sessions.ToArray())
        {
            var old=oldProject?.Tabs.FirstOrDefault(t=>t.Id==session.Key);var next=config.Tabs.FirstOrDefault(t=>t.Id==session.Key);
            if(old is null||next is null||old.UserId!=next.UserId||old.Kind!=next.Kind||old.DeviceId!=next.DeviceId||request.PasswordChanges?.ContainsKey(session.Value.UserId)==true)sessions.Remove(session.Key);
        }
        generation++;project=new(config.Id,config.Name,config.Tabs);tab=config.Tabs.FirstOrDefault(t=>t.Id==selected)??config.Tabs.First();
        // Refresh labels without invoking the project-switch logout or tab-leave timers.
        Projects.SelectionChanged-=ProjectChanged;changing=true;
        try
        {
            Projects.ItemsSource=((ProjectListItem[])Projects.ItemsSource).Select(p=>p.Id==config.Id?project:p).ToArray();Projects.SelectedItem=project;
            Tabs.Items.Clear();foreach(var profile in config.Tabs)Tabs.Items.Add(new TabItem {Header=profile.Name,Tag=profile});Tabs.SelectedIndex=Array.FindIndex(config.Tabs,t=>t.Id==tab.Id);
        }
        finally{changing=false;Projects.SelectionChanged+=ProjectChanged;}
    }
    private void TrackWizardActivity(ProjectWizard wizard)
    {
        wizard.PreviewKeyDown+=Activity;wizard.PreviewMouseDown+=Activity;wizard.PreviewMouseMove+=Activity;
    }
    private async void OpenDay(object sender,RoutedEventArgs e)
    {
        try{var at=await PickDayTime("Początek dnia zdjęciowego");if(at is null)return;await api.Post("/projects/"+ProjectId()+"/days",new {At=at.Value},Current().Token);await Refresh();}catch(Exception ex){Error(ex);}
    }
    private async void CloseDay(object sender,RoutedEventArgs e)
    {
        try{var days=await api.Get<ShootingDay[]>("/projects/"+ProjectId()+"/days",Current().Token);var day=days.SingleOrDefault(d=>!d.Closed)??throw new ArgumentException("Nie ma otwartego dnia.");var at=await PickDayTime("Zamknięcie dnia "+day.Number);if(at is null)return;await api.Post("/projects/"+ProjectId()+"/days/"+day.Id+"/close",new {At=at.Value},Current().Token);await Refresh();}catch(Exception ex){Error(ex);}
    }
    private async void Pause(object sender,RoutedEventArgs e)=>await AdminPost("pause");
    private async void Resume(object sender,RoutedEventArgs e)=>await AdminPost("resume");
    private async Task AdminPost(string path){try{await api.Post("/projects/"+ProjectId()+"/"+path,token:Current().Token);await Refresh();}catch(Exception ex){Error(ex);}}
    private async void Approve(object sender,RoutedEventArgs e)
    {
        var text=Prompt("Zatwierdzenie spóźnionego materiału","ID zgrywania z tabeli. Potwierdź, że montażysta nie edytuje projektu.","");if(text is not null)await AdminPost("approve/"+Uri.EscapeDataString(text));
    }
    private async void ExportReport(object sender,RoutedEventArgs e)
    {
        try
        {
            var report=await api.Get<JsonElement>("/projects/"+ProjectId()+"/report",Current().Token);var dialog=new SaveFileDialog {Filter="Raport JSON|*.json|Statystyki CSV|*.csv",FileName="raport-dit"};
            if(dialog.ShowDialog()!=true)return;
            var text=JsonSerializer.Serialize(report,ApiClient.Json);
            if(dialog.FilterIndex==2)
            {
                var rows=report.GetProperty("statistics").EnumerateArray().ToArray();
                var columns=new[]{"deviceId","clips","originalBytes","backupBytes","proxyBytes","durationTicks","unknownDurations"};
                string Escape(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
                text=string.Join(",",columns)+Environment.NewLine+string.Join(Environment.NewLine,rows.Select(row=>string.Join(",",columns.Select(column=>Escape(row.GetProperty(column).ToString())))));
            }
            await File.WriteAllTextAsync(dialog.FileName,text);
        }catch(Exception ex){Error(ex);}
    }
    private async void AdobeKey(object sender,RoutedEventArgs e)
    {
        try
        {
            if(sender is Button setupButton)setupButton.IsEnabled=false;
            Status.Text="Przygotowywanie panelu Adobe…";await AdobeInstaller.EnsureInstalled();
            var result=await api.Get<JsonElement>("/projects/"+ProjectId()+"/adobe-key",Current().Token);
            var folder=Path.Combine(data,"Adobe");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"DIT.ditconnection");
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new {key=result.GetProperty("key").GetString()},ApiClient.Json));
            var info=await api.Get<JsonElement>("/projects/"+ProjectId()+"/adobe-status",Current().Token);var executable=info.GetProperty("premiere").GetString();
            if(executable is not null&&!Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)).Any())Process.Start(new ProcessStartInfo(executable){UseShellExecute=false});
            Process.Start(new ProcessStartInfo("explorer.exe"){Arguments="/select,\""+path+"\"",UseShellExecute=true});
            Status.Text="Panel zainstalowany. W Premiere: Okno → UXP Plugins → DIT. Wybierz plik połączenia DIT.ditconnection jeden raz. Jeśli panelu nie widać, uruchom ponownie Premiere po zapisaniu pracy.";
        }catch(Exception ex){Error(ex);}finally{if(sender is Button setupButton)setupButton.IsEnabled=true;}
    }
    private async void ChangePassword(object sender,RoutedEventArgs e)=>await Password();
    private async Task Password()
    {
        try
        {
            var actor=Current();var user=actor.UserId;
            var old=Prompt("Zmiana hasła","Aktualne hasło","",true);if(old is null)return;
            var value=Prompt("Nowe hasło","Wpisz nowe hasło.","",true);if(value is null)return;
            await api.Post("/projects/"+ProjectId()+"/password",new PasswordRequest(user,old,value),actor.Token);
            if(user==actor.UserId)await LockEverything();
        }catch(Exception ex){Error(ex);}
    }
    private async void Assign(object sender,RoutedEventArgs e)
    {
        if(AssetsGrid.SelectedItem is not AssetRow row)return;
        try
        {
            var days=await api.Get<ShootingDay[]>("/projects/"+ProjectId()+"/days",Current().Token);
            var day=Prompt("Przypisanie dnia","Numer dnia zdjęciowego",days.LastOrDefault()?.Number.ToString()??"1");if(day is null)return;
            var device=Prompt("Przypisanie źródła","ID urządzenia (np. kamera1, audio1)",row.Asset.DeviceId??"");if(device is null)return;
            await api.Post("/projects/"+ProjectId()+"/assign",new {AssetId=row.Asset.Id,DayId=days.Single(d=>d.Number==int.Parse(day)).Id,DeviceId=device},Current().Token);await Refresh();
        }catch(Exception ex){Error(ex);}
    }
    private async void Restore(object sender,RoutedEventArgs e)
    {
        if(AssetsGrid.SelectedItem is not AssetRow row)return;
        var folder=new OpenFolderDialog {Title="Folder odtwarzania struktury karty"};if(folder.ShowDialog()!=true)return;
        try{await api.Post("/projects/"+ProjectId()+"/restore",new {BatchId=row.Asset.BatchId,Destination=folder.FolderName},Current().Token);Status.Text="Odtwarzanie zakończone, SHA-256 zgodne.";}catch(Exception ex){Error(ex);}
    }
    private async void CorrectSync(object sender,RoutedEventArgs e)
    {
        if(AssetsGrid.SelectedItem is not AssetRow row)return;
        var text=Prompt("Korekta synchronizacji","Pozycja od początku sekwencji, hh:mm:ss.fff",row.Asset.PositionTicks is {} ticks?TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.fff"):"00:00:00.000");if(text is null)return;
        try{await api.Post("/projects/"+ProjectId()+"/sync",new {AssetId=row.Asset.Id,PositionTicks=TimeSpan.Parse(text,System.Globalization.CultureInfo.InvariantCulture).Ticks},Current().Token);await Refresh();}catch(Exception ex){Error(ex);}
    }
    private async void AlertNote(object sender,RoutedEventArgs e)
    {
        try
        {
            if(AlertsGrid.SelectedItem is not AlertRow row){SelectAdminSection("alerts");Status.Text="Zaznacz alert w tabeli.";return;}
            var note=Prompt("Notatka do alertu",row.Message+"\n\nWpisz ustaloną przyczynę. Sama notatka nie zamyka alertu.",row.Alert.Note??"");if(note is null)return;
            await api.Post("/projects/"+ProjectId()+"/alerts/"+Uri.EscapeDataString(row.Key)+"/note",new {Note=note},Current().Token);await Refresh();
        }catch(Exception ex){Error(ex);}
    }
    private void Error(Exception ex){Status.Text=ex.Message;System.Windows.MessageBox.Show(this,ex.Message,"DIT",MessageBoxButton.OK,MessageBoxImage.Warning);}
    public string? Prompt(string title,string label,string initial,bool password=false,Window? owner=null)
    {
        var dialog=new Window {Title=title,Owner=owner??this,Width=580,SizeToContent=SizeToContent.Height,MinHeight=240,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};var panel=new StackPanel {Margin=new Thickness(28)};panel.Children.Add(new ScrollViewer {MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)}});
        var text=new TextBox {Text=initial};var secret=new PasswordBox();panel.Children.Add(password?secret:text);var button=new Button {Content="Zapisz",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(0,20,0,0)};button.Click+=(_,_)=>dialog.DialogResult=true;panel.Children.Add(button);dialog.Content=panel;return dialog.ShowDialog()==true?(password?secret.Password:text.Text):null;
    }
}
