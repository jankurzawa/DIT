# Panel DIT dla Premiere Pro

## Instalacja i kwalifikacja

1. Wymagane Premiere Pro i Media Encoder 26.5+. Nie aktualizujemy aplikacji Adobe automatycznie. Odczyt wersji tej stacji podczas implementacji: oba programy 26.5.2.
2. Gotowy pakiet zawiera `adobe/DIT-Premiere.ccx`. Przycisk „Skonfiguruj / otwórz Adobe” instaluje go przez oficjalny instalator UPIA z Creative Cloud. Alternatywnie uruchom plik CCX dwuklikiem. Na tej stacji instalacja 0.1.8 zakończyła się sukcesem, a UPIA potwierdziło panel jako włączony dla Premiere 26.5.2.
3. Otwórz w Premiere **Okno → UXP Plugins → DIT** (nazwa dodatku: DIT Media Workspace). Jeśli nowo zainstalowany panel nie pojawia się w menu, zapisz pracę i uruchom ponownie Premiere. Dokumentacja Adobe wymaga Developer Mode w preferencjach Plugins przed połączeniem z pluginem. Pierwsze otwarcie panelu i wybór pliku połączenia wykonuje użytkownik. UXP Developer Tool nie jest wymagany do instalowania gotowego CCX.
4. Każdy mikroport dodaj i nazwij jako osobne urządzenie w Konfiguracja → Zakładki i audio. Wszystkie korzystają z jednej zakładki Audio; przy zgrywaniu wybierz urządzenie z dropdownu. Nowe źródła są mono, a kanały rzeczywistego pliku są odczytywane z metadanych. DIT generuje preset na podstawie fabrycznego UHD 50 fps Premiere, z osobnymi nazwanymi ścieżkami. Jeśli instalacja nie zawiera fabrycznego presetu, wskaż sprawdzony `.sqpreset` lub pusty `.prproj` w konfiguracji. Istniejące prawdziwe źródła wielokanałowe zachowują parametry; źródła ponad 2 kanały wymagają sprawdzonego presetu adaptacyjnego albo szablonu.
5. Nowe produkcje mają wybrany dołączony do programu preset **DIT Proxy 720p**. Własny `.epr` wybierzesz przez **Konfiguracja → Premiere i profile → Wybierz więcej**; **Użyj domyślnego** przywraca preset DIT. Walidacja nadal wymaga ProRes Proxy MOV, do 1280×720, zachowania FPS, czasu trwania i kanałów. Kolejność ścieżek audio odpowiada konfiguracji `devices`: kamery i źródła audio. Własny szablon musi zapewniać taki układ.
6. W Admin → Premiere i proxy wybierz „Skonfiguruj / otwórz Adobe”. Program uruchamia zainstalowane Premiere i wskazuje plik `DIT.ditconnection` w Eksploratorze. W panelu DIT w Premiere wybierz ten plik przyciskiem „Wybierz plik połączenia”. To jednorazowe parowanie; nie trzeba przepisywać klucza. Panel zapisuje go w szyfrowanym magazynie UXP, a plik transportowy usuwa po zapisaniu. Klucz daje dostęp wyłącznie do ograniczonego protokołu Adobe, nie operacji użytkowników.
7. Uruchom próbę z krótkimi plikami 25/50 fps i osobnymi WAV mono. Sprawdź wszystkie kryteria poniżej. Samo sparowanie nie jest potwierdzeniem kwalifikacji integracji.

Po ponownym wczytaniu/otwarciu panelu połączenie jest odtwarzane automatycznie. Nie trzeba parować każdego dnia ani po każdym restarcie DIT. Po pierwszym parowaniu worker uruchamia Premiere dla oczekujących zadań; panel musi być załadowany w Adobe. Instalacja nie jest równoznaczna z otwarciem panelu ani potwierdzonym połączeniem. Nie deklarujemy niezawodnego automatycznego otwierania panelu przy każdym starcie Premiere; jeżeli pozostał zamknięty, otwórz go z menu. Wygaśnięcie lub zmiana klucza wymaga ponownego wybrania pliku połączenia.

Każda produkcja DIT ma jeden plik Premiere. Otwarcie dnia automatycznie kolejkuje utworzenie osobnej sekwencji „Dzień N” w tym pliku, także bez nagrań. Kolejny dzień korzysta z tego samego projektu; panel wybiera właściwą sekwencję po nazwie. Przy przejściu do innej produkcji panel zapisuje i zamyka poprzedni ukończony projekt DIT. Jeśli otwarty jest obcy projekt montażysty, oczekuje na jego zamknięcie; kopie plików pracują dalej. Pierwszy projekt jest tworzony z wzorca lub presetu, a kolejne sekwencje z presetu. Sekwencja wzorca otrzymuje nazwę pierwszego dnia. Przed zmianami worker zapisuje kopię `.before.bak`, po potwierdzonym zapisie powstaje kolejna wersja. Brakujące ustawienia Adobe są stanem oczekiwania, bez automatycznego uznawania proxy lub montażu za ukończone.

Docelowy plik jest rezerwowany przy utworzeniu produkcji. Inna produkcja o tej samej nazwie lub istniejący plik powoduje wybór kolejnej wolnej nazwy, np. Film(2).prproj. Zmiana nazwy produkcji lub folderu w konfiguracji nie przenosi już przypisanego projektu. Zgoda na spóźnione nagrania pozostaje przypisana do dnia; kopia bezpieczeństwa obejmuje cały plik produkcji.

## Co sprawdzić na rzeczywistym hoście

- Import 15 urządzeń, zachowanie nazw folderów, źródeł i ścieżek oraz kanałów adaptacyjnych.
- 25 fps na sekwencji 50 fps ma właściwy czas trwania i nie jest zwolnione.
- AME: zdarzenie końca jest skorelowane z konkretnym plikiem; worker niezależnie sprawdza wynik. Niejednoznaczne zdarzenie nie oznacza ukończenia.
- Wynik proxy: ProRes Proxy, maksymalnie 1280×720, proporcje, FPS, czas oraz liczba kanałów zgodne z oryginałem. Zatwierdzenie cyklu wymaga całego kompletu proxy pod dokładnie zaplanowanymi ścieżkami.
- Etykieta o indeksie 2 jest żółta przy ustawieniach użytkownika. Nazwy/kolory etykiet Premiere można zmieniać — sprawdź je podczas kwalifikacji.
- Klipy bez pozycji i ich późniejsze przesunięcie nie naruszają poprawnych klipów ani powiązanego audio.
- Ponowienie po awarii rozpoznaje istniejące referencje po pełnej ścieżce, nie po samej nazwie.
- Zapis, zamknięcie i ponowne otwarcie projektu; nadal poprawne media, proxy, markery i pozycje.

Instalację CCX sprawdzono rzeczywistym UPIA (kod 0 i lista z włączoną wersją 0.1.15). Po aktualizacji zapisz pracę i uruchom ponownie Premiere, aby wczytać nowy kod panelu. Na tej stacji potwierdzono uwierzytelnione połączenie z DIT oraz zapis projektów „Dzień 2” i „Dzień 3” z pojedynczymi sekwencjami UHD 50 fps, nazwanymi ścieżkami i wersjonowanymi kopiami. Panel 0.1.15 naprawia porównywanie ścieżek Windows z prefiksem \\?\ zwracanym przez Adobe. W rzeczywistym hoście wykonano przejście między oboma pustymi projektami w obie strony: zapis, zamknięcie i otwarcie. Import nagrań, generowanie i podłączenie proxy oraz synchronizacja wymagają oddzielnej kwalifikacji — same instalacja i utworzenie pustego projektu ich nie potwierdzają. Zadania wideo bez sprawdzonego presetu AME oczekują na jego wskazanie w konfiguracji.

Dokumentacja metod: [Project](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/project), [SequenceEditor](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/sequenceeditor), [EncoderManager](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/encodermanager).

Manifest i operacje markerów: [manifest UXP](https://developer.adobe.com/premiere-pro/uxp/plugins/concepts/manifest/), [Markers](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/markers/).

Dystrybucja: [format CCX](https://developer.adobe.com/premiere-pro/uxp/plugins/distribution/package/), [instalacja i menu UXP](https://developer.adobe.com/premiere-pro/uxp/plugins/distribution/install/). Skrypt `scripts/package-adobe.ps1` tworzy CCX z samych plików runtime, bez kluczy i plików połączenia, z pojedynczą definicją hosta. Dla pracy nad źródłem nadal można użyć UXP Developer Tool.

## Kwalifikacja importu i proxy 11.10.2026

Aktualizacja aplikacji/worker 0.1.19 i panel 0.1.20 dodaje znak wodny Adobe do domyślnego presetu DIT Proxy 720p oraz kontrolę rzeczywistego podłączenia proxy przed i po zapisie. Nowe pliki `_proxy_wm.mov` nie zastępują starych `_proxy.mov`. Własny preset nie jest modyfikowany. Grafikę pobiera się lokalnie z instalacji Adobe. W monitorze Premiere ikonka pojawia się przy włączonym „Toggle Proxies / Przełącz proxy”; do eksportu pełnej jakości pozostają oryginały.

W projekcie Test potwierdzono 68 pełnych proxy z widoczną zmianą obrazu od znaku wodnego oraz 68 poprawnych odnośników proxy w zapisanym PRPROJ. Timeline zachował dokładnie 68 klipów i jedną sekwencję dnia. Stan kolejki Completed, SQLite ok; raport `artifacts/e2e-watermark-0119-result.json`. Poprzednie proxy i rendery próbne pozostały na dysku. Kontrole automatyczne: 194 .NET, 52 panelu Adobe.

Aplikacja/worker 0.1.18 i panel 0.1.19 naprawiają blokowanie kolejnych importów przez pierwsze proxy. W produkcji Test zapisany projekt zawiera 68 importów, 68 klipów na timeline Dnia 1 i 68 zweryfikowanych, przypiętych proxy. Ponowne otwarcie i replay zachowują komplet bez duplikatów i bez ponownego kodowania gotowych plików. Szczegółowy raport `artifacts/e2e-0118-result.json` dotyczy tych plików i tej stacji; nie kwalifikuje niebadanych formatów, wielokanałowego wideo ani synchronizacji nagrań bez danych czasu lub wspólnego dźwięku.


Panel 0.1.21: niepewna synchronizacja jest oznaczona punktowym markerem w źródłowym klipie (początek nagrania), widocznym w instancjach klipu na timeline. Nie tworzy markerów na linijce sekwencji. Podczas ponownej aktualizacji usuwa wyłącznie stare markery sekwencji identyfikujące konkretne nagrania DIT. Marker klipu jest używany także do rozpoznania wcześniejszego stanu przy zmianie pozycji po synchronizacji; pozostałe markery użytkownika pozostają nienaruszone.
