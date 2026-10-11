using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Dit.Desktop;

public partial class MainWindow
{
    private string? previewAssetId,playbackPath;
    private bool mediaLoaded,updatingSeek,scrubbing,resumeAfterScrub,playWhenReady;
    private int previewGeneration;
    private readonly DispatcherTimer playbackTimer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private readonly DispatcherTimer scrubTimer=new(){Interval=TimeSpan.FromMilliseconds(80)};
    private double targetPosition;
    private System.Threading.CancellationTokenSource? previewCancellation;
    private void SetupPlayback()
    {
        playbackTimer.Tick+=(_,_)=>UpdatePlayback();playbackTimer.Start();
        scrubTimer.Tick+=async(_,_)=>{scrubTimer.Stop();var resume=!scrubbing&&player?.IsPlaying==true;await ScrubToTarget();if(resume){playWhenReady=true;player?.SetPause(false);}};
    }
    private void ConfigurePlayer()
    {
        if(player is null)return;
        player.Playing+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{if(!mediaLoaded)return;ApplySpeed();if(!playWhenReady){player.SetPause(true);player.Position=(float)targetPosition;}UpdatePlayback();}));
        player.EndReached+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{PlayPauseButton.Content="▶";playWhenReady=false;}));
    }
    private async void AssetSelected(object sender,SelectionChangedEventArgs e)
    {
        if(AssetsGrid.SelectedItem is not AssetRow row||Workspace.Visibility!=Visibility.Visible||row.Asset.Id==previewAssetId)return;
        StopPlayer();previewCancellation=new();var ct=previewCancellation.Token;previewAssetId=row.Asset.Id;var sequence=previewGeneration;var g=generation;PreviewHint.Text="Wczytywanie pierwszej klatki…";PreviewStatus.Text=row.Owner+" · "+row.LogicalName;
        try
        {
            var id=ProjectId();var token=Current().Token;
            var path=await api.Send<JsonElement>(HttpMethod.Post,"/projects/"+id+"/preview",new {AssetId=row.Asset.Id,Lightweight=false},token,ct);
            if(sequence!=previewGeneration||g!=generation)return;playbackPath=path.GetProperty("path").GetString();
            if(row.Asset.Media?.HasVideo==true)
            {
                var result=await api.Send<JsonElement>(HttpMethod.Post,"/projects/"+id+"/preview",new {AssetId=row.Asset.Id,Thumbnail=true},token,ct);
                if(sequence!=previewGeneration||g!=generation||previewAssetId!=row.Asset.Id)return;
                var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(result.GetProperty("path").GetString()!);bitmap.EndInit();bitmap.Freeze();Thumbnail.Source=bitmap;Thumbnail.Visibility=mediaLoaded?Visibility.Collapsed:Visibility.Visible;PreviewHint.Visibility=Visibility.Collapsed;
            }
            else PreviewHint.Text="Nagranie audio · naciśnij ▶";
            UpdatePlayback();
        }
        catch(Exception ex){if(sequence==previewGeneration){PreviewHint.Text="Nie udało się wczytać podglądu";PreviewStatus.Text=ex.Message;}}
    }
    private void StopPlayer()
    {
        previewGeneration++;previewCancellation?.Cancel();previewCancellation?.Dispose();previewCancellation=null;scrubTimer.Stop();player?.Stop();mediaLoaded=false;playWhenReady=false;previewAssetId=null;playbackPath=null;scrubbing=false;targetPosition=0;
        Video.Visibility=Visibility.Collapsed;Thumbnail.Visibility=Visibility.Collapsed;Thumbnail.Source=null;PreviewHint.Visibility=Visibility.Visible;PreviewHint.Text="Wybierz nagranie z listy";
        if(PlayPauseButton is not null){PlayPauseButton.Content="▶";PreviewStatus.Text="";PlaybackTime.Text="00:00 / 00:00";updatingSeek=true;Seek.Value=0;updatingSeek=false;}
    }
    private bool LoadMedia(bool play)
    {
        if(player is null||vlc is null||playbackPath is null)return false;playWhenReady=play;
        if(player.State==VLCState.Ended){player.Stop();mediaLoaded=false;targetPosition=0;}
        if(!mediaLoaded)
        {
            using var media=new Media(vlc,new Uri(playbackPath));media.AddOption(":start-time="+(targetPosition*SelectedDurationSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture));
            mediaLoaded=true;if(!player.Play(media)){mediaLoaded=false;PreviewStatus.Text="Nie można odtworzyć nagrania.";return false;}
        }
        else player.SetPause(!play);
        Video.Visibility=Visibility.Visible;Thumbnail.Visibility=Visibility.Collapsed;PreviewHint.Visibility=Visibility.Collapsed;return true;
    }
    private void TogglePlay(object sender,RoutedEventArgs e)
    {
        if(Workspace.Visibility!=Visibility.Visible)return;
        if(player?.IsPlaying==true){playWhenReady=false;player.SetPause(true);}else LoadMedia(true);UpdatePlayback();
    }
    private double SelectedDurationSeconds()=>(AssetsGrid.SelectedItem as AssetRow)?.Asset.Media?.DurationTicks is {} ticks?TimeSpan.FromTicks(ticks).TotalSeconds:player?.Length/1000d??0;
    private void UpdatePlayback()
    {
        if(PlayPauseButton is null)return;
        var seconds=mediaLoaded?(player?.Time??0)/1000d:targetPosition*SelectedDurationSeconds();var duration=SelectedDurationSeconds();
        string Time(double value){var time=TimeSpan.FromSeconds(Math.Max(0,value));return time.TotalHours>=1?$"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}":time.ToString(@"mm\:ss");}
        PlaybackTime.Text=Time(seconds)+" / "+Time(duration);
        PlayPauseButton.Content=player?.IsPlaying==true?"❚❚":"▶";
        if(!scrubbing&&mediaLoaded&&player is not null){updatingSeek=true;Seek.Value=Math.Clamp(player.Position*1000,0,1000);updatingSeek=false;}
    }
    private void SeekBegin(object sender,MouseButtonEventArgs e){scrubbing=true;resumeAfterScrub=player?.IsPlaying==true;playWhenReady=false;player?.SetPause(true);}
    private async void SeekEnd(object sender,MouseButtonEventArgs e){scrubTimer.Stop();targetPosition=Seek.Value/1000;await ScrubToTarget();scrubbing=false;if(resumeAfterScrub){playWhenReady=true;player?.SetPause(false);}UpdatePlayback();}
    private void SeekValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(updatingSeek||previewAssetId is null||Workspace.Visibility!=Visibility.Visible)return;targetPosition=e.NewValue/1000;
        // Continuous dragging must decode frames before mouse-up: throttle, never debounce.
        if(!scrubTimer.IsEnabled)scrubTimer.Start();
    }
    private Task ScrubToTarget()
    {
        if(!LoadMedia(false)||player is null)return Task.CompletedTask;
        player.Time=(long)(targetPosition*SelectedDurationSeconds()*1000);UpdatePlayback();return Task.CompletedTask;
    }
    private void NextClip(object sender,RoutedEventArgs e)=>SelectAdjacentClip(1);
    private void PreviousClip(object sender,RoutedEventArgs e)=>SelectAdjacentClip(-1);
    private void SelectAdjacentClip(int delta)
    {
        var rows=AssetsGrid.Items.Cast<object>().OfType<AssetRow>().ToArray();var index=Array.IndexOf(rows,AssetsGrid.SelectedItem as AssetRow);var next=index+delta;
        if(next>=0&&next<rows.Length){AssetsGrid.SelectedItem=rows[next];AssetsGrid.ScrollIntoView(rows[next]);}
    }
    private void SpeedChanged(object sender,SelectionChangedEventArgs e)=>ApplySpeed();
    private void ApplySpeed(){if(player is not null&&PlaybackSpeed is not null)player.SetRate(new[]{1f,1.5f,2f,3f,4f}[Math.Clamp(PlaybackSpeed.SelectedIndex,0,4)]);}
}
