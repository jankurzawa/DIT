# Specyfikacja programu DIT

Wersja 4 — 10 października 2026 r. Skonsolidowane ustalenia z rozmowy, łącznie z układem folderów przedstawionym na trzech zdjęciach użytkownika i planem przekazanym do implementacji. Dokument opisuje docelowy program. Rzeczywisty stan kodu i kwalifikacji opisuje STATUS.md.

## 1. Cel i zakres

Program zabezpiecza materiał z planu filmowego i przygotowuje go do montażu: zgrywa karty, weryfikuje kopie, wykonuje backup, opcjonalnie czyści karty, tworzy proxy, organizuje materiał, synchronizuje wideo i audio oraz zapisuje projekt Premiere Pro produkcji z osobnymi sekwencjami dni.

- Jedna stacja Windows, indywidualne konta, lokalna praca bez chmury.
- Około 15 kamer, 15–20 mikroportów oraz rejestratory; 40–60 godzin materiału dziennie łącznie, nie na kamerę.
- Materiał wideo: 4K MOV, 10 bit, log, 25 lub 50 fps. Pierwsza wersja kwalifikuje profile H.264, HEVC i ProRes na rzeczywistych plikach.
- Audio: WAV/BWF/RF64, mono i wielokanałowe, PCM oraz 32-bit float. Obsługiwane są zarówno centralne rejestratory, jak i urządzenia zapisujące własne pliki.
- Zgrywania odbywają się asynchronicznie przez cały dzień. Nie zakładamy jednoczesnego zgrywania wszystkich kamer i rejestratorów.
- Zabezpieczanie danych ma pierwszeństwo przed proxy, analizą i podglądami. Brak sztywnego terminu ukończenia kolejki.
- Po zakończeniu zdjęć program pracuje do ukończenia zadań. Przełączenie lub zablokowanie zakładki i wylogowanie użytkownika nie przerywa przyjętych zadań.
- Poza zakresem pierwszej wersji: kontrola kamer i obrazu na żywo, ocena artystyczna oraz wykonywanie montażu właściwego.

## 2. Projekt i konfiguracja

Projekt aplikacji reprezentuje produkcję i może obejmować wiele dni zdjęciowych. Każda produkcja ma jeden plik `.prproj`, a każdy dzień zdjęciowy osobną sekwencję „Dzień N” wewnątrz tego projektu.

Kreator konfiguracji otwiera się po utworzeniu projektu i zawiera:

1. Nazwę projektu.
2. Liczbę, nazwy i konta zakładek kamer/operatorów oraz ich przypisania do urządzeń.
3. Jedną zakładkę audio, jej użytkowników, liczbę i definicje źródeł/rejestratorów oraz ich kanałów.
4. Uporządkowane ścieżki magazynu głównego i magazynu backupowego.
5. Folder proxy.
6. Folder projektów Premiere oraz opcjonalny szablon projektu.
7. Ustawienia timeline’u: domyślnie UHD 3840×2160, 50 fps.
8. Profil proxy: domyślnie ProRes Proxy MOV, do 1280×720, z zachowaniem proporcji.
9. Strefę czasową i profile interpretacji zegarów urządzeń; domyślnie Europe/Warsaw.
10. Przełącznik kompresji backupu.
11. Przełącznik automatycznego czyszczenia kart po zabezpieczeniu danych.
12. Profile rozpoznawania numeracji nagrań i źródeł audio.

Wszystkie ustawienia może zmieniać administrator. Operator nie zmienia konfiguracji projektu. Opcja czyszczenia jest ustawieniem projektu, a nie ręcznym poleceniem administratora dla każdej karty. Domyślnie jest wyłączona; po jej włączeniu działa automatycznie.

Rozpoczęte zgrywanie zachowuje wersję parametrów wpływających na jego lokalizacje i sposób backupu. Wyłączenie czyszczenia oraz globalne wstrzymanie natychmiast blokuje jeszcze nierozpoczęte usuwania, także dla starszych zadań. Ponowne włączenie czyszczenia nie uruchamia go wstecz dla zgrywań rozpoczętych z wyłączoną opcją.

## 3. Zakładki i przypisywanie materiału

Główny widok ma zakładki przypominające przeglądarkę: administrator, zakładki operatorów/kamer oraz dokładnie jedną zakładkę **Audio**. Użytkownicy logują się we właściwych zakładkach.

### 3.1. Kontekst zakładki

- Administrator przypisuje do zakładki kamery/operatora urządzenie i właściciela nagrań.
- Zgrywanie dziedziczy te przypisania z zakładki. Operator wskazuje wyłącznie źródłowy nośnik/folder i uruchamia zgrywanie; nie wybiera ponownie urządzenia ani właściciela materiału.
- Osoba wykonująca zgrywanie i właściciel nagrań są odrębnymi informacjami w historii. Uprawnienia do podglądu wynikają z właściciela i roli.
- Kamera i operator są odrębnymi obiektami. Zmiana przypisania przez administratora dotyczy nowych zgrywań i nie przepisuje historii.
- Karta nie ma trwałego przypisania do urządzenia. Tożsamość nośnika i spis zawartości są przechowywane dla konkretnej operacji, wyłącznie dla poprawnego kopiowania, wznowień i bezpiecznego usuwania.
- Przy sprzeczności dostępnych metadanych urządzenia z kontekstem zakładki powstaje alert. Program nie przypisuje po cichu materiału do innego urządzenia. Wyjaśnienie należy do administratora.

### 3.2. Jedna zakładka Audio

- Administrator definiuje w niej źródła, rejestratory, właścicieli oraz liczbę, nazwy i mapowanie kanałów.
- Liczba mikroportów nie jest automatycznie utożsamiana z liczbą rejestratorów lub plików.
- Wielokanałowy plik jednego rejestratora zachowuje strukturę kanałów. Niezależnie zapisujący nadajnik jest osobnym źródłem.
- Przypisanie plików do zdefiniowanych źródeł następuje według skonfigurowanych identyfikatorów w metadanych, wzorców nazw lub przypisanych wejść/czytników. Nie jest to przypisanie konkretnej karty do kamery.
- Operator nie wybiera ręcznie właściciela i urządzenia dla każdej partii audio. Brak jednoznacznego mapowania zgłaszany jest administratorowi; program nie zgaduje źródła.
- Jedna zakładka Audio obsługuje wiele źródeł i zgrywań, ale nie łączy ich w jeden anonimowy folder ani jeden wspólny track.

## 4. Konta, blokady i dostęp

- Każda zakładka ma niezależną sesję i uprawnienia egzekwowane również przez proces wykonujący operacje.
- Opuszczenie zakładki rozpoczyna odliczanie 3 sekund. Powrót przed upływem tego czasu anuluje odliczanie. Po co najmniej 3 sekundach powrót wymaga ponownego wpisania hasła.
- Kilka opuszczonych zakładek ma niezależne terminy blokady.
- **Po 5 minutach bez aktywności użytkowników w aplikacji blokowane są wszystkie zakładki**, również aktualnie otwarta.
- Aktywność to interakcje myszy i klawiatury z aplikacją. Zadania w tle, aktualizacje postępu, powiadomienia i samo odtwarzanie materiału nie zerują licznika.
- Blokada po 5 minutach ma pierwszeństwo przed krótszymi odliczaniami. Nie blokuje zadań wykonujących się w tle.
- Przy zablokowaniu zakładki zatrzymujemy odtwarzanie, chowamy obraz, miniatury i prywatne dane oraz unieważniamy dostęp sesji. Nie pozostawiamy ostatniej klatki widocznej pod blokadą.
- Restart aplikacji lub blokada sesji Windows blokuje wszystkie zakładki.
- Operator ma dostęp do własnych nagrań; administrator do wszystkich. Sesja administratora nie nadaje uprawnień innym zakładkom.
- Każdy użytkownik może zmienić własne hasło po podaniu aktualnego. Administrator może zmienić hasło dowolnego użytkownika bez znajomości poprzedniego.
- Zmiana hasła unieważnia istniejące sesje danego użytkownika, bez przerywania jego zadań.
- Hasła są przechowywane jako bezpieczne skróty. Zabezpieczenie dotyczy dostępu przez aplikację; nie szyfruje nagrań na dysku.

## 5. Układ folderów

Wskazana ścieżka jest katalogiem głównym danego projektu na danym woluminie. Nie dodajemy nad nią kolejnego obowiązkowego folderu produkcji. Administrator powinien wskazać osobną ścieżkę dla każdej produkcji.

Układ materiału głównego oraz backupu nieskompresowanego odpowiada zdjęciom:

```text
<wskazana ścieżka>/
├── Dzień 1/
│   ├── Urządzenie 1/
│   │   ├── C0001.MOV
│   │   ├── C0002.MOV
│   │   └── ...
│   ├── Urządzenie 2/
│   │   └── ...
│   ├── Rejestrator 1/
│   │   └── ...
│   └── ...
├── Dzień 2/
│   └── ...
└── _DIT/
    ├── Manifesty/
    ├── Raporty/
    └── Metadane źródłowe/
```

- Nazwy urządzeń ustawia administrator. Nazwa widoczna dla użytkownika jest oddzielona od stałego identyfikatora.
- Nagrania leżą bezpośrednio w folderze urządzenia. Nie dodajemy obowiązkowego poziomu karty lub zgrywania pomiędzy urządzeniem a nagraniami.
- `_DIT` jest wydzielonym katalogiem technicznym. Przechowuje m.in. oryginalne ścieżki, nazwy, strukturę i pliki pomocnicze powiązane z manifestami zgrywań. Nie umieszczamy plików technicznych na źródłowej karcie.
- Treść oryginalnych plików pozostaje niezmieniona. Układ czytelny dla montażu nie oznacza utraty oryginalnej struktury: funkcja odtwarzania rekonstruuje ją na podstawie manifestów i zachowanych metadanych.
- Obsługa profilu wymagającego natywnej struktury folderów lub klipów dzielonych wymaga testu odtworzenia i importu. Nie zatwierdzamy adaptera spłaszczającego strukturę bez tego testu.
- Foldery dni i urządzeń są tworzone również na kolejnych wykorzystanych dyskach. Ten sam względny układ obowiązuje na magazynie głównym i backupowym; konkretne pliki mogą leżeć na różnych woluminach obu magazynów.
- Dla backupu skompresowanego foldery `Dzień / Urządzenie` pozostają takie same, a wewnątrz znajdują się archiwa i indeks oryginalnych plików. Odtworzenie daje taki sam układ materiału jak na magazynie głównym.
- Proxy ma analogiczny podział na dni i urządzenia we własnej skonfigurowanej ścieżce.

### 5.1. Kolizje nazw

- Nigdy nie nadpisujemy dwóch różnych plików o tej samej nazwie.
- Drugi różny plik otrzymuje kolejny dopisek, np. `C0001(2).MOV`, następny `C0001(3).MOV`. Identyfikator zgrywania pozostaje w audycie i manifeście. Powiązane pliki otrzymują spójne mapowanie nazw.
- Administrator otrzymuje powiadomienie o kolizji z oryginalnymi nazwami, lokalizacjami i identyfikatorami zgrywań.
- Oryginalne nazwy pozostają w historii, manifestach, metadanych oraz kontroli numeracji. Dopisek nie jest traktowany jako numer nagrania.
- Wspólne mapowanie nazw obowiązuje na magazynie głównym, backupie i w referencjach projektu. Nie modyfikujemy oryginalnej zawartości plików towarzyszących; manifest pozwala odtworzyć ich oryginalne relacje i strukturę.
- Identyczny rozmiar lub nazwa nie wystarcza do uznania pliku za duplikat. Ponowne zgrywanie rozpoznajemy po zweryfikowanych danych i kontekście, nie tylko po nazwie.

## 6. Zapis na kilku dyskach i weryfikacja

- Magazyny są uporządkowanymi listami, np. główny `E → F`, backup `G → H`.
- Zapisujemy na bieżącym dysku; gdy nie pomieści następnego kompletnego pliku lub grupy tworzącej klip, przechodzimy na kolejny. Nie rozcinamy pojedynczych plików ani archiwów.
- Pliki niezbędne do obsługi jednego logicznego klipu pozostają na tym samym woluminie. Ich oryginalne relacje przechowuje manifest.
- Istniejących plików nie przemieszczamy automatycznie. Dla równoległych operacji rezerwujemy przestrzeń, uwzględniając pliki robocze i narzut archiwizacji.
- Rozpoznajemy woluminy niezależnie od liter dysków. Magazyny główny i backupowy muszą być fizycznie niezależne; dwie partycje jednego dysku nie wystarczają.
- Źródło, magazyn główny i backup nie mogą wskazywać tego samego nośnika lub tych samych danych przez aliasy ścieżek.
- SHA-256 obliczamy podczas odczytu źródła. Kopię główną ponownie odczytujemy z dysku i porównujemy z manifestem.
- Backup powstaje ze zweryfikowanej kopii głównej i przechodzi niezależny odczyt kontrolny. Sam zgodny rozmiar plików nie wystarcza.
- Pliki robocze nie są oznaczane jako ukończone. Po awarii rozpoznajemy ukończone etapy, a nieukończone pliki kopiujemy ponownie.
- Manifesty i spójne kopie bazy są zabezpieczane na obu magazynach. Nie kopiujemy otwartego pliku SQLite zwykłą operacją plikową jako rzekomo spójnego backupu.

### 6.1. Kompresja backupu i odtwarzanie

Kompresja oznacza bezstratną archiwizację oryginalnych bajtów, bez ponownego kodowania wideo lub audio. Stosujemy niezależne archiwa 7z/LZMA2 w szybkim ustawieniu, bez kompresji solid między plikami. Zachowujemy nazwy, komplet wpisów i mapowanie do oryginalnych ścieżek.

Po zapisaniu archiwum odczytujemy je z dysku backupowego, rozpakowujemy wpisy strumieniowo i porównujemy SHA-256 oraz kompletność ścieżek z manifestem. Sam test CRC lub prawidłowe zamknięcie procesu archiwizatora nie zatwierdza kopii.

Nie zakładamy oszczędności miejsca na podstawie rozszerzenia. Pokazujemy rzeczywisty rozmiar i współczynnik kompresji. Funkcja odtwarzania rekonstruuje materiał oraz pierwotną strukturę źródła i ponownie weryfikuje sumy. Indeks backupu jest czytelny również bez działającej głównej bazy aplikacji.

## 7. Czyszczenie kart i reakcja na awarie

Przy włączonym ustawieniu projektu karta jest czyszczona automatycznie po zweryfikowaniu kopii głównej i backupu oraz zabezpieczeniu manifestów. Nie czekamy na proxy, synchronizację lub zapis Premiere.

Usuwanie wymaga potwierdzonej tożsamości i niezmienionej zawartości źródła, wyłącznego dostępu oraz potwierdzonej niezależności magazynów. Obejmuje tylko pliki objęte zakończonym zgrywaniem; program nie formatuje karty. Podmiana nośnika, dodatkowe lub zmienione pliki, brak miejsca albo nieukończona weryfikacja blokują usuwanie.

**Luka w numeracji i sama kolizja nazwy powodują alert, ale nie blokują czyszczenia**, jeżeli oba rzeczywiste pliki zostały poprawnie zabezpieczone i spełnione są wszystkie warunki bezpieczeństwa. Ta polityka została wybrana przez użytkownika. Powiadomienie nie jest dowodem ręcznego usunięcia nagrania.

Istotny błąd wykonania zatrzymuje rozpoczynanie kolejnych etapów w całym procesie i wszelkie oczekujące usuwania. Bieżące zapisy doprowadzamy do bezpiecznego punktu. Wznowienie wymaga reakcji administratora; nie udajemy ukończenia zadania po błędzie. Oczekiwanie na uruchomienie Adobe jest osobnym stanem, nie potwierdzeniem awarii danych.

## 8. Kontrola numeracji nagrań

- Dla każdego skonfigurowanego urządzenia rozpoznajemy numer nagrania w **oryginalnej** nazwie według profilu producenta lub wzorca administratora.
- Kontrolujemy oryginalne nagrania, nie liczymy proxy i plików towarzyszących jako kolejnych ujęć. Fragmenty jednego nagrania są obsługiwane zgodnie z profilem urządzenia.
- Zakres kontroli wynika z urządzenia i serii/licznika, nie z tożsamości karty lub kolejności oddawania kart. Uwzględniamy kolejne dni oraz partie zgrywane poza kolejnością.
- Przykład: po zaimportowaniu `C0001`, `C0002` i `C0004` powstaje ostrzeżenie o brakującym `C0003`, widoczne w zakładce administratora.
- Alert zawiera urządzenie, serię, brakujące numery, znane sąsiednie pliki, dzień lub dni i zgrywania, w których wykryto lukę. Kolejne obserwacje aktualizują ten sam alert zamiast tworzyć lawinę duplikatów.
- Gdy brakujący materiał zostanie zgrany później, luka zostaje automatycznie oznaczona jako uzupełniona, z zachowaniem historii i powiadomieniem administratora.
- Przy zamykaniu dnia pokazujemy nierozwiązane luki. Administrator może je wyjaśnić i odnotować przyczynę; wyjaśnienie nie kasuje historii.
- Reset, przepełnienie licznika, różne serie lub prefixy nie mogą być bezwarunkowo interpretowane jako usunięcie. Nie wnioskujemy o resecie tylko dlatego, że później oddana karta ma niższe numery.
- Dla nieznanego wzorca zgłaszamy administratorowi brak możliwości kontroli i potrzebę konfiguracji. Nie wybieramy arbitralnie dowolnej liczby z nazwy zawierającej np. datę i numer kamery.
- Bez ustalonego numeru początkowego nie zgłaszamy braków poprzedzających pierwszy zaobserwowany plik. Numer początkowy może wynikać z wcześniejszej historii lub konfiguracji administratora.
- Nie da się wykryć nigdy niezaobserwowanego końcowego nagrania wyłącznie na podstawie wcześniejszych nazw. Pełna kontrola takiego przypadku wymaga znanego oczekiwanego zakresu lub danych z urządzenia.
- Komunikat brzmi „Brakujące numery nagrań — do wyjaśnienia”, a nie „Operator usunął nagranie”. Ręczne usuwanie przez operatora jest zabronione organizacyjnie; sama luka nie ustala przyczyny.

## 9. Dni zdjęciowe i spóźnione zgrywania

- Administrator zawsze ręcznie otwiera i zamyka dzień. Godzina zgrywania lub ukończenia zadania nie wyznacza dnia nagrania.
- Uwzględniamy metadane, strefę czasową, korekty zegarów i granice dni przechodzących przez północ.
- Jedna karta może zawierać pliki z kilku dni. Przypisanie i docelowy folder dnia ustalamy dla nagrań, nie dla całej karty.
- Zgrywanie porannego materiału z poprzedniego dnia od razu wykonuje kopię i backup oraz zgłasza administratorowi spóźniony materiał.
- Aktualizacja sekwencji zamkniętego dnia czeka na zatwierdzenie administratora. Po zatwierdzeniu zabezpieczamy poprzedni `.prproj`, uzupełniamy sekwencję właściwego dnia i zapisujemy wspólny projekt produkcji.
- Gdy data nie pasuje do żadnego znanego dnia lub jej brakuje, materiał trafia do dnia zapamiętanego przy przyjęciu zgrywania (bieżącego, a gdy wszystkie są zamknięte — ostatniego). Powstaje ostrzeżenie, bez rutynowego stanu „Do przypisania”. Właściciel i kamera zawsze wynikają z zakładki. Zmiana dnia podczas zgrywania nie zmienia tej decyzji.
- Zamknięcie dnia nie usuwa przyjętych zadań. Mogą się zakończyć po północy lub następnego dnia.
- Nie aktualizujemy pliku równocześnie używanego przez montażystę. Przekazanie do montażu kończy zwykłe automatyczne aktualizacje; spóźnione zmiany wymagają koordynacji administratora.

## 10. Proxy, synchronizacja i timeline

- Proxy ma do 1280×720 z zachowaniem proporcji, FPS, czasu trwania i kanałów źródła. Bez automatycznego wypalania LUT-u. Administrator może wskazać sprawdzony preset AME.
- Potwierdzamy zakończenie AME oraz poprawność i zgodność wynikowego pliku przed przypięciem proxy do oryginału. Samo przyjęcie zadania do kolejki nie oznacza sukcesu.
- Wideo i audio przechodzą te same etapy ustalania czasu: odczyt timecode’u/LTC, sprawdzenie spójności, dopasowanie wspólnego dźwięku oraz wykorzystanie metadanych godziny nagrania jako przybliżenia.
- Brakujący timecode wyliczamy z najlepszych dostępnych danych, z uwzględnieniem strefy i korekt zegara. Zapisujemy go w aplikacji, plikach towarzyszących i projekcie Premiere, bez zmiany oryginałów.
- Zachowujemy metodę, offset, stopień pewności i historię ręcznych korekt administratora. Arytmetyka czasu nie opiera się na narastających błędach zmiennoprzecinkowych.
- Domyślny timeline: **UHD 3840×2160, 50 fps**. Materiał 25/50 fps zachowuje rzeczywisty czas trwania, bez automatycznego slow motion.
- Każda kamera ma własny nazwany track wideo i przypisany tor jej audio. Każdy rejestrator lub niezależnie zapisujące źródło ma własny nazwany track audio, mimo jednej wspólnej zakładki Audio w aplikacji.
- Wielokanałowe nagranie zachowuje kanały w reprezentacji adaptacyjnej; nie jest automatycznie mieszane do stereo. Możliwość uzyskania takiego układu przez integrację musi być potwierdzona przed kwalifikacją wersji Adobe.
- Poprawnie zsynchronizowane klipy zachowują pozycje i przerwy. Klipy ustawione tylko przybliżenie mają wyraźne oznaczenie braku potwierdzonej synchronizacji.
- Klipy bez możliwej do ustalenia pozycji trafiają na **główny timeline**, kolejno, bez nakładania, na track swojego urządzenia, za istniejącą zawartością. Mają żółtą etykietę i marker „DO SYNCHRONIZACJI”; ich sztuczna pozycja nie oznacza godziny nagrania.
- Przy nadejściu nowych poprawnie zsynchronizowanych klipów wolno przesunąć żółte klipy dalej. Nie przesuwamy poprawnie ustawionych nagrań.
- Kolizja wyliczonych przedziałów dwóch różnych nagrań tego samego urządzenia powoduje alert i wstrzymanie procesu przed nadpisaniem lub przycięciem. Nagrania różnych urządzeń oraz kanały tego samego nagrania wielokanałowego mogą prawidłowo pokrywać się czasowo.
- Sprawdzamy aktualny stan sekwencji przed zastosowaniem zmian. Powtórzenie operacji po awarii nie dodaje tych samych klipów ponownie.
- Po każdej zatwierdzonej partii zapisujemy `.prproj`, potwierdzamy wynik oraz tworzymy wersjonowaną kopię zapasową.

## 11. Podgląd i statystyki

Operator widzi i odtwarza własne nagrania, administrator wszystkie. Dostępne są miniatury, metadane, odtwarzanie, przewijanie i odsłuch audio. Dane nie są ujawniane w zablokowanych zakładkach.

Panel administratora zaczyna się od kafelków głównych sekcji. W widoku materiału nagrania, transfery i odtwarzacz są stale widoczne obok siebie, z możliwością zmiany szerokości sekcji. Administrator widzi właściciela nagrania. Transfery pokazują pasek postępu kopii głównej i backupu; zabezpieczenie kopii i ukończenie Adobe mają odrębne opisy. Wybór nagrania automatycznie pokazuje pierwszą klatkę. Odtwarzacz ma play/pauzę, poprzednie/następne nagranie, podgląd podczas przewijania i prędkości 1×–4×. Każdy przycisk ma podpowiedź pod przyciskiem. Główne wylogowanie nosi nazwę „Wyloguj ze wszystkich kart”.

Konfiguracja zakładek i źródeł audio używa osobnych sekcji formularza. Plus pod listą dodaje sekcję, usunięcie sekcji nie usuwa historycznych nagrań. Edycja zachowuje identyfikatory istniejących kont, urządzeń i źródeł.

Najpierw odtwarzamy gotowe proxy. Gdy go nie ma, korzystamy ze zweryfikowanego oryginału na dysku, z dekodowaniem sprzętowym, jeśli jest dostępne. Dla zbyt ciężkich plików tworzymy na żądanie lekki podgląd H.264 do 720p, z niższym priorytetem niż zabezpieczanie danych. Aktywny jest jeden odtwarzacz; nie dekodujemy materiału w nieaktywnych zakładkach.

Statystyki administratora są filtrowane według produkcji, dnia, urządzenia i operatora:

- liczba nagrań i zgrywań, czas materiału danej kamery lub źródła;
- rozmiar oryginałów, backupów, proxy i podglądów oraz rzeczywisty efekt kompresji;
- zajęta, wolna i zarezerwowana przestrzeń każdego dysku;
- szybkość i czas etapów, długość kolejki i statusy zadań;
- braki numeracji, kolizje nazw, spóźnione zgrywania, błędy i niepotwierdzone synchronizacje.

Ponowne zgrywanie nie zwiększa liczby unikalnych nagrań. Długości kanałów tego samego nagrania audio nie sumujemy wielokrotnie jako czasu pracy rejestratora. Nieznany czas nie jest przedstawiany jako zero.

Historia obejmuje osoby, źródła, konfiguracje, lokalizacje, sumy kontrolne, etapy, alarmy, korekty, zatwierdzenia, reset haseł, zapis projektu oraz usuwanie z karty. Hasła nie trafiają do historii. Eksport raportów CSV/JSON i manifestów SHA-256. Powiadomienia dla administratora są trwałe w jego panelu; nie wysyłamy wiadomości zewnętrznych bez osobnej konfiguracji i autoryzacji.

## 12. Architektura i interfejsy

- WPF odpowiada za zakładki, prezentację, logowanie i odtwarzanie. Proces wykonawczy odpowiada za kolejkę, uprawnienia, manifesty i operacje na danych.
- Dane aplikacji rozdzielają: `Project`, `ShootingDay`, `TabProfile`, `User`, `Device`, `AudioSource`, `IngestBatch`, `CardSnapshot`, `Asset`, `StoragePool`, `Replica`, `SyncResult`, `Alert` i `AuditEvent`.
- Każdy asset zachowuje kontekst zakładki i właściciela, oryginalną nazwę i ścieżkę, logiczne urządzenie, dzień oraz faktyczne lokalizacje kopii. Nagranie i plik fizyczny nie są utożsamiane w przypadku klipów dzielonych lub wielokanałowych.
- Oddzielne statusy opisują zabezpieczenie danych, możliwość usunięcia źródła, proxy, synchronizację i aktualizację Premiere. Nie używamy jednego ogólnego statusu „gotowe” dla całego procesu.
- Interfejsy zegara, magazynu, backupu, synchronizacji i Premiere są wymienne dla testów. Uprawnienia i bezpieczeństwo usuwania pozostają we wspólnej logice produkcyjnej i testowej.
- Dodatek UXP komunikuje się przez uwierzytelniony interfejs dostępny wyłącznie lokalnie. Nie wykonuje dowolnego kodu otrzymanego przez API. Komendy mają identyfikatory pozwalające bezpiecznie rozpoznać ponowienie.
- Operacje Adobe są serializowane i przypisane do konkretnego projektu, dnia i sekwencji; nie działają na przypadkowo aktywnym projekcie.
- Baza lokalna SQLite przechowuje trwały stan kolejki i zdarzenia. Raporty oraz manifesty umożliwiają odtworzenie informacji o zabezpieczonym materiale.

## 13. Testy i kryteria odbioru

Testy są częścią implementacji wszystkich reguł i interfejsów. Poniższa macierz jest minimalnym zakresem, nie wykazem już wykonanych testów.

| Obszar | Jednostkowe | Integracyjne / E2E |
| --- | --- | --- |
| Konfiguracja i kontekst zakładki | Dziedziczenie urządzenia i właściciela; wersje konfiguracji | Zgrywanie bez dodatkowych selektorów; odrzucony dostęp obcej sesji |
| Blokada 3 sekund | Granice czasu, szybki powrót, wiele timerów | Uprawnienia po wygaśnięciu; przyjęte zadanie nadal pracuje |
| Blokada 5 minut | Dokładna granica, aktywność różnych użytkowników; brak resetu przez zadania i playback | Wszystkie sesje wygasają, obraz i odsłuch znikają; kopiowanie trwa |
| Hasła i role | Zmiana własna, reset admina, unieważnienie sesji | Izolacja podglądu i API; brak haseł w logach |
| Audio | Jedna zakładka, wiele źródeł i kanałów, niejednoznaczne mapowanie | Wielokanałowy WAV i osobne rejestratory na właściwych folderach i trackach |
| Obrót kart | Brak trwałego powiązania karty; rozróżnienie snapshotów | Ta sama karta w różnych kamerach i dniach; podmiana podczas operacji |
| Układ folderów | Dzień / urządzenie / pliki, nazwy, brak poziomu karty | Rzeczywisty zapis i odtworzenie oryginalnej struktury, również po rozdzieleniu woluminów |
| Kolizje nazw | Unikalny dopisek, oryginalna numeracja, rozróżnienie duplikatu | Dwa różne pliki zachowane w obu magazynach; alert i poprawne referencje Premiere |
| Kilka dysków | Kolejność, rezerwacje, całe pliki/grupy, brak miejsca | E→F i G→H; zmiana litery, odłączenie i ponowne podłączenie |
| Kopiowanie | Dozwolone przejścia statusów, wznowienia | Rzeczywiste SHA-256; zmieniony bajt, przerwany plik, awaria procesu |
| Kompresja | Mapowanie wpisów, kompletność, błędy | Archiwizacja, pełny odczyt i odtwarzanie; uszkodzone lub brakujące wpisy |
| Usuwanie | Każdy warunek dopuszczenia i blokady | Tylko nośniki testowe; awaria, podmiana, nowe pliki, zmiana konfiguracji; luka numeracji nie blokuje poprawnie zabezpieczonej karty |
| Numeracja | C0001/2/4; późniejsze C0003; prefixy, reset, wrap, kolejność, nieznany wzorzec | Alert bez duplikatów; aktualizacja po spóźnionej karcie; brak fałszywego oskarżenia |
| Dni | Ręczne granice, północ, zegary i strefy | Karta z kilku dni; następny poranek; oczekiwanie na admina przed aktualizacją sekwencji zamkniętego dnia |
| Synchronizacja | Rational time, offsety, pewność, kolizje, ułożenie żółtych klipów | Znany wzorzec TC/LTC/audio; brak czasu; 25/50 fps; rzeczywisty układ i kolory w Premiere |
| Proxy i podgląd | Wybór pliku, dostęp, priorytet, zwalnianie odtwarzacza | MOV 10-bit, proxy, lekki podgląd i blokada bez pozostawionej klatki |
| Statystyki i historia | Brak podwójnego liczenia, kanały, nieznane wartości | Wyniki zgodne z manifestami, plikami i eksportami |
| Adobe | Idempotencja komend, wybór projektu, rozróżnienie kolejki i ukończenia | Import, AME, przypięcie proxy, tracki, kolory, zapis i ponowne otwarcie `.prproj` |

### 13.1. Wstrzykiwanie awarii i testy całościowe

- Testujemy awarię na każdej granicy etapów, szczególnie pomiędzy potwierdzeniem kopii a usuwaniem źródła. Uwzględniamy awarię po częściowym usunięciu i bezpieczne wznowienie bez dotknięcia nowych plików.
- Testy właściwości generują różne kolejności zadań, restartów, odłączania nośników i zmian konfiguracji. Podstawowy niezmiennik: brak usunięcia pliku bez kompletu potwierdzonych kopii tego samego pliku i właściwego źródła.
- E2E bez UI wykonują prawdziwe kopiowanie, archiwizację, odtwarzanie, analizę, logikę kolejki i raportowanie w katalogach/nośnikach testowych. Adapter testowy Adobe nie jest przedstawiany jako test rzeczywistego Premiere.
- E2E z rzeczywistym Premiere/AME są sterowane programowo, bez klikania interfejsu, ale wymagają uruchomionych aplikacji Adobe i ich dodatku. Nie opisujemy ich jako w pełni bezgłowych.
- Testy WPF obejmują faktyczne ukrycie odtwarzacza, obrazów i prywatnych danych. Reguły timerów są dodatkowo weryfikowane bez UI na kontrolowanym zegarze.
- Próba całodniowa obejmuje asynchroniczne oddawanie kart przez różne osoby, wiele zgrywań jednego operatora, obrót kart, 40–60 godzin materiału, uzupełnienie brakujących numerów i spóźniony materiał następnego dnia.

### 13.2. Kolejność realizacji i dopuszczenie

1. Próba integracji Adobe: import, 15 nazwanych ścieżek kamer, rejestratory wielokanałowe, AME, proxy, żółte etykiety, ustawianie czasu źródłowego, zapis i ponowne otwarcie. Sprawdzamy niezmienność SHA-256 oryginałów.
2. Implementacja i testy rdzenia zabezpieczania: magazyny, sumy, backup, odtwarzanie, manifesty, wznowienia i czyszczenie na danych testowych.
3. Implementacja synchronizacji, numeracji i integracji Premiere wraz z testami błędów oraz ponowień.
4. Implementacja zakładek, blokad, kont, zakładki Audio, podglądu, historii i statystyk.
5. Testy całego cyklu i pilotaż na rzeczywistym planie z wyłączonym czyszczeniem kart.

Warunkiem wydania jest odtwarzalny backup, brak usuwania źródła bez kompletu zweryfikowanych kopii, poprawne egzekwowanie dostępu i brak cichego nadpisywania nagrań lub timeline’u. Modele sprzętu, kodeki i wydajność stacji kwalifikujemy na rzeczywistym zestawie; nie obiecujemy czasu ukończenia bez pomiaru.

## 14. Źródła researchu

- [ScreenSkills — obowiązki DIT-a](https://www.screenskills.com/job-profiles/browse/film-and-tv-drama/technical/digital-imaging-technician-film-and-tv-drama/) i [data wranglera](https://www.screenskills.com/job-profiles/roles/data-wrangler/).
- [Adobe — EncoderManager, kodowanie i zdarzenia AME](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/encodermanager).
- [Adobe — ClipProjectItem, proxy i etykiety](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/clipprojectitem).
- [Adobe — czas źródła](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/media), [VideoTrack](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/videotrack) i [AudioTrack](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/audiotrack).
- [Adobe — synchronizacja](https://helpx.adobe.com/in/premiere/desktop/add-audio-effects/basic-audio-editing/synchronize-clips-in-the-timeline-panel.html) i [kanały audio](https://helpx.adobe.com/premiere/desktop/add-audio-effects/advanced-audio-techniques/audio-channel-mapping.html).
- [ExifTool — interpretacja dat QuickTime i problem czasu lokalnego/UTC](https://exiftool.org/TagNames/QuickTime.html).
- [Sony — numeracja nagrań, reset i przepełnienie licznika](https://helpguide.sony.net/dsc/1920/v1/en/contents/TP0002392798.html).
- [Canon — numeracja filmów i reset po zmianie karty](https://cam.start.canon/en/C021/manual/html/UG-08_Set-up_0050.html).
- [VideoLAN — LibVLCSharp i osadzanie odtwarzacza WPF](https://docs.videolan.me/libvlcsharp/docs/getting_started.html).
- [7-Zip — format 7z i LZMA2](https://7-zip.org/7z.html).
- [Microsoft — wsparcie .NET 10 LTS](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
