using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Dit.Desktop;

internal static class Tooltips
{
    public static void Apply(Button button)
    {
        ToolTipService.SetPlacement(button,PlacementMode.Bottom);ToolTipService.SetShowOnDisabled(button,true);ToolTipService.SetInitialShowDelay(button,150);ToolTipService.SetShowDuration(button,30000);
        if(button.ToolTip is not null)return;
        button.ToolTip=Describe(button.Content?.ToString()??"");
    }
    public static string Describe(string text)=>text switch
        {
            "+  Nowy projekt" or "+  Utwórz pierwszy projekt" or "Utwórz projekt"=>"Tworzy nowy projekt produkcji z własnymi zakładkami, kontami i magazynami.",
            "Zablokuj wszystko" or "Wyloguj ze wszystkich kart"=>"Wylogowuje wszystkie zakładki. Uruchomione zgrywania i backupy pracują dalej.",
            "＋ Dodaj zakładkę"=>"Dodaje osobną sekcję z nazwą i opcjonalnym hasłem nowej kamery lub operatora.",
            "＋ Dodaj źródło audio"=>"Dodaje rejestrator lub źródło z własną nazwą i liczbą kanałów w jednej zakładce Audio.",
            "Usuń sekcję"=>"Usuwa tę sekcję z formularza. Wcześniej zgrane pliki pozostają zachowane.",
            "▶"=>"Odtwarza wybrane nagranie.","❚❚"=>"Wstrzymuje odtwarzanie na bieżącej klatce.","⏭"=>"Wybiera następne nagranie z listy.","⏮"=>"Wybiera poprzednie nagranie z listy.",
            "← Panel administratora"=>"Wraca do kafelków głównych opcji administratora.",
            "Odblokuj zakładkę  →"=>"Otwiera wybraną zakładkę. Jeśli nie ustawiono hasła, pozostaw pole puste.",
            "Zmień hasło"=>"Zmienia hasło bieżącego konta. Hasło może być puste; zmiana blokuje jego dotychczasowe sesje.",
            "Wybierz kartę…"=>"Wybiera kartę pamięci lub folder źródłowy do zgrania w tej zakładce.",
            "↓  Rozpocznij zgrywanie"=>"Przyjmuje źródło do kolejki, tworzy kopię główną i backup oraz sprawdza SHA-256.",
            "Konfiguracja"=>"Edytuje ustawienia produkcji, magazynów, kont, audio i integracji Premiere.",
            "Otwórz dzień"=>"Otwiera kolejny dzień zdjęciowy. Początek zakresu zdjęć podaje administrator.",
            "Zamknij dzień"=>"Ręcznie zamyka bieżący dzień zdjęciowy. Materiał dostarczony później zachowuje właściwy dzień.",
            "Wstrzymaj"=>"Wstrzymuje uruchamianie kolejnych etapów kolejki i blokuje czyszczenie kart.",
            "Wznów"=>"Wznawia kolejkę i ponawia nieukończone operacje po usunięciu przyczyny błędu. Nie kasuje raportów ani historii.",
            "Raport JSON / CSV"=>"Eksportuje statystyki i historię produkcji do pliku JSON lub CSV.",
            "Połącz Adobe"=>"Pokazuje klucz połączenia lokalnego panelu DIT w Premiere Pro.",
            "Reset hasła"=>"Administrator ustawia nowe hasło wybranego konta. Puste hasło jest dozwolone.",
            "Notatka do alertu"=>"Dopisuje wyjaśnienie do zaznaczonego alertu, zachowując historię.",
            "Oznacz jako wyjaśniony"=>"Zamyka zaznaczony alert z uzasadnieniem i datą. Nie wznawia kolejki ani nie zmienia plików.",
            "Otwórz ponownie"=>"Przywraca zaznaczony alert do spraw wymagających wyjaśnienia, z zapisem przyczyny.",
            "Wyczyść filtry"=>"Pokazuje całą historię, usuwając filtry czasu, poziomu, operacji, zakładki i tekstu.",
            "▷  Podgląd"=>"Odtwarza zaznaczone nagranie ze zweryfikowanej kopii lub przygotowanego proxy.",
            "Przypisz dzień / źródło"=>"Rozstrzyga dzień i urządzenie zaznaczonego nieprzypisanego nagrania. Oryginał pozostaje niezmieniony.",
            "Odtwórz z backupu"=>"Odtwarza pierwotną strukturę materiału do osobnego folderu i sprawdza sumy SHA-256.",
            "Korekta synchronizacji"=>"Administrator ustawia pozycję zaznaczonego klipu na osi czasu z zapisem korekty w historii.",
            "Zatwierdź spóźnione zgrywanie"=>"Pozwala zaktualizować zamknięty projekt Premiere materiałem dostarczonym po zamknięciu dnia.",
            "Pauza / odtwórz"=>"Wstrzymuje lub kontynuuje odtwarzanie podglądu.",
            "Zatrzymaj"=>"Zatrzymuje podgląd i usuwa wyświetloną klatkę.",
            "Anuluj"=>"Zamyka formularz bez zapisywania wprowadzonych zmian.",
            "Zapisz konfigurację"=>"Sprawdza pola i zapisuje ustawienia projektu. W przypadku błędu formularz pozostaje otwarty.",
            "Spróbuj ponownie"=>"Ponawia uruchomienie interfejsu i połączenie z procesem roboczym.",
            "Zapisz"=>"Zatwierdza wpisaną wartość w tym oknie.",
            "Zapisz profile"=>"Zapisuje zaawansowane profile urządzeń po sprawdzeniu ich formatu.",
            "Profile urządzeń — ustawienia zaawansowane"=>"Edytuje profile zegarów, numeracji, LTC i kwalifikacji czyszczenia urządzeń.",
            "…"=>"Otwiera okno wyboru pliku lub folderu.",
            _=>"Wykonuje czynność „"+text+"” w bieżącym oknie."
        };
}
public sealed class ButtonTooltipConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>Tooltips.Describe(value?.ToString()??"");
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
