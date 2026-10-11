using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dit.Core;
using LibVLCSharp.Shared;

namespace Dit.Desktop;

public partial class MainWindow
{
    internal async Task<string[]> RunPlaybackSmoke(string clip,string output)
    {
        try
        {
            PrepareDesignPreview("admin-footage");AssetsGrid.SelectionChanged-=AssetSelected;
            var media=new MediaInfo(TimeSpan.FromSeconds(6).Ticks,null,null,25,1,0,true);
            Asset TestAsset(string id)=>new(id,"test","preview","operator1","cam1","day","test.mov","test.mov","hash",1,true,media,new(clip,"test","main","hash",1,true),new(clip,"test","backup","hash",1,true));
            var rows=new[]{new AssetRow(TestAsset("a"),"Nagranie 1","1 MB","00:00:06","unresolved","Operator 1"),new AssetRow(TestAsset("b"),"Nagranie 2","1 MB","00:00:06","unresolved","Operator 2")};AssetsGrid.ItemsSource=rows;AssetsGrid.SelectedIndex=0;playbackPath=clip;previewAssetId="a";
            vlc=await Task.Run(()=>{LibVLCSharp.Shared.Core.Initialize();return new LibVLC("--no-video-title-show","--avcodec-hw=any","--no-audio");});player=new MediaPlayer(vlc);Video.MediaPlayer=player;ConfigurePlayer();
            ShowActivated=false;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.Manual;Left=-20000;Top=-20000;Show();await Task.Delay(150);
            if(OwnerColumn.Visibility!=Visibility.Visible||FootageWorkspace.Visibility!=Visibility.Visible||BatchesGrid.ActualWidth<180||AssetsGrid.ActualWidth<200)throw new InvalidOperationException("Brak równoczesnych paneli nagrań i transferów albo właściciela.");
            System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject parent){for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Children(child))yield return descendant;}}
            var bars=Children(BatchesGrid).OfType<ProgressBar>().ToArray();
            if(bars.Length<2||!bars.Any(b=>Math.Abs(b.Value-47)<.01&&b.ActualWidth>50))throw new InvalidOperationException("Brak widocznych pasków postępu transferu. "+string.Join(", ",Children(BatchesGrid).OfType<FrameworkElement>().Select(b=>b.GetType().Name+":"+b.ActualWidth+"x"+b.ActualHeight)));
            var tooltip=new ToolTip {Content=PlayPauseButton.ToolTip,PlacementTarget=PlayPauseButton,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};tooltip.IsOpen=true;await Task.Delay(150);if(!tooltip.IsOpen||tooltip.ActualWidth<=0||string.IsNullOrWhiteSpace(tooltip.Content?.ToString()))throw new InvalidOperationException("Tooltip nie otwiera się w rzeczywistym oknie WPF.");tooltip.IsOpen=false;
            async Task Until(Func<bool> ready,string message){for(var i=0;i<120;i++){if(ready())return;await Task.Delay(50);}throw new InvalidOperationException(message);}
            TogglePlay(this,new());await Until(()=>player.IsPlaying&&player.Time>150,"Odtwarzanie nie rozpoczęło się.");TogglePlay(this,new());await Until(()=>!player.IsPlaying,"Pauza nie działa.");
            var first=Path.Combine(Path.GetDirectoryName(output)!,"playback-first-frame.png");player.TakeSnapshot(0,first,320,180);await Until(()=>File.Exists(first)&&new FileInfo(first).Length>256,"Brak obrazu odtwarzacza.");
            targetPosition=.65;await ScrubToTarget();await Until(()=>player.Time>3300&&player.Time<4500,"Przewijanie na pauzie nie ustawia klatki.");await Task.Delay(200);
            var scrub=Path.Combine(Path.GetDirectoryName(output)!,"playback-scrub-frame.png");player.TakeSnapshot(0,scrub,320,180);await Until(()=>File.Exists(scrub)&&new FileInfo(scrub).Length>256,"Brak obrazu po przewinięciu.");
            byte[] Pixels(string path){var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();var bytes=new byte[image.PixelHeight*image.PixelWidth*4];image.CopyPixels(bytes,image.PixelWidth*4,0);return bytes;}
            if(Pixels(first).SequenceEqual(Pixels(scrub)))throw new InvalidOperationException("Przewijanie nie zmieniło widocznej klatki.");
            LoadMedia(true);await Until(()=>player.IsPlaying,"Nie wznowiono odtwarzania przed przewijaniem.");
            var mouse=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left);
            SeekBegin(Seek,mouse);
            for(var i=0;i<30;i++){Seek.Value=100+i*15;await Task.Delay(20);}
            if(player.Time<1600||player.IsPlaying)throw new InvalidOperationException("Suwak nie przewija podczas trzymania myszy.");
            var live=Path.Combine(Path.GetDirectoryName(output)!,"playback-live-drag.png");player.TakeSnapshot(0,live,320,180);await Until(()=>File.Exists(live)&&new FileInfo(live).Length>256,"Brak klatki podczas przeciągania.");
            if(Pixels(first).SequenceEqual(Pixels(live)))throw new InvalidOperationException("Widoczna klatka nie zmienia się przed puszczeniem myszy.");
            SeekEnd(Seek,mouse);await Task.Delay(150);
            SeekBegin(Seek,mouse);Seek.Value=400;SeekEnd(Seek,mouse);await Task.Delay(300);if(!player.IsPlaying)throw new InvalidOperationException("Przeciąganie suwaka podczas odtwarzania pozostawia film na pauzie.");
            for(var i=1;i<5;i++){PlaybackSpeed.SelectedIndex=i;await Task.Delay(50);if(Math.Abs(player.Rate-new[]{1f,1.5f,2f,3f,4f}[i])>.01)throw new InvalidOperationException("Nie zastosowano prędkości odtwarzania.");}
            PlaybackSpeed.SelectedIndex=4;LoadMedia(true);await Until(()=>player.State==VLCState.Ended,"Nagranie nie dotarło do końca.");TogglePlay(this,new());await Until(()=>player.IsPlaying&&player.Time>100&&player.Time<3000,"Ponowne odtwarzanie po końcu nie działa.");
            NextClip(this,new());if(AssetsGrid.SelectedIndex!=1)throw new InvalidOperationException("Następne nagranie nie działa.");PreviousClip(this,new());if(AssetsGrid.SelectedIndex!=0)throw new InvalidOperationException("Poprzednie nagranie nie działa.");
            ShowAdminHome();if(FootageWorkspace.Visibility!=Visibility.Collapsed||AdminDashboard.Visibility!=Visibility.Visible||Thumbnail.Source is not null)throw new InvalidOperationException("Powrót do kafelków pozostawia podgląd.");
            return ["Continuous mouse-held scrubbing updates decoded frames before release","Three panels remain visible side by side; admin sees owner","Transfer progress bars are rendered with real values","Tooltip opens as a real WPF popup","Real LibVLC playback and pause","Scrubbing while paused changes the decoded visible frame","Dragging the seek slider resumes playback when it was playing","Playback speeds 1.5x, 2x, 3x and 4x","Playback restarts after reaching the end","Previous and next clip controls","Returning to dashboard clears playback"];
        }
        finally{StopPlayer();exit=true;Close();}
    }
}
