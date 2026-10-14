# Changelog

Alle wichtigen Änderungen an Flüster. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

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
