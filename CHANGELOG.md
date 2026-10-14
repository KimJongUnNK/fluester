# Changelog

Alle wichtigen Änderungen an Flüster. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

## [1.1.1] – 2026-10-14

Linux: Mikrofon wählen.

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
