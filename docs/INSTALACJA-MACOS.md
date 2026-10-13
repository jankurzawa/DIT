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

## Komunikat „Apple could not verify DIT”

Obecne wydanie `v0.1.20-macos` nie ma notaryzacji Apple. To ograniczenie podpisu dystrybucyjnego: pozytywny test uruchomienia na runnerze nie oznacza, że Gatekeeper zaakceptuje plik pobrany z internetu. Podpis ad-hoc ani ponowne pobranie tego samego DMG nie usuwają tego ograniczenia.

Jeżeli pobrałeś DIT z właściwego repozytorium i sprawdziłeś jego pochodzenie:

1. Przeciągnij DIT z DMG do **Applications / Aplikacje** i spróbuj uruchomić go z tego folderu.
2. W komunikacie kliknij **Done / Gotowe**.
3. Otwórz **System Settings / Ustawienia systemowe → Privacy & Security / Prywatność i ochrona**.
4. Przewiń do sekcji **Security / Ochrona** i przy informacji o zablokowanym DIT kliknij **Open Anyway / Otwórz mimo to**.
5. Potwierdź **Open / Otwórz** i uwierzytelnij się, jeśli macOS o to poprosi. To zapisuje wyjątek dla DIT; kolejne uruchomienia korzystają z niego.

Nie wyłączaj Gatekeepera dla całego systemu i nie usuwaj masowo atrybutów kwarantanny. Na Macu zarządzanym przez firmę wyjątek może wymagać administratora. Instrukcja Apple: https://support.apple.com/en-gb/102445.

## Wydanie z notaryzacją — dla osoby przygotowującej instalator

Trwałe usunięcie tej blokady wymaga aktywnego Apple Developer Program, certyfikatu **Developer ID Application** z kluczem prywatnym oraz poświadczeń usługi notaryzacji Apple. Nie można zastąpić ich zmianą kodu, podpisem ad-hoc ani kontem GitHub. Nie przesyłaj certyfikatu, kluczy ani haseł w czacie.

Workflow `macOS installer` ma jawny tryb `pilot` (domyślny) lub `notarized`. Dla `notarized` ustaw w **GitHub → repozytorium → Settings → Secrets and variables → Actions** sekrety:

- `MAC_CSC_LINK`: certyfikat `.p12` z kluczem prywatnym, zakodowany base64;
- `MAC_CSC_KEY_PASSWORD`: hasło eksportu `.p12`;
- `MAC_SIGN_IDENTITY`: pełna nazwa certyfikatu `Developer ID Application: … (TEAMID)`;
- `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD`, `APPLE_TEAM_ID`: dane konta i hasło aplikacji do notaryzacji.

Tryb ten podpisuje wszystkie natywne komponenty, zachowuje wymagane uprawnienia JIT Electron i .NET, wymaga hardened runtime, notaryzuje aplikację i DMG oraz dołącza bilety Apple. Brak poświadczeń lub odrzucenie przez Apple przerywa budowanie; nie ma cichego powrotu do podpisu ad-hoc.

`test-distribution-{arch}.json` oddzielnie zapisuje kontrolę DMG, podpis skopiowanej aplikacji i ocenę Gatekeepera po nadaniu kwarantanny pobranego pliku. Dopiero `notarizedGatekeeperAccepted: true`, poprawny bilet Apple i zaliczony tryb `notarized` pozwalają opisać wydanie jako zaakceptowane przez Gatekeepera. Tryb `pilot` zachowuje informację `requiresOpenAnyway: true`, jeśli Apple nie zaakceptowało aplikacji. Sam proces produkcyjnej notaryzacji wymaga jeszcze rzeczywistego uruchomienia z poświadczeniami Apple.
