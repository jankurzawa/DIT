# DIT Media Workspace

## Wersja macOS

Oddzielna aplikacja i instalatory DMG dla Apple Silicon oraz Intela znajdują się w `mac/`. Instrukcja: [INSTALACJA-MACOS.md](docs/INSTALACJA-MACOS.md). Budowanie na Macu: `bash mac/build.sh arm64` lub `bash mac/build.sh x64` (SDK .NET 10, Node.js 24, Homebrew `ffmpeg` i `sevenzip` jako narzędzia kompilacji).

Kod WPF, projekty i skrypty Windows pozostają niezależne. `mac/prepare-host.py` generuje w ignorowanym katalogu adaptery workera z istniejących źródeł; zmiana oczekiwanej struktury źródła przerywa generowanie. Mac używa osobnej implementacji identyfikacji dysków APFS, wyszukiwania aplikacji Adobe i podglądu Chromium. Nie zmienia oryginalnych plików. Workflow `macos-installer.yml` buduje i testuje rzeczywisty pakiet na macOS dla obu architektur. Wymagany system docelowy: macOS 14+; pakiety mają podpis ad-hoc, bez notaryzacji Apple.

## Wersja Windows

Aplikacja Windows: WPF, osobny proces roboczy .NET 10, SQLite i panel UXP dla Premiere Pro. Kod realizuje zabezpieczanie plików, uprawnienia, kolejki, raporty i przygotowanie projektów Adobe. To wersja do pilotażu; kwalifikacja rzeczywistych kamer, nośników i integracji Adobe pozostaje obowiązkowym etapem odbioru.

## Uruchomienie

W PowerShell w katalogu repozytorium:

```powershell
./scripts/bootstrap.ps1
./scripts/run.ps1
```

Wymagania deweloperskie: Windows 10/11 x64, PowerShell 7 i Node.js 22+. Skrypt pobiera SDK oraz narzędzia do `.tools`. Dane aplikacji znajdują się w `%LOCALAPPDATA%\DIT`. Kluczy `local.key` i `adobe.key` nie należy publikować.

Gotowy pakiet tworzy `./scripts/package.ps1`. Uruchom `artifacts/DIT/Dit.Desktop.exe`; SDK i Node nie są wymagane do pracy spakowanej aplikacji. Proces roboczy pozostaje aktywny po zamknięciu interfejsu.

Instalator Windows tworzy `./scripts/build-installer.ps1` (wymaga kompilatora Inno Setup 6.7+; opcjonalnie `-CompilerPath`). Gotowy plik EXE i suma SHA-256 trafiają do `artifacts/installers`. Parametr `-SkipPublish` wykorzystuje istniejący pakiet bieżącej wersji. Instalator zawiera runtime i narzędzia x64, tworzy skróty oraz deinstalator i zachowuje dane użytkownika. Instrukcja: [INSTALACJA-WINDOWS.md](docs/INSTALACJA-WINDOWS.md).

## Pierwszy projekt

1. Utwórz projekt i wskaż niezależne fizyczne dyski główne oraz backupowe. Dwie partycje tego samego dysku nie stanowią osobnych kopii.
2. Każda kamera/operator ma własną sekcję z nazwą i opcjonalnym hasłem. Przycisk „＋ Dodaj zakładkę” dodaje kolejny formularz. Wszystkie hasła są opcjonalne, mogą być puste i nie mają wymagań długości ani składu. Dotyczy to również administratora, Audio i zmiany hasła.
3. Osobne pliki mikroportów konfiguruj jako osobne źródła Audio, zwykle mono (1 kanał). Pole „Nazwa źródła / tracku” określa katalog i ścieżkę w Premiere. W zakładce Audio wskaż folder lub pliki, wybierz źródło z dropdownu obok ścieżki i rozpocznij zgrywanie. Wcześniejsze źródło opisane np. jako „Audio / 20 kanałów” można rozdzielić przyciskiem w konfiguracji. Zgrywanie części karty nie uruchamia jej czyszczenia.
4. Zaloguj się do Admin i ręcznie otwórz dzień zdjęciowy. Kalendarz, godzina i lista stref są domyślnie ustawione na bieżącą datę i czas w strefie projektu. Wybrany dzień pokazuje podsumowanie materiału i zgrywań.
5. Operator loguje się do własnej zakładki, wskazuje kartę i uruchamia zgrywanie. Właściciel wynika z zakładki.
6. Podłącz panel Adobe zgodnie z [instrukcją](docs/ADOBE.md). Samo skopiowanie plików nie oznacza ukończenia proxy ani zapisu Premiere.

Przez pierwsze próby pozostaw automatyczne czyszczenie wyłączone. Profile `clearQualified` domyślnie są wyłączone. Ustawienie wymaga potwierdzenia odtworzenia/importu konkretnego formatu. W tej wersji czyszczenie wymaga całego źródłowego nośnika oznaczonego przez Windows jako wymienny. Czytnik zgłaszany jako dysk stały jest blokowany.

## Zaimplementowane zachowania

- Oddzielne zakładki Admin, kamer/operatorów i jedna Audio; hasła i uprawnienia po stronie procesu roboczego; blokady po 3 sekundach i po 5 minutach bezczynności.
- Postęp uruchamiania z inicjalizacją podglądu poza wątkiem UI, jedna instancja interfejsu i własna ikona DIT.
- Panel administratora otwiera się na kafelkach. Osobne sekcje zawierają nagrania/zgrywania, statystyki/dyski, alerty, historię, dni, konfigurację, Adobe i eksport raportów. Konfiguracja zajmuje główny widok; zmiany haseł są przy kontach, bez oddzielnej sekcji. Powrót jest po lewej; przyciski myszy Wstecz/Dalej przechodzą przez odwiedzone widoki administratora.
- Nagrania, zgrywania z paskami postępu oraz odtwarzacz są widoczne obok siebie. Admin widzi właściciela każdego nagrania; operator widzi własny materiał.
- Każde nagranie ma potwierdzenia zgrania, backupu, proxy, zapisu w Premiere i synchronizacji. Wynik przybliżony nie jest oznaczany jako potwierdzona synchronizacja; proxy nie dotyczy samych plików audio.
- Źródło i folder główny mogą być na jednym dysku w oddzielnych, niepokrywających się katalogach. Backup musi być na osobnym fizycznym dysku. Współdzielenie dysku przez źródło i kopię blokuje automatyczne czyszczenie źródła.
- Dwa równoległe zgrywania, trwała kolejka, SHA-256 źródła i odczytu kopii, uporządkowane magazyny wielodyskowe, bez nadpisywania.
- Układ `Dzień N / Urządzenie / pliki`; kolizje `(2)`, `(3)`; manifesty oryginalnej struktury, raporty i spójne kopie SQLite.
- Bezstratne archiwa 7z, pełna kontrola wpisów i SHA-256 po rozpakowaniu; odtwarzanie również bez głównej bazy.
- Braki numeracji, późne karty, ręczne zamknięcie dnia i zatwierdzanie aktualizacji zamkniętego dnia. Metadane wskazują znany dzień; brak pasującej daty oznacza użycie dnia zapamiętanego przy rozpoczęciu zgrywania i ostrzeżenie, bez blokady organizacji plików.
- Analiza ffprobe, timecode zgodny z zegarem, przybliżenie czasu rejestracji, dopasowanie audio i dekoder LTC 25 fps na skonfigurowanym kanale; historia korekt.
- Podgląd LibVLC: kliknięcie nagrania wczytuje pierwszą klatkę, przewijanie pokazuje bieżącą klatkę również na pauzie. Dostępne są poprzednie/następne nagranie, play/pauza i prędkości 1×, 1,5×, 2×, 3×, 4×. Preferowane są proxy lub istniejące lekkie podglądy; odtwarzanie korzysta wyłącznie ze zweryfikowanych kopii.
- Panel UXP: import do folderów urządzeń, kontrola sekwencji UHD 50 fps, nazwane ścieżki, kolejka AME, weryfikacja proxy, żółte klipy i markery, zapis i kopie projektu.
- Panel Adobe zapamiętuje połączenie w szyfrowanym magazynie UXP i ponawia je po wczytaniu panelu. Każda produkcja DIT ma jeden plik Premiere `.prproj`; otwarcie dnia kolejkuje osobną sekwencję „Dzień N”, także przed pierwszym zgrywaniem. Pierwsze załadowanie panelu i sprawdzenie presetu AME wymagają jednorazowej konfiguracji; patrz instrukcja Adobe.
- Kompaktowe formularze kamer i audio mają akcję usunięcia w jednym wierszu z polami. Nowe hasło wpisujesz bez checkboxu; nietknięte pole zachowuje hasło, wpisanie i wyczyszczenie usuwa je. Hasło administratora zmieniasz przyciskiem „Zmień hasło” przy nagłówku. Przycisk konfiguracji Adobe instaluje dostarczony panel CCX przez instalator Creative Cloud; pierwszy wybór pliku połączenia odbywa się w panelu Premiere.

## Testy i ograniczenia odbioru

```powershell
./scripts/test.ps1
```

Testy .NET obejmują jednostki, rzeczywisty system plików/SQLite, awarie, odtwarzanie, prawdziwe archiwa 7z, generowane MOV/WAV oraz API uruchomionego workera. Testy TypeScript sprawdzają arytmetykę, zastępczy host Adobe, ponawianie importu, AME, konflikty i przesuwanie nierozwiązanej końcówki. Nie są dowodem działania rzeczywistego hosta Adobe ani usuwania z kart.

Szczegółowy stan i procedura odbioru: [STATUS.md](docs/STATUS.md). Nie wykonano testu ciągłego na 40–60 godzinach rzeczywistego materiału ani destrukcyjnych testów prawdziwych kart. Przed użyciem produkcyjnym wymagane są kwalifikacja formatu i test odtwarzania, integracja w Premiere/AME oraz pilotaż na docelowej stacji.

Źródła API: [Adobe UXP](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/), [LibVLCSharp](https://docs.videolan.me/libvlcsharp/), [7-Zip](https://7-zip.org/7z.html), [FFmpeg](https://ffmpeg.org/documentation.html). Biblioteki multimedialne zachowują własne licencje; pakiet FFmpeg z gyan.dev jest GPLv3, LibVLC LGPL, 7zr public domain. Dekoder LTC jest implementacją własną protokołu opisanego w [dokumentacji libltc](https://github.com/x42/libltc/blob/master/src/ltc.h).
