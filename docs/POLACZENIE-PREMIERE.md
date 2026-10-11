# Połączenie DIT z Premiere Pro na tej stacji

Panel DIT jest już zainstalowany. Nie trzeba instalować UXP Developer Tool ani ręcznie kopiować klucza.

1. Uruchom DIT ikoną na pulpicie, wybierz produkcję i zaloguj się do zakładki **Admin**.
2. Otwórz kafelek **Premiere i proxy**, następnie kliknij **Skonfiguruj / otwórz Adobe**. DIT przygotuje plik połączenia i pokaże go w Eksploratorze. Jeśli Premiere nie działa, zostanie uruchomione.
3. Po aktualizacji panelu zapisz swoją pracę w Premiere i uruchom Premiere ponownie, żeby wczytać nową wersję dodatku. DIT może pozostać otwarte.
4. W Premiere otwórz **Edit → Preferences → Plugins** (po polsku: **Edycja → Preferencje → Wtyczki**) i włącz **Developer Mode / Tryb programisty**, zgodnie z wymaganiem Adobe dla połączenia UXP. Zatwierdź ustawienia; jeżeli program poprosi o restart, wykonaj go po zapisaniu pracy.
5. Otwórz **Window → UXP Plugins → DIT Media Workspace → DIT** (po polsku pierwszy element to **Okno**). Zależnie od układu menu widoczny może być bezpośrednio wpis **DIT**.
6. W panelu **DIT / Premiere** kliknij **Wybierz plik połączenia**. W oknie wyboru przejdź do folderu:

   `C:\Users\Admin\AppData\Local\DIT\Adobe`

   Wybierz **DIT.ditconnection** i kliknij **Otwórz**. Folder jest ukryty w zwykłym widoku Windows: najłatwiej wkleić powyższą ścieżkę do paska adresu okna wyboru pliku i nacisnąć Enter. Jeśli pliku nie ma, ponownie wykonaj krok 2.
7. Połączenie jest potwierdzone, gdy w DIT, w sekcji **Premiere i proxy**, pojawi się **Połączono z panelem Premiere — kolejka działa automatycznie**. Panel Premiere bez oczekujących zadań pokazuje **Połączono z DIT. Oczekiwanie na zadania.** Przy oczekujących zadaniach może zamiast tego wskazywać wymagane ustawienie lub aktualnie wykonywaną operację.

Panel najpierw potwierdza połączenie z działającym DIT, następnie zapisuje klucz w szyfrowanym magazynie Adobe i usuwa plik DIT.ditconnection. Przy błędzie połączenia plik pozostaje, a panel pokazuje treść błędu. Nie trzeba parować każdego dnia ani po ponownym uruchomieniu DIT. Panel musi jednak pozostać otwarty/załadowany w Premiere; jeśli został zamknięty, otwórz go ponownie z menu.

Po wyczyszczeniu danych DIT trzeba sparować panel ponownie: stary zapamiętany klucz jest nieważny. Panel aktualnej wersji: 0.1.16.

## Połączenie a tworzenie projektów i proxy

- Każda produkcja DIT ma jeden plik Premiere (np. `Film.prproj`). Otwarcie dnia kolejkuje osobną sekwencję „Dzień 1”, „Dzień 2” itd. w tym samym projekcie. Nie musisz ręcznie tworzyć projektu ani sekwencji.
- Domyślna sekwencja UHD 50 fps może powstać z presetu fabrycznego Premiere. Jeśli DIT zgłosi brak tego presetu, wskaż sprawdzony `.sqpreset` lub pusty projekt wzorcowy `.prproj` w **Admin → Konfiguracja → Premiere i profile**.
- Nowe projekty używają domyślnie **DIT Proxy 720p**, dołączonego do programu w folderze `Presets`. Nie trzeba wskazywać pliku `.epr` z pulpitu. W **Admin → Konfiguracja → Premiere i profile → Preset proxy AME** przycisk **Wybierz więcej** pozwala wskazać własny `.epr`, a **Użyj domyślnego** przywraca dołączony preset. Własny preset powinien zapewniać ProRes Proxy MOV, do 1280×720, z zachowaniem klatkażu i kanałów.
- Jeśli otwarty jest Twój inny projekt montażowy, zapisz i zamknij go. DIT nie przełącza automatycznie obcego projektu montażysty.
- Spóźnione nagrania do zamkniętego dnia wymagają zatwierdzenia administratora przed zmianą jego montażu.

Jeśli panel pokazuje **Wstrzymany**, kliknij **Wznów kolejkę** w panelu Premiere. Jeśli aplikacja DIT pokazuje globalnie **Kolejka wstrzymana**, sprawdź przyczynę w panelu administratora; przycisk Premiere nie usuwa tej blokady.

Dokumentacja Adobe: [instalacja, Developer Mode i menu UXP](https://developer.adobe.com/premiere-pro/uxp/plugins/distribution/install/).

## Kolejność i postęp od wersji 0.1.18

Najpierw panel importuje cały komplet i zapisuje klipy na sekwencji dnia. Potem tworzy oraz przypina proxy. W DIT → Premiere i proxy widać etap, licznik gotowych plików i procent kodowania bieżącego pliku. Nagrania mogą już mieć oznaczenie Premiere, gdy część proxy nadal się koduje. Błąd kodowania jest widoczny w tej sekcji; timeline i gotowe, zweryfikowane pliki pozostają zapisane.

Po restarcie panel podejmuje przerwane zadanie po wygaśnięciu 45-sekundowej dzierżawy. Rozpoznaje wcześniejsze importy i klipy, a poprawnych proxy nie koduje ponownie. Globalną pauzę po błędzie nadal trzeba świadomie wznowić w DIT. Starsze zadania utworzone bez dzierżawy wymagają jednorazowego sprawdzenia projektu przed wznowieniem.
