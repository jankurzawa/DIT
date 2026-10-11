namespace Dit.Desktop;
public partial class App : System.Windows.Application
{
    private DesktopInstance? instance;
    public App()
    {
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),System.Windows.FrameworkElement.LoadedEvent,
            new System.Windows.RoutedEventHandler((sender,_)=>WindowTheme.Apply((System.Windows.Window)sender)));
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button),System.Windows.FrameworkElement.LoadedEvent,
            new System.Windows.RoutedEventHandler((sender,_)=>Tooltips.Apply((System.Windows.Controls.Button)sender)));
    }
    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length>=2&&e.Args[0]=="--check-recordings")
        {
            ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
            try{var checks=await new MainWindow(false).RunRecordingsSmoke();System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new {passed=checks},ApiClient.Json));Shutdown(0);}
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}return;
        }
        if(e.Args.Length>=2&&e.Args[0]=="--check-audio-ui")
        {
            ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
            try{var checks=await new MainWindow(false).RunAudioSmoke();System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new {passed=checks},ApiClient.Json));Shutdown(0);}
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}return;
        }
        if(e.Args.Length>=2&&e.Args[0]=="--create-icon"){BrandIcon.Write(e.Args[1]);Shutdown(0);return;}
        if(e.Args.Length>=3&&e.Args[0]=="--check-playback")
        {
            ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
            try{var checks=await new MainWindow(false).RunPlaybackSmoke(e.Args[2],e.Args[1]);System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new {passed=checks},ApiClient.Json));Shutdown(0);}
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length>=2&&e.Args[0]=="--check-activity")
        {
            try{var checks=new MainWindow().RunActivitySmoke();System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new {passed=checks},ApiClient.Json));Shutdown(0);}
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length>=2&&e.Args[0]=="--check-startup")
        {
            ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
            try{var checks=await new MainWindow().RunStartupSmoke();System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new {passed=checks},ApiClient.Json));Shutdown(0);}
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length>=2&&e.Args[0]=="--check-project-wizard")
        {
            try{var checks=await ProjectWizard.RunValidationSmoke();System.IO.File.WriteAllText(e.Args[1],System.Text.Json.JsonSerializer.Serialize(new {passed=checks},ApiClient.Json));Shutdown(0);}
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        var window=new MainWindow();
        if(e.Args.Length>=2&&e.Args[0]=="--render-preview")
        {
            try
            {
                window.RunPrivacySmoke();
                var scenario=e.Args.Length>2?e.Args[2]:"welcome";
                System.Windows.Window preview=window;
                if(scenario=="day-picker")preview=new DayTimeDialog("Początek dnia zdjęciowego","Europe/Warsaw");else if(scenario.StartsWith("wizard",System.StringComparison.Ordinal)){var wizard=new ProjectWizard();if(scenario=="wizard-errors")wizard.PrepareValidationPreview();if(scenario=="wizard-accounts")wizard.PrepareAccountsPreview();if(scenario=="wizard-proxy")wizard.PrepareProxyPreview();preview=wizard;}else window.PrepareDesignPreview(scenario);
                var width=e.Args.Length>3?int.Parse(e.Args[3]):scenario.StartsWith("wizard",System.StringComparison.Ordinal)?900:1440;
                var height=e.Args.Length>4?int.Parse(e.Args[4]):920;
                var visual=(System.Windows.FrameworkElement)preview.Content;
                visual.Width=double.NaN;visual.Height=double.NaN;visual.Measure(new System.Windows.Size(width,height));visual.Arrange(new System.Windows.Rect(0,0,width,height));visual.UpdateLayout();
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new System.Action(()=>{}));visual.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                var background=new System.Windows.Media.DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(preview.Background,null,new System.Windows.Rect(0,0,width,height));bitmap.Render(background);bitmap.Render(visual);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file=System.IO.File.Create(e.Args[1]);encoder.Save(file);Shutdown(0);
            }
            catch(System.Exception ex){System.IO.File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        instance=new DesktopInstance();if(!instance.Attach(window)){Shutdown(0);return;}
        MainWindow=window;window.Show();
    }
    protected override void OnExit(System.Windows.ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
}
