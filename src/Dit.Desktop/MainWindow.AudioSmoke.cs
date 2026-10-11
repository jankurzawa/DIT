using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Dit.Core;

namespace Dit.Desktop;

public partial class MainWindow
{
    internal async Task<string[]> RunAudioSmoke()
    {
        List<string> passed=[];
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);passed.Add(label);}
        IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
        {
            if(root is T match)yield return match;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Descendants<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
        }
        try
        {
            PrepareDesignPreview("audio");Left=-20000;Top=-20000;Show();await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            AudioTrack.ItemsSource=null;SetAudioInputs(Enumerable.Range(1,20).Select(i=>new AudioInputProfile("mic"+i,"Mikroport "+i,1,["Dźwięk"])).ToArray());
            Check(AudioTrack.Items.Count==21&&AudioTrack.SelectedIndex==0&&!StartIngestButton.IsEnabled,"Twenty named devices are available and ingest requires a selection");
            AudioTrack.ApplyTemplate();Descendants<ToggleButton>(AudioTrack).First().IsChecked=true;await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            var popup=(Popup)AudioTrack.Template.FindName("PART_Popup",AudioTrack);
            Check(popup.IsOpen&&popup.Child is not null,"Dropdown arrow opens its real WPF popup");
            var displayed=Descendants<TextBlock>(popup.Child!).Select(t=>t.Text).ToArray();
            Check(displayed.Contains("Mikroport 1")&&displayed.Contains("Mikroport 2"),"Device names render in the popup rather than an empty list");
            ((ComboBoxItem)AudioTrack.ItemContainerGenerator.ContainerFromIndex(2)).IsSelected=true;
            Check(StartIngestButton.IsEnabled&&((AudioInputProfile)AudioTrack.SelectedItem).DeviceId=="mic2"&&AudioSourceHint.Text.Contains("Mikroport 2"),"Selecting a device enables ingest and identifies its folder and track");
            AudioTrack.IsDropDownOpen=false;SetAudioInputs(Enumerable.Range(1,20).Select(i=>new AudioInputProfile("mic"+i,"Mikroport "+i,1,[])).ToArray());
            Check(((AudioInputProfile)AudioTrack.SelectedItem).DeviceId=="mic2","Refreshing sources preserves the selected device");
            selectedAudioFiles=["video.mp4"];
            try{CreateIngestRequest();throw new InvalidOperationException("Video accepted by Audio UI");}catch(ArgumentException){passed.Add("Manually supplied video selection is blocked by the Audio UI");}
            selectedAudioFiles=["take.WAV"];Check(CreateIngestRequest().AudioDeviceId=="mic2","Audio files retain the selected device ID");
            SetAudioInputs([]);Check(!StartIngestButton.IsEnabled&&AudioSourceHint.Text.Contains("Brak urządzeń"),"Empty configuration explains how to add sources and blocks ingest");
            return passed.ToArray();
        }
        finally{exit=true;Close();}
    }
}
