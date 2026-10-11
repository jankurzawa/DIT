using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dit.Core;

namespace Dit.Desktop;

public sealed class DayTimeDialog : Window
{
    private readonly Calendar calendar=new(){Language=System.Windows.Markup.XmlLanguage.GetLanguage("pl-PL"),FirstDayOfWeek=DayOfWeek.Monday};
    private readonly ComboBox hours=new(){Width=65},minutes=new(){Width=65},seconds=new(){Width=65},zones=new(){DisplayMemberPath="DisplayName"},offsets=new(){MinWidth=140};
    private readonly TextBlock clock=new(){FontSize=32,FontWeight=FontWeights.SemiBold},error=new(){TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.Salmon};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    public DateTimeOffset? Value {get;private set;}
    public DayTimeDialog(string title,string zoneId)
    {
        Title=title;Width=650;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(StyleProperty,"AppWindowStyle");
        var panel=new StackPanel{Margin=new Thickness(28)};Content=panel;
        panel.Children.Add(new TextBlock{Text=title,FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,20)});
        var pair=new Grid();pair.ColumnDefinitions.Add(new(){Width=new GridLength(280)});pair.ColumnDefinitions.Add(new());pair.Children.Add(calendar);
        var time=new StackPanel{Margin=new Thickness(20,0,0,0)};Grid.SetColumn(time,1);pair.Children.Add(time);panel.Children.Add(pair);
        time.Children.Add(new TextBlock{Text="Aktualny czas w wybranej strefie"});time.Children.Add(clock);
        time.Children.Add(new TextBlock{Text="Godzina dnia zdjęciowego",Margin=new Thickness(0,20,0,10)});
        var row=new StackPanel{Orientation=Orientation.Horizontal};time.Children.Add(row);
        hours.ItemsSource=Enumerable.Range(0,24).Select(n=>n.ToString("00"));minutes.ItemsSource=seconds.ItemsSource=Enumerable.Range(0,60).Select(n=>n.ToString("00"));
        foreach(var combo in new[]{hours,minutes,seconds}){combo.Margin=new Thickness(0,0,8,0);row.Children.Add(combo);combo.SelectionChanged+=(_,_)=>UpdateOffsets();}
        offsets.ToolTip="Przesunięcie UTC przy dwukrotnym wystąpieniu tej samej godziny";offsets.Visibility=Visibility.Collapsed;time.Children.Add(offsets);
        panel.Children.Add(new TextBlock{Text="Strefa czasowa",Margin=new Thickness(0,20,0,8)});panel.Children.Add(zones);var available=TimeZoneInfo.GetSystemTimeZones();zones.ItemsSource=available;TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId,out var windowsId);zones.SelectedItem=available.FirstOrDefault(z=>z.Id==zoneId||z.Id==windowsId)??TimeZoneInfo.Local;
        zones.SelectionChanged+=(_,_)=>{UpdateClock();UpdateOffsets();};calendar.SelectedDatesChanged+=(_,_)=>UpdateOffsets();
        panel.Children.Add(error);error.Margin=new Thickness(0,12,0,0);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};panel.Children.Add(buttons);
        var now=new Button{Content="Teraz",ToolTip="Ustaw aktualną datę i godzinę"};now.Click+=(_,_)=>SetNow();buttons.Children.Add(now);
        var cancel=new Button{Content="Anuluj",IsCancel=true};buttons.Children.Add(cancel);
        var save=new Button{Content="Zapisz",Style=(Style)FindResource("PrimaryButton")};buttons.Children.Add(save);
        save.Click+=(_,_)=>{try{Value=WallClock.Resolve(calendar.SelectedDate??throw new ArgumentException("Wybierz dzień w kalendarzu."),hours.SelectedIndex,minutes.SelectedIndex,seconds.SelectedIndex,(TimeZoneInfo)zones.SelectedItem,offsets.SelectedItem as TimeSpan?);DialogResult=true;}catch(ArgumentException ex){error.Text=ex.Message;}};
        SetNow();timer.Tick+=(_,_)=>UpdateClock();timer.Start();Closed+=(_,_)=>timer.Stop();
    }
    private void SetNow(){var now=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,(TimeZoneInfo)zones.SelectedItem);calendar.SelectedDate=now.Date;hours.SelectedIndex=now.Hour;minutes.SelectedIndex=now.Minute;seconds.SelectedIndex=now.Second;UpdateClock();UpdateOffsets();}
    private void UpdateClock(){if(zones.SelectedItem is TimeZoneInfo zone)clock.Text=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,zone).ToString("HH:mm:ss");}
    private void UpdateOffsets()
    {
        if(calendar.SelectedDate is not {} date||zones.SelectedItem is not TimeZoneInfo zone||hours.SelectedIndex<0||minutes.SelectedIndex<0||seconds.SelectedIndex<0)return;
        var local=DateTime.SpecifyKind(date.Date.AddHours(hours.SelectedIndex).AddMinutes(minutes.SelectedIndex).AddSeconds(seconds.SelectedIndex),DateTimeKind.Unspecified);
        var selected=offsets.SelectedItem;offsets.ItemsSource=zone.IsAmbiguousTime(local)?zone.GetAmbiguousTimeOffsets(local):[];offsets.SelectedItem=selected;offsets.Visibility=zone.IsAmbiguousTime(local)?Visibility.Visible:Visibility.Collapsed;
    }
}
