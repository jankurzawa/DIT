# DIT — instalacja na macOS

Wersja macOS jest oddzielną aplikacją. Nie zastępuje programu ani instalatora Windows.

1. Pobierz DMG odpowiedni dla komputera: **arm64** dla Apple Silicon (M1/M2/M3/M4 i nowszych), **x64** dla Maców z Intelem. Wymagany macOS 14 Sonoma lub nowszy.
2. Otwórz obraz DMG i przeciągnij **DIT** do **Applications / Aplikacje**. Wysuń obraz DMG i uruchom DIT z Aplikacji.
3. Ten pakiet ma podpis lokalny ad-hoc; nie ma certyfikatu Apple Developer ID ani notaryzacji. Jeśli Gatekeeper zablokuje pierwsze uruchomienie, skorzystaj z **Ustawienia systemowe → Prywatność i ochrona → Otwórz mimo to**, po sprawdzeniu pochodzenia pliku. Suma SHA-256 jest przy pobranym DMG. Nie trzeba wyłączać Gatekeepera.
4. Utwórz projekt. Wskaż osobne foldery materiału, backupu, proxy i projektów Premiere. Folder backupu musi leżeć na niezależnym fizycznym dysku. Dwie partycje lub dwa woluminy APFS jednego dysku nie stanowią niezależnych kopii.
5. Nazwij kamery i źródła audio (np. Mikroport 1, Mikroport 2). Projekt ma jedną zakładkę Audio; obok ścieżki wybierasz aktualnie zgrywane źródło. Można wybierać tylko pliki audio.
6. Zainstaluj osobno Premiere Pro 26.5+, Media Encoder i Creative Cloud. W DIT wybierz **Admin → Premiere i proxy → Skonfiguruj / otwórz Adobe**. Potwierdź instalację CCX w Creative Cloud. W Premiere otwórz panel DIT i wybierz pokazany w Finderze plik `DIT.ditconnection`. W razie potrzeby włącz tryb programisty dodatków UXP. Połączenie jest zapamiętywane; nie kopiuj pliku połączenia z Windows.
7. Otwórz dzień zdjęciowy i zgraj materiał. Jedna produkcja DIT ma jeden projekt Premiere, a dni osobne sekwencje. Dołączony jest domyślny preset proxy 720p; znak wodny pochodzi z lokalnej instalacji Adobe. Zapis i attach proxies wykonuje wspólny panel DIT.

Instalator zawiera własne środowisko .NET, FFmpeg, ffprobe, 7zz i ich biblioteki. Na docelowym Macu nie potrzeba Homebrew, Node.js ani SDK .NET. Podgląd aplikacji używa osobnego H.264/AAC, ponieważ odtwarzacz nie dekoduje proxy ProRes. Nie zmienia to proxy przekazywanego do Premiere.

Dane, klucze i log workera: `~/Library/Application Support/DIT`. Zamknięcie okna pozostawia kolejkę zgrywania w tle. Przed wylogowaniem z macOS lub aktualizacją zakończ zgrywanie. Usunięcie aplikacji z Aplikacji nie usuwa danych ani materiału.

Automatyczne kasowanie kart na macOS jest wyłączone; wymaga osobnej kwalifikacji rzeczywistych nośników. Magazyny sieciowe, obrazy dysków i APFS z wieloma fizycznymi magazynami (np. Fusion Drive) są odrzucane, gdy nie można potwierdzić niezależności kopii.

Testy runnerów macOS sprawdzają prawdziwy pakiet aplikacji, uruchomienie workera, APFS, renderowanie formularzy, kodowanie wideo i archiwa. Runner nie zawiera licencjonowanego Premiere/AME ani fizycznych kart operatorów, więc integracja Adobe i długi pilotaż na docelowym Macu pozostają do sprawdzenia przed pracą produkcyjną.
