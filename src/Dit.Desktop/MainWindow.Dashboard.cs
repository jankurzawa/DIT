using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dit.Desktop;

public partial class MainWindow
{
    private bool adminView;
    private void BuildDashboard()
    {
        var tiles=new[]{("footage","▷","Nagrania i zgrywania","Materiał wszystkich operatorów, postęp i odtwarzacz."),("overview","▥","Statystyki i dyski","Czas nagrań, rozmiary kopii i dostępne miejsce."),("alerts","⚠","Alerty","Ostrzeżenia, błędy i wyjaśnienia administratora."),("history","≡","Historia operacji","Kto, kiedy i z jakiej karty wykonał operację."),("days","◷","Dni zdjęciowe","Otwieranie, zamykanie i historia dni."),("config","⚙","Konfiguracja","Ustawienia produkcji, zakładek i magazynów."),("adobe","▸","Premiere i proxy","Połączenie Adobe i zatwierdzanie spóźnionych zgrywań."),("reports","↓","Raporty","Eksport historii i statystyk JSON / CSV.")};
        foreach(var (key,icon,title,description) in tiles)
        {
            var content=new StackPanel();content.Children.Add(new TextBlock {Text=icon,FontSize=27,Foreground=(Brush)FindResource("AccentBrush"),Margin=new Thickness(0,0,0,14)});content.Children.Add(new TextBlock {Text=title,FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});content.Children.Add(new TextBlock {Text=description,Foreground=(Brush)FindResource("MutedBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,0),FontSize=13});
            var button=new Button {Content=content,Tag=key,Width=290,Height=180,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(20),Margin=new Thickness(0,0,14,14),ToolTip=description};
            button.Click+=(_,_)=>{if(key=="config")EditConfig(button,new());else if(key=="reports")ExportReport(button,new());else SelectAdminSection(key);};DashboardTiles.Children.Add(button);
        }
    }
    private IEnumerable<FrameworkElement> DetailSections=>new FrameworkElement[]{OverviewSection,AlertsSection,HistorySection,DaysSection,AdobeSection};
    private void AdminHome(object sender,RoutedEventArgs e)=>ShowAdminHome();
    private void ShowAdminHome(bool remember=true)
    {
        if(remember)RememberView("home");ConfigurationHost.Visibility=Visibility.Collapsed;StopPlayer();AdminDashboard.Visibility=Visibility.Visible;AdminDetail.Visibility=Visibility.Collapsed;FootageWorkspace.Visibility=Visibility.Collapsed;BackToTiles.Visibility=Visibility.Collapsed;ContextTitle.Text="Panel administratora";
    }
    private void SelectAdminSection(string key,bool remember=true)
    {
        if(!adminView)return;if(remember)RememberView(key);ConfigurationHost.Visibility=Visibility.Collapsed;StopPlayer();AdminDashboard.Visibility=Visibility.Collapsed;BackToTiles.Visibility=Visibility.Visible;
        foreach(var section in DetailSections)section.Visibility=Visibility.Collapsed;
        FootageWorkspace.Visibility=key=="footage"?Visibility.Visible:Visibility.Collapsed;AdminDetail.Visibility=key=="footage"?Visibility.Collapsed:Visibility.Visible;
        FrameworkElement? selected=key switch {"overview"=>OverviewSection,"alerts"=>AlertsSection,"history"=>HistorySection,"days"=>DaysSection,"adobe"=>AdobeSection,_=>null};if(selected is not null)selected.Visibility=Visibility.Visible;
        ContextTitle.Text=key switch {"footage"=>"Nagrania i zgrywania","overview"=>"Statystyki i dyski","alerts"=>"Alerty","history"=>"Historia operacji","days"=>"Dni zdjęciowe","adobe"=>"Premiere i proxy",_=>"Panel administratora"};
    }
}
