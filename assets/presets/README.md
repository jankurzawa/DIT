# Preset proxy

`DIT Proxy 720p.epr` jest presetem utworzonym przez użytkownika w Adobe Media Encoder i dołączonym do DIT na jego prośbę. Format: QuickTime / Apple ProRes 422 Proxy, 1280×720, FPS zgodny ze źródłem, eksport audio stereo.

Plik jest kopiowany do katalogu `Presets` podczas kompilacji i publikacji. Konfiguracja zapisuje identyfikator `dit:proxy-720p`; worker rozwiązuje go do ścieżki we własnym pakiecie przed przekazaniem zadania Adobe. Dzięki temu nowa instalacja nie zależy od pulpitu użytkownika ani ścieżki poprzedniej wersji.

Od wersji 0.1.18 worker generuje z tego pliku wariant dla konkretnego nagrania: bez audio, mono lub stereo. Warianty znajdują się w `_DIT/Presety` katalogu proxy. Materiał wideo z większą liczbą kanałów wymaga własnego presetu zachowującego wszystkie kanały. Wybrany przez użytkownika preset pozostaje niezmieniony. Walidacja porównuje czas strumienia obrazu, a nie dłuższego kontenera; sprawdza też pełną liczbę pakietów w gotowym MOV.

Od wersji 0.1.19 domyślny preset zawiera znak wodny Adobe w lewym dolnym rogu (85% krycia). Ścieżka `dit:adobe-proxy-watermark` w szablonie jest rozwiązywana podczas generowania wariantu do `png/ProxyWatermark.png` z zainstalowanego Media Encoder lub Premiere. Grafika Adobe nie jest redystrybuowana w DIT. Brak grafiki daje czytelny błąd. Nowe domyślne wyniki mają końcówkę `_proxy_wm.mov`, aby nie używać ponownie wcześniejszych plików bez znaku; stare pliki pozostają zachowane. Własne presety zachowują własny wygląd i końcówkę `_proxy.mov`.

Panel 0.1.20 automatycznie wykonuje Attach Proxies po fizycznej walidacji filmu. Sprawdza `hasProxy()` oraz `getProxyPath()` przed i po zapisie projektu; istniejące poprawne podłączenie jest używane ponownie. Samo `attachProxy() == true` nie wystarcza do ukończenia zadania. Podgląd proxy w monitorze Premiere wymaga włączenia przełącznika „Toggle Proxies / Przełącz proxy”; oryginał pozostaje bez znaku wodnego.

Warianty presetu mają deterministyczny PresetID zależny od zawartości. AME może pamiętać ustawienia po PresetID, więc sama zmiana ścieżki EPR nie gwarantuje odświeżenia efektów ani kanałów.
