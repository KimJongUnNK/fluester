# Changelog

Alle wichtigen Änderungen an Flüster. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

## [1.1.2] – 2026-10-14

Linux: Flüster bleibt zuverlässig an, auch nach dem ersten Diktat.

- **Unabhängig vom Terminal:** `fluester start` startet den Dienst als eigene systemd-Einheit `fluester`. Ohne systemd läuft er in eigener Sitzung. Schließt du das Terminal, läuft er weiter, und nach einem Absturz startet er nach 2 Sekunden neu. Den Autostart erledigt jetzt auch `fluester start`.
- **Hängende Erkennung:** Kommt die Erkennung nicht zurück (z. B. Grafikkarten-Treiber über Vulkan), startet Flüster nach einer Zeitgrenze neu und rechnet ab dann mit dem Prozessor. Vorher blieb Flüster für immer auf „verarbeitet“, und jede weitere Taste wurde ignoriert.
- **Wächter:** Hängt der Dienst an einem Werkzeug, das nicht antwortet, startet er nach 25 Sekunden neu. Externe Werkzeuge können ihn nicht mehr endlos blockieren.
- **Rückmeldung:** Wer während der Erkennung erneut drückt, bekommt „Moment …“ statt Stille. `fluester status` zeigt, seit wann erkannt wird.
- **Protokoll:** Jeder Schritt steht mit Dauer in `fluester.log`: Aufnahme, Erkennung, Einfügen, Abstürze und Signale. Der diktierte Text steht dort nie.
- **Hintergrundbetrieb:** SIGHUP wird ignoriert, Ausgaben gehen nicht mehr ins Leere.
- **Letzte Silbe:** Nach dem Loslassen nimmt Flüster noch 150 ms weiter auf, damit das Ende nicht abgeschnitten wird.
- **`fluester status`:** Ein bewusst gewähltes Gerät wie ein Monitor erscheint nicht mehr fälschlich als „nicht angeschlossen“.
- **CI:** neuer Ende-zu-Ende-Test mit echter Taste (evdev), Einfügen über uinput, echtem Mikrofon-Audio und Whisper. Er macht mehrere Diktate hintereinander, Start über `fluester start` wie bei Nutzern, und prüft, dass der Dienst das Ende der startenden Shell überlebt.

## [1.1.1] – 2026-10-14

Linux: Mikrofon wählen, Tastaturzugriff ohne Neuanmelden.

- **„Kein Zugriff auf /dev/input/…“:**
  - Flüster sagt jetzt genau, was fehlt: Gruppe `input` nicht eingetragen, oder eingetragen, aber noch nicht neu angemeldet.
  - Im zweiten Fall startet `fluester start` den Dienst über `sg input` sofort mit Tastaturzugriff. Ab- und Anmelden ist dafür nicht mehr nötig.
  - Das Setup bietet das direkt an.
  - Auf Fedora Silverblue/Kinoite übernimmt es die Gruppe vorher aus `/usr/lib/group`, sonst scheitert `usermod` dort.

- **Neuer Befehl `fluester mic`:**
  - Zeigt alle Mikrofone mit Nummer, ohne Lautsprecher-Monitore.
  - Auswahl per Nummer oder Namensteil, danach Pegeltest mit Live-Anzeige.
  - Hebt auf Wunsch die Stummschaltung auf.
  - Dazu `fluester mic list`, `fluester mic test`, `fluester mic 0` (Systemstandard).
- **Einrichtung:** `fluester setup` hat jetzt einen eigenen Schritt „Mikrofon“.
- **Abgestecktes Mikrofon:** Flüster nimmt solange den Systemstandard und meldet das einmal, statt stumm aufzunehmen.
- **Fehler sichtbar:** Scheitert die Aufnahme, kommt eine Meldung mit dem Grund, statt dass das Diktat einfach nichts tut.
- **Einstellungen:** Änderungen über `fluester mic`, `fluester model` und `fluester setup` übernimmt der laufende Dienst jetzt sofort. Vorher erst nach einem Neustart.
- **PipeWire 1.0 (z. B. Ubuntu 24.04):** Die Aufnahme über `pw-record` funktioniert jetzt auch dort.
- **`fluester status` und `fluester selftest`:** zeigen das gewählte Mikrofon und den Pegel jedes Mikrofons.
- **Windows:** Ist das gewählte Mikrofon abgesteckt, nimmt Flüster jetzt den Standard, statt mit „Mikrofon lässt sich nicht öffnen“ abzubrechen.

## [1.1.0] – 2026-10-14

Flüster gibt es jetzt auch für **Windows** und **Linux**.

- **Windows 10/11:**
  - Tray-App mit Flow-Leiste, Einstellungen und Einrichtung.
  - Diktat-Taste Strg rechts, alternativ Strg+Win, ⇪, Alt rechts oder F8.
  - Whisper läuft direkt in der App, mit Grafikkarte über Vulkan.
  - Einfügen per Strg+V mit Wiederherstellung der Zwischenablage, oder per Tippen.
  - API-Schlüssel mit DPAPI verschlüsselt.
  - Installation per PowerShell-Einzeiler, ohne Administratorrechte, mit Eintrag unter „Apps“.
- **Linux (x64/arm64):**
  - Hintergrunddienst für X11 und Wayland: Diktat-Taste über evdev, Einfügen über eine eigene virtuelle Tastatur (uinput).
  - Aufnahme über PipeWire/PulseAudio/ALSA, Rückmeldung per Benachrichtigung.
  - Assistent `fluester setup` richtet Rechte, Pakete, Modell und Autostart ein.
- **Gemeinsame C#-Logik für Windows und Linux:** Sie verhält sich identisch zur Mac-Version, geprüft mit denselben Testfällen.
- **Repo:** neu gegliedert in `macos/`, `windows/`, `linux/`, `dotnet/`, mit Anleitungen je System unter `docs/`.
- **Ein Release mit allen Paketen:** Vorher prüft die CI auf allen drei Systemen echte Spracherkennung und Installer.

## [1.0.0] – 2026-10-14

Erste öffentliche Version.

- Diktieren in jeder App: Fn halten, sprechen, loslassen. Doppeltipp für freihändig, Esc bricht ab.
- Rechte ⌥, ⌘ oder ⌃ als Alternative zur Fn-Taste.
- Erkennung wählbar:
  - Apple SpeechAnalyzer (macOS 26), mit automatischem Rückfall auf die ältere Apple-Erkennung, solange das Sprachpaket lädt.
  - whisper.cpp lokal mit Large v3 Turbo.
  - Cloud: Groq, OpenAI oder ein eigener Server.
- KI-Bereinigung wählbar:
  - Apple Intelligence.
  - Sprachmodell über Groq, OpenAI oder Ollama.
  - Nur Regeln.
- Regeln entfernen Füllwörter und Stotterer, übernehmen Selbstkorrekturen und formatieren Aufzählungen. „Neue Zeile“ und „neuer Absatz“ funktionieren als Befehl.
- Command Mode: markierten Text per Sprache umschreiben.
- Stil je App-Gruppe, persönliches Wörterbuch, Textbausteine.
- Verlauf mit Suche, Statistik (Wörter pro Minute, gesparte Zeit).
- Flow-Leiste mit Live-Wellenform, Menüleisten-App, Einrichtung in vier Schritten.
- Installation und Update mit einem Befehl (`scripts/install.sh`).
