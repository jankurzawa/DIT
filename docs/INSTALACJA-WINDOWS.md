# DIT — instalacja na innym komputerze Windows

1. Skopiuj plik `DIT-Setup-0.1.20-win-x64.exe` na drugi komputer i uruchom go. Instalator jest dla Windows 10 (od kompilacji 19041) i Windows 11 w wersji x64. DIT instaluje się dla bieżącego użytkownika, bez wymagania uprawnień administratora.
2. Zostaw skrót na pulpicie. Jeśli instalator wykryje Creative Cloud, może również zainstalować panel DIT do Premiere. Zamknij wcześniej Premiere. Aplikacje Adobe nie są częścią tego instalatora.
3. Otwórz DIT ze skrótu i utwórz nowy projekt. Ustaw foldery materiału, backupu, proxy i projektu Premiere oraz nazwy kamer i źródeł audio. Domyślny preset DIT Proxy 720p ze znakiem wodnym jest już dołączony.
4. Do montażu i proxy wymagane są Premiere Pro zgodne z panelem (co najmniej 26.5), Media Encoder oraz Creative Cloud. W razie potrzeby włącz tryb programisty dodatków UXP w preferencjach Premiere.
5. W DIT wybierz Admin → Premiere i proxy → Skonfiguruj / otwórz Adobe. Otwórz w Premiere Okno → UXP Plugins → DIT. Jednorazowo wybierz przygotowany plik DIT.ditconnection. Użyj pliku utworzonego na tym komputerze. Połączenie jest potem zapamiętywane.
6. Otwórz dzień zdjęciowy w DIT i zgraj materiał. Produkcja otrzyma jeden projekt Premiere, a dni osobne sekwencje. Postęp proxy będzie widoczny w DIT i kolejce Media Encodera.

Instalator zawiera środowisko .NET, LibVLC, FFmpeg/ffprobe, 7zr, preset proxy, panel Adobe i narzędzie odtwarzania kopii. Nie wymaga instalowania SDK .NET, Node.js ani VLC. Instalacja DIT działa bez pobierania dodatkowych składników; Adobe wymaga własnej instalacji i licencji.

Pakiet nie zawiera projektów, kont, haseł, kluczy połączenia ani nagrań z komputera, na którym został przygotowany. Nowa instalacja tworzy własne dane i klucze w `%LOCALAPPDATA%\DIT`. Istniejące dane DIT na docelowym komputerze są zachowywane.

Przed aktualizacją lub odinstalowaniem zakończ zgrywanie i pracę Adobe. Proces roboczy DIT może działać po zamknięciu okna. Instalator wykrywa zajęte pliki. Odinstalowanie usuwa program i jego skróty; zachowuje bazę, klucze, kopie i materiał użytkownika. Nie usuwa panelu Adobe współdzielonego z Premiere.

Instalator nie jest podpisany certyfikatem wydawcy. Windows może wyświetlić ostrzeżenie SmartScreen. Obok pliku znajduje się suma SHA-256 umożliwiająca sprawdzenie kopii.
