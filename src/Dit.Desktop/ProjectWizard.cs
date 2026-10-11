using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dit.Core;
using Microsoft.Win32;

namespace Dit.Desktop;

public sealed class ProjectWizard : Window
{
    public CreateProjectRequest? Request {get;private set;}
    public Func<CreateProjectRequest,Task>? SubmitAsync {get;set;}
    private readonly ProjectConfig? original;
    private bool embedded;
    public event Action? Saved;
    public event Action? Cancelled;
    private readonly Dictionary<string,PasswordBox> passwordEditors=new();
    private readonly HashSet<string> changedPasswords=new();
    public FrameworkElement Embed()
    {
        embedded=true;cancelButton.IsCancel=false;var form=(FrameworkElement)Content;Content=null;form.Margin=new Thickness(0);return form;
    }
    private Window DialogOwner()=>embedded?Application.Current.MainWindow:this;
    private void ExistingPassword(StackPanel parent,string userId,string label)
    {
        var box=new PasswordBox {ToolTip="Nowe hasło: "+label+". Nieuzupełnione pole zachowuje obecne hasło. Wpisanie, a następnie wyczyszczenie usuwa hasło."};
        var row=new Grid();row.Children.Add(box);var hint=new TextBlock{Text="Bez zmiany hasła",Margin=new Thickness(12,0,12,0),VerticalAlignment=VerticalAlignment.Center,Foreground=(System.Windows.Media.Brush)FindResource("MutedBrush"),IsHitTestVisible=false};row.Children.Add(hint);
        box.PasswordChanged+=(_,_)=>{changedPasswords.Add(userId);hint.Text="Bez hasła";hint.Visibility=box.Password.Length==0?Visibility.Visible:Visibility.Collapsed;};
        parent.Children.Add(row);passwordEditors[userId]=box;
    }
    private readonly string projectId;
    private readonly TabControl tabs=new();
    private readonly Button saveButton=new(),cancelButton=new();
    private readonly Border validationSummary=new();
    private readonly TextBlock validationTitle=new(),validationMessage=new();
    private readonly Dictionary<ProjectField,(Control Input,TextBlock Hint,int Page)> fields=new();
    private readonly Dictionary<ProjectField,Control> inputs;
    private int currentPage;
    private bool validationShown,isSaving;
    private readonly TextBox name=new(),main=new(),backup=new(),proxy=new(),premiere=new(),timezone=new(),cameras=new(),audio=new(),template=new(),preset=new(),sequencePreset=new();
    private readonly PasswordBox adminPassword=new(),audioPassword=new();
    private readonly CheckBox compress=new() {Content="Kompresuj backup bezstratnie (7z/LZMA2)"};
    private readonly CheckBox clear=new() {Content="Czyść kartę automatycznie po weryfikacji obu kopii"};
    private sealed record CameraForm(TextBox Name,PasswordBox Password,TabProfile? Previous);
    private sealed record AudioForm(TextBox Name,AudioSource? Previous);
    private readonly List<CameraForm> cameraForms=new();
    private readonly List<AudioForm> audioForms=new();
    private readonly StackPanel cameraCards=new(),audioCards=new();
    public ProjectWizard(ProjectConfig? config=null)
    {
        if(config is not null)config=LegacyAudioDevices.Upgrade(config);
        SetResourceReference(StyleProperty,"AppWindowStyle");
        projectId=config?.Id??Guid.NewGuid().ToString("N");
        inputs=new(){{ProjectField.Name,name},{ProjectField.Main,main},{ProjectField.Backup,backup},{ProjectField.Proxy,proxy},{ProjectField.Premiere,premiere},{ProjectField.TimeZone,timezone},{ProjectField.Cameras,cameras},{ProjectField.Audio,audio},{ProjectField.AdminPassword,adminPassword},{ProjectField.AudioPassword,audioPassword},{ProjectField.Template,template},{ProjectField.SequencePreset,sequencePreset},{ProjectField.ProxyPreset,preset}};
        original=config;Title=config is null?"Nowy projekt — konfiguracja":"Konfiguracja projektu";Width=900;Height=860;MinWidth=780;MinHeight=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var outer=new DockPanel {Margin=new Thickness(28)};var footer=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        cancelButton.Content="Anuluj";cancelButton.Style=(Style)FindResource("QuietButton");cancelButton.IsCancel=true;cancelButton.Click+=(_,_)=>{if(embedded)Cancelled?.Invoke();else DialogResult=false;};
        saveButton.Content=config is null?"Utwórz projekt":"Zapisz konfigurację";saveButton.Style=(Style)FindResource("PrimaryButton");saveButton.Click+=Save;footer.Children.Add(cancelButton);footer.Children.Add(saveButton);DockPanel.SetDock(footer,Dock.Bottom);outer.Children.Add(footer);
        var heading=new StackPanel {Margin=new Thickness(0,0,0,24)};heading.Children.Add(new TextBlock {Text=config is null?"Nowa produkcja":"Ustawienia produkcji",FontSize=28,FontWeight=FontWeights.SemiBold});heading.Children.Add(new TextBlock {Text="Projekt, magazyny i zespół — przygotuj przestrzeń do pracy.",Margin=new Thickness(0,8,0,0),Foreground=(System.Windows.Media.Brush)FindResource("MutedBrush")});DockPanel.SetDock(heading,Dock.Top);outer.Children.Add(heading);
        var messages=new StackPanel();validationTitle.FontWeight=FontWeights.SemiBold;validationTitle.Foreground=ErrorBrush;validationMessage.Foreground=ErrorBrush;validationMessage.TextWrapping=TextWrapping.Wrap;validationMessage.Margin=new Thickness(0,6,0,0);messages.Children.Add(validationTitle);messages.Children.Add(new ScrollViewer {MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=validationMessage});
        validationSummary.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45,32,39));validationSummary.BorderBrush=ErrorBrush;validationSummary.BorderThickness=new Thickness(1);validationSummary.CornerRadius=new CornerRadius(7);validationSummary.Padding=new Thickness(14);validationSummary.Margin=new Thickness(0,0,0,16);validationSummary.Child=messages;validationSummary.Visibility=Visibility.Collapsed;DockPanel.SetDock(validationSummary,Dock.Top);outer.Children.Add(validationSummary);
        tabs.Background=System.Windows.Media.Brushes.Transparent;tabs.BorderThickness=new Thickness(0);outer.Children.Add(tabs);Content=outer;
        Closing+=(_,e)=>{if(isSaving)e.Cancel=true;};
        var storage=Page(tabs,"Projekt i magazyny");var identity=Pair(storage);Field(identity.Left,"Nazwa projektu",name);Field(identity.Right,"Strefa czasowa",timezone);
        Field(storage,"Magazyn główny — jedna ścieżka w wierszu, kolejność zapełniania",main,true,true);
        Field(storage,"Backup — jedna ścieżka w wierszu, osobne fizyczne dyski",backup,true,true);
        var output=Pair(storage);Field(output.Left,"Folder proxy",proxy,false,true);Field(output.Right,"Folder projektów Premiere",premiere,false,true);
        storage.Children.Add(compress);clear.Margin=new Thickness(0,16,0,8);storage.Children.Add(clear);storage.Children.Add(new TextBlock {Text="Czyszczenie działa wyłącznie po weryfikacji SHA-256, potwierdzeniu niezależności nośników i kwalifikacji profilu formatu. Domyślnie wyłączone.",TextWrapping=TextWrapping.Wrap,Opacity=.7});
        currentPage=1;var accounts=Page(tabs,"Zakładki i audio");
        accounts.Children.Add(new TextBlock {Text="Zakładki kamer i operatorów",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,12)});
        accounts.Children.Add(cameraCards);
        var addCamera=new Button {Content="＋ Dodaj zakładkę",HorizontalAlignment=HorizontalAlignment.Left};addCamera.Click+=(_,_)=>AddCamera();accounts.Children.Add(addCamera);RegisterField(accounts,cameras);
        if(config is null)Secret(storage,"Hasło administratora (opcjonalne)",adminPassword);
        accounts.Children.Add(new TextBlock {Text="Audio — urządzenia / mikroporty",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,18,0,8)});
        accounts.Children.Add(new TextBlock {Text="Jedna zakładka Audio dla projektu. Każde źródło poniżej to osobne urządzenie, folder i ścieżka w Premiere.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        if(config is null)Secret(accounts,"Hasło zakładki Audio (opcjonalne)",audioPassword);
        else {accounts.Children.Add(new TextBlock{Text="Hasło zakładki Audio",Margin=new Thickness(0,0,0,6)});ExistingPassword(accounts,config.Tabs.Single(t=>t.Kind==TabKind.Audio).UserId,"zakładka Audio");}
        audioCards.Margin=new Thickness(0,12,0,0);accounts.Children.Add(audioCards);
        var addAudio=new Button {Content="＋ Dodaj źródło audio",HorizontalAlignment=HorizontalAlignment.Left};addAudio.Click+=(_,_)=>AddAudio();accounts.Children.Add(addAudio);RegisterField(accounts,audio);
        currentPage=2;var adobe=Page(tabs,"Premiere i profile");adobe.Children.Add(new TextBlock {Text="Timeline: UHD 3840 × 2160 / 50 fps\nProxy: ProRes Proxy MOV, do 1280 × 720, oryginalny FPS i kanały",FontSize=18,Margin=new Thickness(0,0,0,20)});
        adobe.Children.Add(new TextBlock {Text="Jeden projekt Premiere dla całej produkcji. Każdy dzień zdjęciowy ma osobną sekwencję: Dzień 1, Dzień 2…"+(config is null?"":"\nPlik projektu: "+PremiereProjects.PathFor(config)+"\nŚcieżka tego projektu pozostaje stała po zmianie nazwy produkcji lub folderu w konfiguracji."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
        Field(adobe,"Opcjonalny projekt wzorcowy .prproj",template,false,false,true);Field(adobe,"Sprawdzony preset sekwencji .sqpreset",sequencePreset,false,false,true);ProxyPresetField(adobe);
        adobe.Children.Add(new TextBlock {Text="Kwalifikacja Adobe obejmuje zapis i ponowne otwarcie projektu oraz kontrolę kanałów audio. Profile zegarów, numeracji i kwalifikacji formatów są dostępne w ustawieniach zaawansowanych.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,20,0,16)});
        if(config is not null)
        {
            var advanced=new Button {Content="Profile urządzeń — ustawienia zaawansowane"};advanced.Click+=(_,_)=>EditProfiles();adobe.Children.Add(advanced);
        }
        name.Text=config?.Name??"";timezone.Text=config?.TimeZone??"Europe/Warsaw";main.Text=string.Join(Environment.NewLine,config?.MainRoots??[]);backup.Text=string.Join(Environment.NewLine,config?.BackupRoots??[]);proxy.Text=config?.ProxyRoot??"";premiere.Text=config?.PremiereRoot??"";
        compress.IsChecked=config?.CompressBackup??false;clear.IsChecked=config?.AutoClear??false;template.Text=config?.PremiereTemplate??"";SetProxyPreset(config?.ProxyPreset);sequencePreset.Text=config?.SequencePreset??"";
        devices=config?.Devices??[];
        if(config is null){AddCamera();AddAudio();}else
        {
            foreach(var t in config.Tabs.Where(t=>t.Kind==TabKind.Camera))AddCamera(t);
            foreach(var source in config.AudioSources)AddAudio(source);
        }
    }
    private Border Card(StackPanel content)=>new() {Child=content,Background=(System.Windows.Media.Brush)FindResource("Surface"),BorderBrush=(System.Windows.Media.Brush)FindResource("LineBrush"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12),Margin=new Thickness(0,0,0,8)};
    private static void RemoveInRow(StackPanel panel,Action remove,bool narrowRight=false)
    {
        var grid=panel.Children.OfType<Grid>().Last();if(narrowRight)grid.ColumnDefinitions[2].Width=new GridLength(150);
        grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var button=new Button{Content="Usuń",Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Bottom,ToolTip="Usuń tę sekcję z konfiguracji. Wcześniejsze nagrania pozostają zachowane."};Grid.SetColumn(button,3);grid.Children.Add(button);button.Click+=(_,_)=>remove();
    }
    private void SyncForms()
    {
        cameras.Text=string.Join(Environment.NewLine,cameraForms.Select(f=>f.Name.Text));audio.Text=string.Join(Environment.NewLine,audioForms.Select(f=>f.Name.Text+" | "+(f.Previous?.Channels??1)));
        if(cameraForms.Count>0&&fields.TryGetValue(ProjectField.Cameras,out var camera))fields[ProjectField.Cameras]=(cameraForms[0].Name,camera.Hint,camera.Page);
        if(audioForms.Count>0&&fields.TryGetValue(ProjectField.Audio,out var sound))fields[ProjectField.Audio]=(audioForms[0].Name,sound.Hint,sound.Page);
        Revalidate();
    }
    private void AddCamera(TabProfile? previous=null)
    {
        var form=new CameraForm(new TextBox {Text=previous?.Name??"Kamera "+(cameraForms.Count+1)},new PasswordBox(),previous);cameraForms.Add(form);
        var panel=new StackPanel();var pair=Pair(panel);pair.Left.Children.Add(new TextBlock {Text="Nazwa zakładki / operatora",Margin=new Thickness(0,0,0,6)});pair.Left.Children.Add(form.Name);
        pair.Right.Children.Add(new TextBlock {Text=previous is null?"Hasło (opcjonalne)":"Nowe hasło",Margin=new Thickness(0,0,0,6)});
        if(previous is null)pair.Right.Children.Add(form.Password);else ExistingPassword(pair.Right,previous.UserId,previous.Name);
        var card=Card(panel);RemoveInRow(panel,()=>{cameraForms.Remove(form);cameraCards.Children.Remove(card);SyncForms();});cameraCards.Children.Add(card);form.Name.TextChanged+=(_,_)=>SyncForms();form.Password.PasswordChanged+=(_,_)=>SyncForms();SyncForms();
    }
    private void AddAudio(AudioSource? previous=null)
    {
        var used=audioForms.Select(f=>f.Name.Text).Concat(devices.Select(d=>d.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);var number=1;while(used.Contains("Mikroport "+number))number++;
        var form=new AudioForm(new TextBox {Text=previous is null?"Mikroport "+number:original!.Devices.Single(d=>d.Id==previous.DeviceId).Name},previous);audioForms.Add(form);
        var panel=new StackPanel();panel.Children.Add(new TextBlock {Text="Nazwa urządzenia / mikroportu (folder i ścieżka Premiere)",Margin=new Thickness(0,0,0,6)});
        var row=new DockPanel();var remove=new Button {Content="Usuń",Margin=new Thickness(12,0,0,0),ToolTip="Usuń źródło z konfiguracji. Wcześniejsze nagrania pozostają zachowane."};DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);row.Children.Add(form.Name);panel.Children.Add(row);
        var card=Card(panel);remove.Click+=(_,_)=>{audioForms.Remove(form);audioCards.Children.Remove(card);SyncForms();};audioCards.Children.Add(card);form.Name.TextChanged+=(_,_)=>SyncForms();SyncForms();
    }
    private DeviceProfile[] devices;
    private void EditProfiles()
    {
        var text=new TextBox {Text=System.Text.Json.JsonSerializer.Serialize(devices,ApiClient.Json),AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new System.Windows.Media.FontFamily("Consolas")};
        var window=new Window {Owner=DialogOwner(),Title="Profile urządzeń: wzorce, korekta zegara i kwalifikacja",Width=800,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};var panel=new DockPanel {Margin=new Thickness(20)};var button=new Button {Content="Zapisz profile",Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(button,Dock.Bottom);panel.Children.Add(button);panel.Children.Add(text);window.Content=panel;
        button.Click+=(_,_)=> {try{devices=System.Text.Json.JsonSerializer.Deserialize<DeviceProfile[]>(text.Text,ApiClient.Json)??[];window.DialogResult=true;}catch(Exception ex){System.Windows.MessageBox.Show(window,ex.Message);}};window.ShowDialog();
    }
    private static StackPanel Page(TabControl tabs,string label)
    {var panel=new StackPanel {Margin=new Thickness(20)};tabs.Items.Add(new TabItem {Header=label,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
    private static (StackPanel Left,StackPanel Right) Pair(StackPanel parent)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(18)});grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new StackPanel();var right=new StackPanel();Grid.SetColumn(right,2);grid.Children.Add(left);grid.Children.Add(right);parent.Children.Add(grid);return (left,right);
    }
    private void Secret(StackPanel panel,string label,PasswordBox box){panel.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,16,0,6)});panel.Children.Add(box);RegisterField(panel,box);box.PasswordChanged+=(_,_)=>Revalidate();}
    private void Field(StackPanel panel,string label,TextBox input,bool multi=false,bool folder=false,bool file=false)
    {
        panel.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,16,0,6),TextWrapping=TextWrapping.Wrap});
        var row=new DockPanel();if(folder||file)
        {
            var browse=new Button {Content="…",Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);
            browse.Click+=(_,_)=> {if(folder){var picker=new OpenFolderDialog();if(picker.ShowDialog()==true)input.Text=multi?(input.Text.Trim().Length==0?picker.FolderName:input.Text.Trim()+Environment.NewLine+picker.FolderName):picker.FolderName;}else{var picker=new OpenFileDialog();if(picker.ShowDialog()==true)input.Text=picker.FileName;}};
        }
        input.AcceptsReturn=multi;if(multi){input.Height=110;input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;}row.Children.Add(input);panel.Children.Add(row);RegisterField(panel,input);input.TextChanged+=(_,_)=>Revalidate();
    }
    private static System.Windows.Media.Brush ErrorBrush=>new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244,174,168));
    private void RegisterField(StackPanel parent,Control input)
    {
        var key=inputs.Single(p=>ReferenceEquals(p.Value,input)).Key;
        var hint=new TextBlock {Foreground=ErrorBrush,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),Visibility=Visibility.Collapsed};parent.Children.Add(hint);fields.Add(key,(input,hint,currentPage));
    }
    private string SelectedProxyPreset()=>preset.Text==BundledPresets.Proxy720pLabel?BundledPresets.Proxy720p:preset.Text;
    private void SetProxyPreset(string? value)=>preset.Text=string.IsNullOrWhiteSpace(value)||BundledPresets.IsDefault(value)?BundledPresets.Proxy720pLabel:value;
    private void ProxyPresetField(StackPanel panel)
    {
        panel.Children.Add(new TextBlock {Text="Preset proxy AME",Margin=new Thickness(0,16,0,6)});
        var row=new DockPanel();
        var browse=new Button {Content="Wybierz więcej",Margin=new Thickness(8,0,0,0),ToolTip="Wybierz własny preset Adobe Media Encoder (.epr) zamiast domyślnego DIT Proxy 720p."};
        var reset=new Button {Content="Użyj domyślnego",Margin=new Thickness(8,0,0,0),ToolTip="Przywróć preset DIT Proxy 720p dołączony do programu."};
        DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);DockPanel.SetDock(reset,Dock.Right);row.Children.Add(reset);
        preset.IsReadOnly=true;preset.ToolTip="Domyślny preset jest częścią programu i nie wymaga wskazywania pliku.";row.Children.Add(preset);panel.Children.Add(row);RegisterField(panel,preset);
        browse.Click+=(_,_)=>{var picker=new OpenFileDialog {Title="Wybierz preset proxy AME",Filter="Presety Adobe Media Encoder (*.epr)|*.epr",CheckFileExists=true};if(picker.ShowDialog(DialogOwner())==true)SetProxyPreset(picker.FileName);};
        reset.Click+=(_,_)=>SetProxyPreset(BundledPresets.Proxy720p);
        preset.TextChanged+=(_,_)=>{reset.Visibility=BundledPresets.IsDefault(SelectedProxyPreset())?Visibility.Collapsed:Visibility.Visible;Revalidate();};
    }
    private ProjectFormInput Form()=>new(name.Text,main.Text,backup.Text,proxy.Text,premiere.Text,timezone.Text,cameras.Text,audio.Text,adminPassword.Password,audioPassword.Password,original is not null,original?.Tabs.Count(t=>t.Kind==TabKind.Camera)??0,template.Text,sequencePreset.Text,SelectedProxyPreset());
    private ProjectFieldIssue[] ValidateForm()=>ProjectFormValidation.Validate(Form()).Concat(cameraForms.Where(f=>string.IsNullOrWhiteSpace(f.Name.Text)).Select(_=>new ProjectFieldIssue(ProjectField.Cameras,"Uzupełnij nazwę każdej zakładki lub usuń pustą sekcję."))).ToArray();
    private void Revalidate(){if(validationShown&&!isSaving)ShowIssues(ValidateForm(),false);}
    private void ShowIssues(ProjectFieldIssue[] issues,bool focus)
    {
        foreach(var (key,field) in fields)
        {
            var messages=issues.Where(i=>i.Field==key).Select(i=>i.Message).Distinct().ToArray();
            field.Hint.Text=string.Join(Environment.NewLine,messages);field.Hint.Visibility=messages.Length==0?Visibility.Collapsed:Visibility.Visible;
            if(messages.Length>0)field.Input.BorderBrush=ErrorBrush;else field.Input.ClearValue(Control.BorderBrushProperty);
        }
        var general=issues.Where(i=>i.Field==ProjectField.General).Select(i=>i.Message).ToArray();
        validationSummary.Visibility=issues.Length==0?Visibility.Collapsed:Visibility.Visible;
        validationTitle.Text=general.Length>0?"Nie zapisano projektu":"Uzupełnij zaznaczone pola";
        validationMessage.Text=general.Length>0?string.Join(Environment.NewLine,general)+"\nWpisane ustawienia zostały zachowane. Możesz poprawić dane i ponowić zapis.":$"Pola wymagające poprawy: {issues.Select(i=>i.Field).Distinct().Count()}. Wpisane ustawienia pozostają w formularzu.";
        var labels=new[]{"Projekt i magazyny","Zakładki i audio","Premiere i profile"};
        for(var i=0;i<labels.Length;i++)
        {
            var count=issues.Where(x=>fields.TryGetValue(x.Field,out var f)&&f.Page==i).Select(x=>x.Field).Distinct().Count();
            var page=(TabItem)tabs.Items[i];page.Header=count==0?labels[i]:new TextBlock {Text=$"{labels[i]} · {count}",Foreground=ErrorBrush};
        }
        if(focus&&issues.FirstOrDefault(i=>fields.ContainsKey(i.Field)) is {} first)
        {
            var field=fields[first.Field];tabs.SelectedIndex=field.Page;
            Dispatcher.BeginInvoke(new Action(()=>{field.Input.BringIntoView();field.Input.Focus();}));
        }
    }
    private static string[] Lines(TextBox box)=>box.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private static string? Optional(string text)=>string.IsNullOrWhiteSpace(text)?null:text.Trim();
    private async void Save(object sender,RoutedEventArgs e)
    {
        if(await TrySaveAsync()){if(embedded)Saved?.Invoke();else DialogResult=true;}
    }
    internal async Task<bool> TrySaveAsync()
    {
        if(isSaving)return false;
        validationShown=true;var issues=ValidateForm();ShowIssues(issues,true);if(issues.Length>0)return false;
        try
        {
            var request=BuildRequest();
            isSaving=true;saveButton.IsEnabled=false;cancelButton.IsEnabled=false;tabs.IsEnabled=false;saveButton.Content="Zapisywanie…";
            if(SubmitAsync is null)throw new InvalidOperationException("Brak połączenia z zapisem projektu.");
            await SubmitAsync(request);Request=request;return true;
        }
        catch(Exception ex)
        {
            var message=ex.Message;
            if(ex is ApiException)try{using var json=System.Text.Json.JsonDocument.Parse(message);if(json.RootElement.TryGetProperty("error",out var error))message=error.GetString()??message;}catch(System.Text.Json.JsonException){}
            ShowIssues([new(ProjectField.General,message)],false);return false;
        }
        finally{isSaving=false;saveButton.IsEnabled=true;cancelButton.IsEnabled=true;tabs.IsEnabled=true;saveButton.Content=original is null?"Utwórz projekt":"Zapisz konfigurację";}
    }
    private CreateProjectRequest BuildRequest()
    {
            var id=projectId;List<TabProfile> tabs=[];List<DeviceProfile> resultDevices=[];List<NewUser> users=[];List<AudioSource> sources=[];
            tabs.Add(original?.Tabs.Single(t=>t.Kind==TabKind.Admin)??new("admin","Admin",TabKind.Admin,"admin",null));
            if(original is null)users.Add(new("admin","Administrator",adminPassword.Password,true));
            var cameraLines=Lines(cameras);
            for(var i=0;i<cameraLines.Length;i++)
            {
                var values=cameraLines[i].Split('|',StringSplitOptions.TrimEntries);var previousTab=cameraForms[i].Previous;var deviceId=previousTab?.DeviceId??(original is null?"kamera"+(i+1):"kamera-"+Guid.NewGuid().ToString("N")[..8]);var userId=previousTab?.UserId??deviceId;
                var previous=devices.SingleOrDefault(d=>d.Id==deviceId);resultDevices.Add(previous is null?new(deviceId,values[0],false):previous with {Name=values[0]});tabs.Add(new(deviceId,values[0],TabKind.Camera,userId,deviceId));
                if(previousTab is null)users.Add(new(userId,values[0],cameraForms[i].Password.Password,false));
            }
            tabs.Add(original?.Tabs.Single(t=>t.Kind==TabKind.Audio)??new("audio","Audio",TabKind.Audio,"audio",null));if(original is null)users.Add(new("audio","Audio",audioPassword.Password,false));
            var audioLines=Lines(audio);
            for(var i=0;i<audioLines.Length;i++)
            {
                var values=ProjectFormValidation.AudioParts(audioLines[i]);if(values.Length!=2||!int.TryParse(values[1],out var channels)||channels<1)throw new ArgumentException("Źródło audio: nazwa | liczba kanałów, np. Audio | 20.");
                var oldSource=audioForms[i].Previous;var deviceId=oldSource?.DeviceId??(original is null?"audio"+(i+1):"audio-"+Guid.NewGuid().ToString("N")[..8]);var previous=devices.SingleOrDefault(d=>d.Id==deviceId);resultDevices.Add(previous is null?new(deviceId,values[0],true):previous with {Name=values[0]});sources.Add(new(deviceId,oldSource?.Pattern??"",channels,Enumerable.Range(1,channels).Select(c=>oldSource is not null&&c<=oldSource.ChannelNames.Length?oldSource.ChannelNames[c-1]:"Kanał "+c).ToArray()));
            }
            foreach(var retired in devices.Where(d=>resultDevices.All(x=>x.Id!=d.Id)))resultDevices.Add(retired);
            var config=new ProjectConfig(id,name.Text.Trim(),Lines(main),Lines(backup),proxy.Text.Trim(),premiere.Text.Trim(),tabs.ToArray(),resultDevices.ToArray(),sources.ToArray(),compress.IsChecked==true,clear.IsChecked==true,timezone.Text.Trim(),Optional(template.Text),Optional(sequencePreset.Text),SelectedProxyPreset());
            Configuration.Validate(config);return new(config,users.ToArray(),passwordEditors.Where(p=>changedPasswords.Contains(p.Key)&&tabs.Any(t=>t.UserId==p.Key)).ToDictionary(p=>p.Key,p=>p.Value.Password));
    }
    internal void PrepareValidationPreview()
    {
        name.Text="Plan filmowy — październik";main.Text=@"E:\Produkcja";proxy.Text=@"F:\Proxy";premiere.Text=@"F:\Premiere";
        cameraForms[0].Name.Text="Kamera 1";audioForms[0].Name.Text="Mikroport 1";SyncForms();
        validationShown=true;ShowIssues(ProjectFormValidation.Validate(Form()),false);
    }
    internal void PrepareAccountsPreview(){tabs.SelectedIndex=1;AddCamera();AddCamera();}
    internal void PrepareProxyPreview(){tabs.SelectedIndex=2;}
    internal static async Task<string[]> RunValidationSmoke()
    {
        List<string> passed=[];
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);passed.Add(label);}
        var scope="DIT-instance-smoke-"+Guid.NewGuid().ToString("N");
        using(var first=new DesktopInstance(scope))using(var second=new DesktopInstance(scope))Check(first.IsPrimary&&!second.IsPrimary,"Only one desktop instance owns a workspace");
        using(var next=new DesktopInstance(scope))Check(next.IsPrimary,"Closing the instance releases the workspace");
        var wizard=new ProjectWizard();wizard.PrepareValidationPreview();var calls=0;
        wizard.SubmitAsync=_=>{calls++;return Task.CompletedTask;};
        Check(!await wizard.TrySaveAsync()&&calls==0,"Missing fields prevent submission");
        Check(wizard.name.Text=="Plan filmowy — październik"&&wizard.main.Text==@"E:\Produkcja"&&wizard.DialogResult is null,"Validation keeps the form and its values");
        Check(wizard.fields[ProjectField.Backup].Hint.Visibility==Visibility.Visible&&wizard.tabs.SelectedIndex==0,"Missing field is marked and its page selected");
        wizard.backup.Text=@"G:\Produkcja";wizard.adminPassword.Password="1";wizard.audioPassword.Password="2";
        Check(wizard.validationSummary.Visibility==Visibility.Collapsed,"Correcting fields clears validation");
        string? attemptedId=null;
        wizard.SubmitAsync=request=>{attemptedId=request.Config.Id;throw new ApiException(400,"{\"error\":\"Nie można zapisać konfiguracji testowej.\"}");};
        Check(!await wizard.TrySaveAsync()&&wizard.Request is null&&wizard.DialogResult is null,"Worker rejection keeps the wizard open");
        Check(wizard.name.Text=="Plan filmowy — październik"&&wizard.backup.Text==@"G:\Produkcja"&&wizard.adminPassword.Password=="1","Worker rejection preserves configuration and passwords");
        Check(wizard.saveButton.IsEnabled&&wizard.cancelButton.IsEnabled&&wizard.tabs.IsEnabled&&wizard.validationMessage.Text.Contains("Nie można zapisać"),"Failed save can be corrected and retried");
        var completion=new TaskCompletionSource();calls=0;
        wizard.SubmitAsync=_=>{calls++;return completion.Task;};
        var saving=wizard.TrySaveAsync();
        Check(!await wizard.TrySaveAsync()&&calls==1&&!wizard.saveButton.IsEnabled,"Double submission is blocked");
        completion.SetResult();
        Check(await saving&&wizard.Request?.Config.Id==attemptedId&&wizard.DialogResult is null,"Retry succeeds with the same project identity");
        Check(wizard.saveButton.IsEnabled&&wizard.tabs.IsEnabled,"Save releases busy state");
        Check(wizard.Request!.Users.Single(u=>u.Id=="kamera1").Password==""&&wizard.Request.Config.AudioSources.Single() is {Pattern:"",Channels:1},"Optional passwords and a named microport are submitted");
        Check(wizard.Request.Config.ProxyPreset==BundledPresets.Proxy720p&&System.IO.File.Exists(BundledPresets.ResolveProxy(wizard.Request.Config.ProxyPreset)),"New production uses the preset shipped with the program");
        wizard.SetProxyPreset(@"E:\Custom\Other.epr");Check(wizard.BuildRequest().Config.ProxyPreset==@"E:\Custom\Other.epr"&&!wizard.ValidateForm().Any(i=>i.Field==ProjectField.ProxyPreset),"Custom proxy preset replaces the default and is submitted");
        wizard.SetProxyPreset(BundledPresets.Proxy720p);Check(new ProjectWizard(wizard.BuildRequest().Config).BuildRequest().Config.ProxyPreset==BundledPresets.Proxy720p,"Default preset survives saving and reopening configuration");
        wizard.AddCamera();wizard.cameraForms[1].Name.Text="Operator drugi";wizard.cameraForms[1].Password.Password="a|b";var structured=wizard.BuildRequest();
        Check(structured.Config.Tabs.Count(t=>t.Kind==TabKind.Camera)==2&&structured.Users.Single(u=>u.Id=="kamera2").Password=="a|b","Plus adds an independent camera form and accepts arbitrary password characters");
        wizard.cameraForms[1].Name.Clear();Check(wizard.ValidateForm().Any(i=>i.Field==ProjectField.Cameras),"Empty camera section is validated without losing other sections");wizard.cameraForms[1].Name.Text="Operator drugi";
        var editing=new ProjectWizard(wizard.BuildRequest().Config);editing.AddCamera();editing.cameraForms[2].Name.Text="Operator trzeci";var middle=editing.cameraForms[1];editing.cameraForms.Remove(middle);editing.SyncForms();var edited=editing.BuildRequest();
        Check(edited.Config.Tabs.Single(t=>t.Name=="Kamera 1").Id=="kamera1"&&edited.Config.Tabs.All(t=>t.Name!="Operator drugi"),"Removing a section preserves existing account identity");
        editing.AddAudio();editing.audioForms[1].Name.Text="Mikroport Jan";var audioConfig=editing.BuildRequest().Config;
        Check(audioConfig.AudioSources.Length==2&&audioConfig.AudioSources.All(s=>s.Channels==1)&&audioConfig.Devices.Any(d=>d.Audio&&d.Name=="Mikroport Jan")&&audioConfig.Tabs.Count(t=>t.Kind==TabKind.Audio)==1,"Named devices share one Audio tab and produce independent tracks");
        var legacy=audioConfig with {AudioSources=[new("legacy-audio","",20,[])],Devices=[..audioConfig.Devices,new("legacy-audio","Audio",true)]};
        var migrated=new ProjectWizard(legacy);var migration=migrated.BuildRequest().Config;
        Check(migration.AudioSources.Length==20&&migration.AudioSources.All(s=>s.Channels==1)&&migration.Tabs.Count(t=>t.Kind==TabKind.Audio)==1&&migration.Devices.Any(d=>d.Id=="legacy-audio"),"Old generic Audio count becomes editable microports while historical device is preserved");
        var roundTrip=new ProjectWizard(migration).BuildRequest().Config;
        Check(roundTrip.AudioSources.Select(s=>s.DeviceId).SequenceEqual(migration.AudioSources.Select(s=>s.DeviceId)),"Repeated configuration saves keep individual source identities");
        Check(editing.BuildRequest().PasswordChanges!.Count==0&&!editing.passwordEditors.ContainsKey("admin"),"Untouched password inputs preserve passwords; administrator duplicate field is absent");
        editing.passwordEditors["kamera1"].Password="new";editing.passwordEditors["audio"].Password="audio-new";
        Check(editing.BuildRequest().PasswordChanges!["kamera1"]=="new"&&editing.BuildRequest().PasswordChanges!["audio"]=="audio-new","Simple password inputs update camera and Audio without checkboxes");
        editing.passwordEditors["kamera1"].Password="";
        Check(editing.BuildRequest().PasswordChanges!.ContainsKey("kamera1")&&editing.BuildRequest().PasswordChanges!["kamera1"]=="","Clearing an edited password accepts an empty password");
        Check(editing.cameraCards.Children.OfType<Border>().All(c=>((StackPanel)c.Child).Children.Count==1),"Compact camera cards keep remove action on the input row");
        var embedded=editing.Embed();Check(editing.Content is null&&embedded is not null,"Configuration detaches into the full workspace without a modal window");
        return passed.ToArray();
    }
}
