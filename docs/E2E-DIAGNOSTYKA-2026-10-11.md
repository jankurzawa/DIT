# Diagnostyka E2E DIT → Premiere → AME

Data: 11.10.2026. Badana wersja aplikacji, workera i panelu: 0.1.16.

## Wniosek

Kodowanie pierwszego proxy faktycznie zostało wykonane przez AME. Przepływ zatrzymał się między wykonaniem kodowania a potwierdzeniem całego zadania w DIT. Po restarcie Premiere pozostałe zadanie `inflight` blokuje kolejne polecenia. W kodzie są dodatkowo potwierdzone błędy obsługi zdarzeń oraz niezgodność walidacji z użytym presetem. Samo wskazanie pliku .epr nie wystarczy do naprawy E2E.

Nie ustalono z pewnością, które zdarzenie Adobe otrzymał panel po pierwszym kodowaniu: nie ma zapisu jego rzeczywistej treści. Nie należy przedstawiać hipotezy o zgubionym zdarzeniu COMPLETE jako już udowodnionej przyczyny.

## Dowody z rzeczywistej próby

- `C:\Users\Admin\Documents\Adobe\Adobe Media Encoder\26.0\AMEEncodingLog.txt`: 17:46:30 start kolejki, 17:46:32 pomyślne kodowanie `DJI_0357_proxy.mov`. Preset: DIT Proxy 720p; ProRes 422 Proxy, 1280×720, 29,97 kl./s, audio stereo 48 kHz. Log techniczny AME potwierdza wynik 0 i około 2,6 s kodowania.
- Premiere uruchomiono ponownie o 17:49:15. Podczas diagnostyki AME nie pracowało.
- SQLite: pierwsze zadanie Test / Dzień 1 ma stan `inflight`, następne `pending`; oba zgrywania oczekują na Adobe. Żadne ze 136 nagrań nie ma przypisanego zweryfikowanego proxy.
- Preset w konfiguracji obu produkcji istnieje i jest wskazany prawidłowo. Zachowany w pierwszym poleceniu komunikat o brakującym presecie pochodzi z wcześniejszej próby; przejście do `inflight` nie czyści starego błędu.
- Podczas oględzin proxy i plik `Test.prproj` już nie istniały. Użytkownik potwierdził ręczne usunięcie plików. Ten brak nie dowodzi błędu usuwania w aplikacji i nie wyjaśnia wcześniejszego zatrzymania po kodowaniu. Ponowienie wymaga najpierw uzgodnienia bazy z aktualnymi plikami.

## 1. Zablokowanie kolejki po przerwaniu panelu — potwierdzone

`src/Dit.Worker/Program.cs:163` zwraca brak zadania, jeśli jakiekolwiek polecenie ma stan `inflight`. Nie ma dzierżawy zadania ani mechanizmu sprawdzania, czy panel, który je pobrał, nadal wykonuje pracę. Blokada obejmuje wszystkie produkcje.

Kontrola przerwanych poleceń działa przy uruchomieniu workera (`Program.cs:250`), ale restart samego Premiere nie restartuje workera. Przycisk wznawiania w panelu ponownie uruchamia odpytywanie; nie odzyskuje polecenia pozostawionego na serwerze. Administracyjne wznowienie w DIT resetuje wszystkie aktywne/nieudane polecenia, więc także wymaga zawężenia zakresu i sprawdzenia plików przed powtórzeniem.

## 2. Obsługa zdarzeń AME — potwierdzony błąd kontraktu

`adobe/src/bridge.ts:220` szuka ścieżki pliku w `JSON.stringify(event)`. Obsługa ERROR i CANCEL również wymaga ścieżki (`:224`), a timeout wynosi 12 godzin.

Oficjalny przykład Adobe określa COMPLETE przez `jobID` i `outputFiles`, ERROR przez `jobID` i `inErrorNumber`, a CANCEL przez `jobID`. ERROR/CANCEL nie muszą zawierać ścieżki, więc obecny kod ignoruje te zdarzenia. Źródło: [przykład EventManager Adobe](https://github.com/AdobeDocs/uxp-premiere-pro-samples/blob/main/sample-panels/premiere-api/src/eventManager.ts).

Reprodukcja `artifacts/analyze-adobe-events.cjs`, uruchomiona na skompilowanym adapterze, potwierdza:

| Przypadek | Wynik |
| --- | --- |
| COMPLETE jako zwykły obiekt z outputFiles | Walidacja i zakończenie |
| ERROR z jobID i inErrorNumber | Zignorowany; timeout |
| CANCEL z jobID | Zignorowany; timeout |
| COMPLETE z właściwościami nieenumerowalnymi | Zignorowany; timeout |

Ostatni przypadek jest modelem możliwego problemu serializacji obiektów hosta, a nie przechwyconym zdarzeniem z tej sesji Premiere. Test nie wywołuje Adobe ani workera; skraca wyłącznie testowy timeout do 30 ms.

Panel nie obsługuje zdarzenia postępu AME. [EncoderManager Adobe](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/encodermanager) udostępnia EVENT_RENDER_PROGRESS. Obecny interfejs nie potrafi odróżnić faktycznego kodowania od oczekiwania na zgubione potwierdzenie.

## 3. Audio proxy nie spełnia walidacji — potwierdzona niezgodność

Wszystkie 136 nagrań mają w metadanych 0 kanałów audio. Preset .epr włącza eksport audio stereo; log rzeczywistego kodowania także podaje stereo. `src/Dit.Core/ProxyPolicy.cs:14` wymaga identycznej liczby kanałów oryginału i proxy.

Wynik z dwoma kanałami zostanie odrzucony dla oryginału bez audio. Potrzebna jest świadoma obsługa materiałów bez dźwięku oraz materiałów o różnych układach audio. Jeden ręcznie wskazany preset nie zapewnia tej zgodności dla wszystkich plików. Usuniętego proxy nie można już zbadać ffprobe; liczbę kanałów wyjściowych potwierdza log AME, nie analiza istniejącego pliku.

## 4. Długość kontenera i obrazu — potwierdzona różnica, ryzyko odrzucenia

Oryginalny DJI_0357.MP4 nadal istnieje. Odczyt ffprobe wskazuje:

- długość kontenera: 18,000 s;
- długość strumienia wideo: 17,250567 s;
- strumienie danych/napisów: 18,000 s.

`src/Dit.Infrastructure/MediaTools.cs:46` przyjmuje długość kontenera. `ProxyPolicy.cs:14` dopuszcza różnicę tylko 40 ms. AME raportuje długość kodowanego obrazu około 17,2 s. Jeżeli plik proxy ma taką długość jak strumień obrazu, zostanie odrzucony, choć obraz został zakodowany w całości. Dokładna długość usuniętego proxy wymaga ponownego pomiaru po kontrolnej próbie.

Naprawa powinna porównywać odpowiednie strumienie i liczbę klatek z tolerancją wynikającą z materiału, zachowując wykrywanie uciętych plików. Samo zwiększenie tolerancji bez tego rozróżnienia byłoby niewystarczające.

## Dlaczego testy tego nie wykryły

Test adaptera (`adobe/tests/adapter.test.cjs:12`) emituje sztuczny COMPLETE z `outputFile`, a endpoint walidacji zwraca sztuczne `valid: true`. Nie sprawdza rzeczywistego wyniku ffprobe ani zdarzeń hosta. Nie obejmuje opisanych ERROR/CANCEL, zerowego audio, różnicy długości strumieni i restartu Premiere podczas polecenia.

Wyniki wcześniejszych testów .NET/Node nie stanowiły pełnej kwalifikacji AME E2E. Udane testy rzeczywistego hosta dotyczyły wcześniej przede wszystkim pustych projektów, sekwencji, zapisu i przełączania.

## Kolejność naprawy i kryterium zakończenia

1. Zapisywać ustrukturyzowane zdarzenia i identyfikatory AME; obsłużyć COMPLETE/ERROR/CANCEL bez polegania na JSON.stringify. Dodać postęp i czytelny powód niezgodności proxy.
2. Wprowadzić odzyskiwanie przerwanego zadania z kontrolą właściciela, zapisanych wyników i plików; odróżnić restart panelu od nadal pracującego AME. Reset ograniczyć do właściwego zakresu.
3. Dopasować eksport i walidację audio oraz długości do rzeczywistych strumieni.
4. Przeprowadzić kontrolną próbę na istniejącym materiale źródłowym: jedno nagranie bez audio, jedno z audio, potem kilka nagrań i dwa dni w jednym projekcie Premiere. Sprawdzić pliki, przypięcie proxy, sekwencje, zapis i stan complete w bazie.
5. Osobno sprawdzić anulowanie kodowania oraz restart panelu/Premiere; powtórzenie ma kontynuować bez duplikacji importu i sekwencji.

W tej diagnostyce nie zmieniano kodu aplikacji, konfiguracji ani stanu kolejki. Nie uruchamiano ponownie kodowania. Dodano raport i lokalną reprodukcję zachowania adaptera.
