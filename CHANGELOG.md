# Changelog

Alle wichtigen Änderungen an Flüster. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

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
