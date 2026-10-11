using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Dit.Core;

namespace Dit.Desktop;

public partial class MainWindow
{
    private const string All="Wszystkie";
    private readonly ObservableCollection<HistoryRow> historyRows=new();
    private readonly ObservableCollection<AlertRow> alertRows=new();
    private bool overviewRefreshing;
    private bool updatingFilters;
    private async Task RefreshOverview()
    {
        if(overviewRefreshing||project is null)return;
        overviewRefreshing=true;var id=project.Id;
        try{var status=await api.Get<ProjectDayStatus>("/projects/"+id+"/overview");if(project?.Id==id)SetDayStatus(status);}
        catch(Exception ex){if(project?.Id==id){DaySummary.Text="Status dnia niedostępny";DayRange.Text="";Status.Text=ex.Message;}}
        finally{overviewRefreshing=false;}
    }
    private void SetDayStatus(ProjectDayStatus status)
    {
        DaySummary.Text=status.Number is {} day?$"Dzień {day} · {(status.Open?"OTWARTY":"ZAMKNIĘTY")}":"Nie otwarto dnia zdjęciowego";
        DayRange.Text=status.Start is {} start?$"{start:dd.MM.yyyy HH:mm} → {(status.End is {} end?end.ToString("dd.MM.yyyy HH:mm"):"w toku")}":"";
        PublicQueueState.Text=status.Paused?"Kolejka wstrzymana · Admin → Wznów":"Kolejka aktywna";
    }
    private void OverviewMouseWheel(object sender,MouseWheelEventArgs e){OverviewScroll.ScrollToVerticalOffset(OverviewScroll.VerticalOffset-e.Delta);e.Handled=true;}
    private void ClearActivityData()
    {
        historyRows.Clear();alertRows.Clear();updatingFilters=true;
        try{HistoryLevel.ItemsSource=null;HistoryAction.ItemsSource=null;HistoryTab.ItemsSource=null;HistorySearch.Clear();HistoryFrom.SelectedDate=null;HistoryTo.SelectedDate=null;HistoryCount.Text="";}
        finally{updatingFilters=false;}
    }
    private void UpdateActivityData(JsonElement admin)
    {
        string TabName(string? id)=>project?.Tabs.FirstOrDefault(t=>t.Id==id)?.Name??id??"System / projekt";
        var alerts=admin.GetProperty("alerts").Deserialize<Alert[]>(ApiClient.Json)??[];
        var selected=(AlertsGrid.SelectedItem as AlertRow)?.Key;
        foreach(var item in alerts)
        {
            var row=new AlertRow(item,TabName(item.TabId));var old=alertRows.FirstOrDefault(r=>r.Key==row.Key);
            if(old is null)alertRows.Add(row);else if(old!=row)alertRows[alertRows.IndexOf(old)]=row;
        }
        foreach(var old in alertRows.Where(r=>alerts.All(a=>a.Key!=r.Key)).ToArray())alertRows.Remove(old);
        if(AlertsGrid.ItemsSource is null){AlertsGrid.ItemsSource=alertRows;var view=CollectionViewSource.GetDefaultView(alertRows);if(view.SortDescriptions.Count==0)view.SortDescriptions.Add(new(nameof(AlertRow.At),ListSortDirection.Descending));}
        AlertsGrid.SelectedItem=alertRows.FirstOrDefault(r=>r.Key==selected);
        var events=admin.GetProperty("audit").Deserialize<AuditEvent[]>(ApiClient.Json)??[];
        var known=historyRows.ToDictionary(r=>r.Id);var changed=false;
        foreach(var item in events)
        {
            var row=new HistoryRow(item,TabName(item.TabId),project?.Tabs.FirstOrDefault(t=>t.UserId==item.ActorId)?.Name??item.ActorId,FriendlyAction(item.Action),item.Action=="stage"?FriendlyState(item.Detail):item.Detail);
            if(!known.TryGetValue(row.Id,out var old)){historyRows.Add(row);changed=true;}
            else if(old!=row){historyRows[historyRows.IndexOf(old)]=row;changed=true;}
        }
        if(HistoryGrid.ItemsSource is null)
        {
            HistoryGrid.ItemsSource=historyRows;var view=CollectionViewSource.GetDefaultView(historyRows);view.Filter=MatchesHistory;
            if(view.SortDescriptions.Count==0)view.SortDescriptions.Add(new(nameof(HistoryRow.At),ListSortDirection.Descending));changed=true;
        }
        if(changed||HistoryLevel.ItemsSource is null)
        {
            updatingFilters=true;
            try{SetOptions(HistoryLevel,["Informacja","Ostrzeżenie","Błąd"]);SetOptions(HistoryAction,historyRows.Select(r=>r.Action));SetOptions(HistoryTab,historyRows.Select(r=>r.Tab));}
            finally{updatingFilters=false;}
            ApplyHistoryFilter();
        }
    }
    private static void SetOptions(ComboBox box,System.Collections.Generic.IEnumerable<string> values)
    {
        var selected=box.SelectedItem as string;var options=new[]{All}.Concat(values.Distinct().OrderBy(x=>x)).ToArray();
        box.ItemsSource=options;box.SelectedItem=options.Contains(selected)?selected:All;
    }
    private bool MatchesHistory(object item)
    {
        if(item is not HistoryRow row)return false;
        bool Choice(ComboBox box,string value)=>box.SelectedItem is not string selected||selected==All||selected==value;
        return Choice(HistoryLevel,row.Level)&&Choice(HistoryAction,row.Action)&&Choice(HistoryTab,row.Tab)&&
            ActivityContext.Matches(row.Event,HistoryFrom.SelectedDate,HistoryTo.SelectedDate,HistorySearch.Text,row.Actor+" "+row.Tab+" "+row.Action);
    }
    private void ApplyHistoryFilter()
    {
        if(updatingFilters||HistoryGrid is null)return;
        var view=CollectionViewSource.GetDefaultView(historyRows);view.Refresh();
        HistoryCount.Text=HistoryFrom.SelectedDate>HistoryTo.SelectedDate?"Początek zakresu musi być wcześniejszy niż koniec.":$"{view.Cast<object>().Count()} z {historyRows.Count} zdarzeń · kliknij nagłówek kolumny, aby sortować";
    }
    private void HistoryFilterChanged(object sender,SelectionChangedEventArgs e)=>ApplyHistoryFilter();
    private void HistorySearchChanged(object sender,TextChangedEventArgs e)=>ApplyHistoryFilter();
    private void ClearHistoryFilters(object sender,RoutedEventArgs e)
    {
        updatingFilters=true;HistoryLevel.SelectedItem=All;HistoryAction.SelectedItem=All;HistoryTab.SelectedItem=All;HistoryFrom.SelectedDate=null;HistoryTo.SelectedDate=null;HistorySearch.Clear();updatingFilters=false;ApplyHistoryFilter();
    }
    private async void ResolveAlert(object sender,RoutedEventArgs e)=>await ChangeAlertResolution(true);
    private async void ReopenAlert(object sender,RoutedEventArgs e)=>await ChangeAlertResolution(false);
    private async Task ChangeAlertResolution(bool resolved)
    {
        if(AlertsGrid.SelectedItem is not AlertRow row){SelectAdminSection("alerts");Status.Text="Zaznacz alert w tabeli.";return;}
        var note=Prompt(resolved?"Wyjaśnienie alertu":"Ponowne otwarcie alertu",row.Message+"\n\nPodaj przyczynę. Ta operacja nie wznawia kolejki i nie zmienia plików.",row.Alert.Note??"");
        if(note is null)return;
        try{await api.Post("/projects/"+ProjectId()+"/alerts/"+Uri.EscapeDataString(row.Key)+"/resolve",new AlertResolutionRequest(resolved,note),Current().Token);await Refresh();}
        catch(Exception ex){Error(ex);}
    }
    internal string[] RunActivitySmoke()
    {
        PrepareDesignPreview("admin-history");
        var root=(FrameworkElement)Content;root.Measure(new Size(1440,920));root.Arrange(new Rect(0,0,1440,920));root.UpdateLayout();
        if(!DaySummary.Text.Contains("Dzień 3")||!DaySummary.Text.Contains("OTWARTY"))throw new InvalidOperationException("Brak statusu dnia.");
        var view=CollectionViewSource.GetDefaultView(historyRows);var total=historyRows.Count;
        HistoryLevel.SelectedItem="Błąd";if(view.Cast<HistoryRow>().Count()==0||view.Cast<HistoryRow>().Any(r=>r.Event.Level!="error"))throw new InvalidOperationException("Filtr poziomu nie działa.");
        ClearHistoryFilters(this,new());HistoryAction.SelectedItem="Ostrzeżenie";if(view.Cast<HistoryRow>().Any(r=>r.Action!="Ostrzeżenie"))throw new InvalidOperationException("Filtr operacji nie działa.");
        ClearHistoryFilters(this,new());HistoryTab.SelectedItem="Admin";if(view.Cast<HistoryRow>().Count()!=1)throw new InvalidOperationException("Filtr zakładki nie działa.");
        ClearHistoryFilters(this,new());HistorySearch.Text="KARTA-A";if(view.Cast<HistoryRow>().Count()!=40)throw new InvalidOperationException("Wyszukiwanie karty nie działa.");
        ClearHistoryFilters(this,new());HistoryFrom.SelectedDate=DateTime.Today.AddYears(1);if(view.Cast<HistoryRow>().Any())throw new InvalidOperationException("Filtr daty nie działa.");
        ClearHistoryFilters(this,new());view.SortDescriptions.Clear();view.SortDescriptions.Add(new(nameof(HistoryRow.At),ListSortDirection.Ascending));
        var ordered=view.Cast<HistoryRow>().Select(r=>r.At).ToArray();if(!ordered.SequenceEqual(ordered.OrderBy(x=>x)))throw new InvalidOperationException("Sortowanie czasu nie działa.");
        var source=HistoryGrid.ItemsSource;historyRows.Add(historyRows[0] with {Event=historyRows[0].Event with {Id="new-event"}});ApplyHistoryFilter();
        if(!ReferenceEquals(source,HistoryGrid.ItemsSource)||view.SortDescriptions[0].Direction!=ListSortDirection.Ascending)throw new InvalidOperationException("Odświeżenie resetuje sortowanie.");
        var buttons=0;
        void CheckButtons(DependencyObject parent)
        {
            if(parent is Button button){buttons++;if(string.IsNullOrWhiteSpace(button.ToolTip?.ToString())||ToolTipService.GetPlacement(button)!=System.Windows.Controls.Primitives.PlacementMode.Bottom)throw new InvalidOperationException("Brak tooltipu przycisku.");}
            foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())CheckButtons(child);
        }
        CheckButtons(this);CheckButtons(new ProjectWizard());if(buttons<30)throw new InvalidOperationException("Niepełne sprawdzenie przycisków: "+buttons);
        if(AlertsGrid.Columns.Any(c=>c is DataGridCheckBoxColumn))throw new InvalidOperationException("Alerty nadal mają nieaktywne checkboxy.");
        SelectAdminSection("overview");root.UpdateLayout();StatsGrid.Height=1000;root.UpdateLayout();OverviewScroll.ScrollToVerticalOffset(200);root.UpdateLayout();if(OverviewScroll.VerticalOffset<100)throw new InvalidOperationException("Przegląd nie przewija treści.");
        SelectAdminSection("footage");
        NavigationMouse(this,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.XButton1){RoutedEvent=Mouse.PreviewMouseDownEvent});
        if(OverviewSection.Visibility!=Visibility.Visible)throw new InvalidOperationException("Przycisk myszy Wstecz nie przywraca poprzedniej sekcji.");
        NavigationMouse(this,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.XButton2){RoutedEvent=Mouse.PreviewMouseDownEvent});
        if(FootageWorkspace.Visibility!=Visibility.Visible)throw new InvalidOperationException("Przycisk myszy Dalej nie przywraca nagrań.");
        ShowConfiguration(new ProjectWizard());root.UpdateLayout();
        if(ConfigurationHost.Content is null||ConfigurationHost.ActualWidth<1000||Grid.GetColumn(BackToTiles)!=0)throw new InvalidOperationException("Konfiguracja nie zajmuje przestrzeni roboczej lub powrót nie jest po lewej.");
        NavigationMouse(this,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.XButton1){RoutedEvent=Mouse.PreviewMouseDownEvent});NavigationMouse(this,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.XButton2){RoutedEvent=Mouse.PreviewMouseDownEvent});
        if(ConfigurationHost.Visibility!=Visibility.Visible)throw new InvalidOperationException("Nawigacja nie odtwarza konfiguracji.");
        var adminSession=new SessionView("admin-session",tab!.UserId,tab.Id,true);sessions[tab.Id]=adminSession;var cameraTab=project!.Tabs.First(t=>t.Kind==TabKind.Camera);var cameraSession=new SessionView("camera-session",cameraTab.UserId,cameraTab.Id,false);sessions[cameraTab.Id]=cameraSession;
        var editedConfig=new ProjectConfig(project.Id,"Nowa nazwa",[],[],"","",project.Tabs.Select(t=>t.Id==cameraTab.Id?t with {Name="Nowa kamera"}:t).ToArray(),[],[]);
        ApplyConfiguration(new(editedConfig,[]));
        if(sessions[tab.Id]!=adminSession||sessions[cameraTab.Id]!=cameraSession||project.Name!="Nowa nazwa"||((TabProfile)((TabItem)Tabs.Items[1]).Tag).Name!="Nowa kamera")throw new InvalidOperationException("Zapis konfiguracji wylogowuje lub nie aktualizuje nazw.");
        ApplyConfiguration(new(editedConfig,[],new(){[cameraSession.UserId]="new-password"}));
        if(sessions.ContainsKey(cameraTab.Id)||sessions[tab.Id]!=adminSession)throw new InvalidOperationException("Zmiana hasła nie ogranicza wylogowania do właściwego konta.");
        SetDayStatus(new(3,false,DateTimeOffset.Now,DateTimeOffset.Now,false));HideWorkspace();if(!DaySummary.Text.Contains("ZAMKNIĘTY")||AccountsBar.Visibility!=Visibility.Visible||historyRows.Count!=0||alertRows.Count!=0)throw new InvalidOperationException("Blokada ukrywa status dnia lub pozostawia prywatną historię.");
        tab=project!.Tabs.Single(t=>t.Kind==TabKind.Audio);SetAudioInputs([new("mic-a","Jan",1,["Jan"]),new("mic-b","Anna",1,["Anna"])]);Source.Text="E:\\Audio";
        try{CreateIngestRequest();throw new InvalidOperationException("Zgrywanie bez źródła przyjęte.");}catch(ArgumentException){}
        AudioTrack.SelectedIndex=2;selectedAudioFiles=["take.wav"];var request=CreateIngestRequest();selectedAudioFiles[0]="other.wav";
        if(request.AudioDeviceId!="mic-b"||request.Files![0]!="take.wav"||request.Source!="E:\\Audio")throw new InvalidOperationException("Dropdown nie zamraża przypisania plików do wybranego źródła.");
        Source.Text="F:\\Audio";if(CreateIngestRequest().Files is not null)throw new InvalidOperationException("Zmiana folderu zachowała obce pliki.");
        tab=project.Tabs.First(t=>t.Kind==TabKind.Camera);if(CreateIngestRequest().AudioDeviceId is not null)throw new InvalidOperationException("Wybór audio przedostał się do kamery.");
        HideWorkspace();if(AudioTrack.ItemsSource is not null||selectedAudioFiles is not null||Source.Text.Length!=0)throw new InvalidOperationException("Blokada nie czyści źródła audio.");
        return ["Audio dropdown requires a configured source and freezes its ID and selected files","Changing source clears file selection; camera ingest ignores audio selection; lock hides inputs","Mouse back and forward navigate between sections and embedded configuration","Full workspace configuration and left return button","Public shooting day survives tab lock","Severity, operation, tab, source search and date filters","Chronological sort survives live append","Alert statuses replace read-only checkboxes","Single overview viewport scrolls","Private activity data cleared on lock",$"Bottom tooltips for {buttons} application and wizard buttons",$"History fixture contains {total} events"];
    }
}
