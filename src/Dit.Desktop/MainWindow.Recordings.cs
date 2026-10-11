using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Dit.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<AssetRow> assetRows=new();
    private bool settingAssetFilters;
    private void SetupAssetFilters()
    {
        settingAssetFilters=true;
        AssetStage.ItemsSource=new[]{"Wszystkie etapy","W toku","Kopie i Premiere gotowe","W pełni gotowe","Do synchronizacji","Brak proxy","Brak w Premiere","Błędy zgrywania"};AssetStage.SelectedIndex=0;
        AssetSort.ItemsSource=new[]{"Nazwa A–Z","Nazwa Z–A","Etap pracy","Właściciel","Największe pliki","Najdłuższe nagrania"};AssetSort.SelectedIndex=0;
        CollectionViewSource.GetDefaultView(assetRows).Filter=item=>MatchesAsset((AssetRow)item);
        settingAssetFilters=false;ApplyAssetSort();
    }
    private bool MatchesAsset(AssetRow row)
    {
        var stage=AssetStage.SelectedIndex switch
        {
            1=>row.Processing&&row.Error.Length==0,
            2=>!row.Processing&&row.Error.Length==0,
            3=>row.StatusOrder==3,
            4=>row.Workflow?.Synchronized!=true,
            5=>row.Workflow?.ProxyRequired==true&&!row.Workflow.Proxy,
            6=>row.Workflow?.InPremiere!=true,
            7=>row.Error.Length>0,
            _=>true
        };
        var query=AssetSearch.Text.Trim();
        return stage&&(query.Length==0||row.LogicalName.Contains(query,StringComparison.CurrentCultureIgnoreCase)||row.Owner.Contains(query,StringComparison.CurrentCultureIgnoreCase));
    }
    private void SetAssetRows(AssetRow[] rows)
    {
        var selected=(AssetsGrid.SelectedItem as AssetRow)?.Asset.Id;
        var view=CollectionViewSource.GetDefaultView(assetRows);
        if(!ReferenceEquals(AssetsGrid.ItemsSource,view)){AssetsGrid.ItemsSource=view;ApplyAssetSort();}
        // WPF ListCollectionView cannot process Replace notifications while refresh is deferred.
        var ids=rows.Select(r=>r.Asset.Id).ToHashSet();
        for(var i=assetRows.Count-1;i>=0;i--)if(!ids.Contains(assetRows[i].Asset.Id))assetRows.RemoveAt(i);
        var indices=assetRows.Select((r,i)=>(r.Asset.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
        foreach(var row in rows)if(indices.TryGetValue(row.Asset.Id,out var i)){if(assetRows[i]!=row)assetRows[i]=row;}else assetRows.Add(row);
        AssetsGrid.SelectedItem=view.Cast<AssetRow>().FirstOrDefault(r=>r.Asset.Id==selected);
        if(selected is not null&&AssetsGrid.SelectedItem is null)StopPlayer();
        UpdateAssetCount();
    }
    private void ClearAssetRows()
    {
        assetRows.Clear();settingAssetFilters=true;AssetStage.SelectedIndex=0;AssetSort.SelectedIndex=0;AssetSearch.Clear();settingAssetFilters=false;ApplyAssetSort();
    }
    private void UpdateAssetCount()
    {
        var count=CollectionViewSource.GetDefaultView(assetRows).Cast<object>().Count();
        AssetsSummary.Text=$"{count} z {assetRows.Count} nagrań · {assetRows.Count(r=>r.Processing&&r.Error.Length==0)} w toku · {assetRows.Count(r=>!r.Processing&&r.Error.Length==0)} kopie i Premiere gotowe";
        EmptyAssetsHint.Text=assetRows.Count==0?"Brak nagrań. Wybierz kartę, aby rozpocząć zgrywanie.":"Brak nagrań pasujących do filtrów.";
        EmptyAssetsHint.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
    }
    private void AssetFilterChanged(object sender,SelectionChangedEventArgs e)=>RefreshAssetFilter();
    private void AssetSearchChanged(object sender,TextChangedEventArgs e)=>RefreshAssetFilter();
    private void RefreshAssetFilter()
    {
        if(settingAssetFilters||AssetStage is null||AssetSearch is null||AssetsGrid is null)return;
        CollectionViewSource.GetDefaultView(assetRows).Refresh();UpdateAssetCount();
        if(previewAssetId is not null&&AssetsGrid.SelectedItem is null)StopPlayer();
    }
    private void AssetSortChanged(object sender,SelectionChangedEventArgs e){if(!settingAssetFilters&&AssetSort is not null)ApplyAssetSort();}
    private void ApplyAssetSort()
    {
        var view=CollectionViewSource.GetDefaultView(assetRows);
        using(view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(AssetSort.SelectedIndex switch
            {
                1=>new(nameof(AssetRow.LogicalName),ListSortDirection.Descending),
                2=>new(nameof(AssetRow.StatusOrder),ListSortDirection.Ascending),
                3=>new(nameof(AssetRow.Owner),ListSortDirection.Ascending),
                4=>new("Asset.Bytes",ListSortDirection.Descending),
                5=>new(nameof(AssetRow.SortDuration),ListSortDirection.Descending),
                _=>new(nameof(AssetRow.LogicalName),ListSortDirection.Ascending)
            });
            if(AssetSort.SelectedIndex!=0&&AssetSort.SelectedIndex!=1)view.SortDescriptions.Add(new(nameof(AssetRow.LogicalName),ListSortDirection.Ascending));
        }
    }
    internal async System.Threading.Tasks.Task<string[]> RunRecordingsSmoke()
    {
        PrepareDesignPreview("admin-overview");
        using(var fixture=System.Text.Json.JsonDocument.Parse("""{"adobe":[{"id":"proxy-test","state":"inflight","error":null,"progress":{"stage":"proxy","completed":1,"total":4,"percent":50}}]}"""))
            AdobeGrid.ItemsSource=fixture.RootElement.GetProperty("adobe").EnumerateArray().Select(a=>new {Operation="Test",State="W toku",Info=AdobeProgressInfo(a),Percent=AdobeProgressPercent(a)}).ToArray();
        var root=(FrameworkElement)Content;root.Measure(new Size(1440,920));root.Arrange(new Rect(0,0,1440,920));root.UpdateLayout();
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        if(OverviewSection.Visibility!=Visibility.Visible||AdobeGrid.Items.Count!=1)throw new InvalidOperationException("Przegląd nie został wyrenderowany z postępem Adobe.");
        SelectAdminSection("footage");
        var media=new Dit.Core.MediaInfo(10000000,null,null,AudioChannels:2,HasVideo:true);
        AssetRow Row(string id,long bytes,bool proxy,bool premiere,bool sync,string owner="Kamera 1")
        {
            var replica=new Dit.Core.Replica("D:/"+id,"v","d","h",bytes,true);
            var asset=new Dit.Core.Asset(id,"batch","preview",owner,"cam1","day","",id+".MOV","h",bytes,true,media,replica,replica);
            return CreateAssetRow(asset,new(id,true,true,proxy,true,premiere,sync,sync?"timecode":"unresolved"));
        }
        var rows=new[]{Row("B",100,false,false,false),Row("A",20,true,true,false),Row("C",200,true,true,true),Row("D",1,false,false,false) with {Error="Błąd kopii"}};
        SetAssetRows(rows);var view=CollectionViewSource.GetDefaultView(assetRows);
        void Expect(params string[] ids){if(!view.Cast<AssetRow>().Select(r=>r.Asset.Id).SequenceEqual(ids))throw new InvalidOperationException("Niepoprawny filtr lub sort: "+string.Join(",",view.Cast<AssetRow>().Select(r=>r.Asset.Id)));}
        AssetStage.SelectedIndex=1;Expect("B");AssetStage.SelectedIndex=2;Expect("A","C");AssetStage.SelectedIndex=3;Expect("C");AssetStage.SelectedIndex=4;Expect("A","B","D");AssetStage.SelectedIndex=5;Expect("B","D");AssetStage.SelectedIndex=7;Expect("D");
        AssetStage.SelectedIndex=0;AssetSort.SelectedIndex=4;Expect("C","B","A","D");AssetSearch.Text="a.mov";Expect("A");AssetSearch.Clear();
        AssetStage.SelectedIndex=1;SetAssetRows(rows.Select(r=>r.Asset.Id=="B"?r with {Workflow=r.Workflow! with {Proxy=true,InPremiere=true}}:r).ToArray());Expect();
        if(AssetSort.SelectedIndex!=4||AssetStage.SelectedIndex!=1)throw new InvalidOperationException("Odświeżenie resetuje filtry.");
        AssetStage.SelectedIndex=0;AssetSearch.Text="Kamera 1";Expect("C","B","A","D");
        HideWorkspace();if(assetRows.Count!=0||AssetSearch.Text.Length>0)throw new InvalidOperationException("Blokada zostawia dane nagrań.");
        exit=true;Close();
        return ["Statistics renders read-only Adobe progress without crashing","Workflow filters distinguish processing, completed copies, synchronization and errors","Filename and owner search","Size sorting uses numeric bytes","Live workflow update preserves filters and sort","Lock clears recordings and search"];
    }
}
