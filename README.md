# Flüster

Diktieren in jeder Mac-App, wie **Wispr Flow**, aber kostenlos: **Fn halten, sprechen, loslassen.** Der Text landet bereinigt dort, wo der Cursor steht: Mail, WhatsApp, Slack, Notion, Word, Browser, Terminal.

Flüster ist ein eigener Nachbau und enthält keinen Code von Wispr Flow. Spracherkennung und KI laufen wahlweise **komplett auf deinem Mac** oder über das Gratis-Kontingent von Groq.

## Was es kann

| | Flüster | Wispr Flow Pro |
| --- | --- | --- |
| Halten zum Diktieren, Doppeltipp = freihändig | ✓ | ✓ |
| In jede App einfügen, Zwischenablage bleibt erhalten | ✓ | ✓ |
| KI-Bereinigung: Füllwörter raus, Selbstkorrekturen, Satzzeichen, Listen | ✓ | ✓ |
| Command Mode: Text markieren, Fn+⌃ halten, „mach das förmlicher“ | ✓ | ✓ |
| Persönliches Wörterbuch (Namen, Marken) | ✓ | ✓ |
| Textbausteine („meine Adresse“ → ganze Adresse) | ✓ | ✓ |
| Stil je App (formell in Mail, locker in WhatsApp) | ✓ | ✓ (nur Englisch) |
| Verlauf, Wörter/Minute, gesparte Zeit | ✓ | ✓ |
| Deutsch, Englisch, gemischt, 100+ Sprachen | ✓ | ✓ |
| Komplett offline möglich | ✓ | – |
| Preis | **0 €** | 15 $/Monat |

**Erkennung (wählbar):**
- **Apple (auf dem Mac):** macOS 26 SpeechAnalyzer, offline, keine Einrichtung. Das Sprachpaket lädt beim ersten Start im Hintergrund. So lange, oder falls SpeechAnalyzer streikt, springt automatisch die ältere Apple-Erkennung ein. Unter macOS 14/15 läuft immer die ältere.
- **Whisper lokal:** whisper.cpp mit Large-v3-Turbo, offline, sehr genau, auch gemischt Deutsch/Englisch.
- **Cloud:** Groq (Gratis-Kontingent von ca. 8 h Audio pro Tag), OpenAI oder jeder OpenAI-kompatible Server.

**KI-Bereinigung (wählbar):**
- **Apple Intelligence:** Foundation Models, auf dem Mac, kostenlos (macOS 26, Apple Silicon).
- **Sprachmodell:** Groq (gratis), OpenAI, **Ollama lokal** oder ein eigener Server.
- **Nur Regeln:** sofort, offline. Entfernt Füllwörter und Stotterer, setzt „neue Zeile“ als Befehl um, formatiert Aufzählungen und schreibt Wörterbuch-Begriffe richtig.

## Installieren

### A) Fertige App aus GitHub Actions
1. Im Repo unter **Actions → „Flüster (macOS)“** den letzten grünen Lauf öffnen und **Fluester-macOS** herunterladen.
2. Entpacken und **Flüster.app** nach **Programme** ziehen.
3. Die App ist nicht notarisiert, also einmal im Terminal ausführen:
   ```bash
   xattr -dr com.apple.quarantine "/Applications/Flüster.app"
   ```
   Alternativ beim ersten Start in **Systemeinstellungen › Datenschutz & Sicherheit** auf „Trotzdem öffnen“ klicken.

### B) Selbst bauen (ca. 1 Minute)
Voraussetzung sind die Xcode Command Line Tools (`xcode-select --install`).
```bash
cd fluester
INSTALL=1 ./scripts/build-app.sh
```
Das baut, signiert, kopiert die App nach `/Applications` und startet sie.

## Einrichten (einmalig, die App führt durch)
1. **Mikrofon** erlauben.
2. **Bedienungshilfen** erlauben: Flüster in der Liste einschalten. Das braucht die App für die Fn-Taste und zum Einfügen.
3. **Spracherkennung** erlauben (nur bei „Apple“, für die ältere Erkennung, solange das Sprachpaket lädt).
4. **Fn/🌐-Taste:** Systemeinstellungen › Tastatur › „🌐-Taste drücken zum“ auf **„Keine Aktion“** stellen. Sonst öffnet sich das Emoji-Fenster. Alternativ in Flüster rechte ⌥, ⌘ oder ⌃ als Taste wählen.
5. Fertig. Fn halten und sprechen.

**Optional, für die beste Qualität:**
- **Whisper lokal:** `brew install whisper-cpp`, dann unter Einstellungen › Erkennung das Modell „Large v3 Turbo“ laden (547 MB).
- **Groq (gratis):** Schlüssel auf [console.groq.com/keys](https://console.groq.com/keys) erstellen und unter Einstellungen › Erkennung bzw. › KI-Bereinigung eintragen.
- **Ollama lokal:** `brew install ollama && ollama pull qwen2.5:7b`, dann unter KI-Bereinigung „Sprachmodell“ und „Ollama“ wählen.

## Bedienung
| Aktion | Wie |
| --- | --- |
| Diktieren | Fn halten → sprechen → loslassen |
| Freihändig | Fn doppelt tippen → sprechen → Fn tippen |
| Command Mode | Text markieren → Fn + ⌃ halten → „übersetz das ins Englische“ |
| Abbrechen | Esc |
| Zeilenumbruch | „neue Zeile“ / „neuer Absatz“ |
| Korrigieren | „um 3, ich meine um 4“ → „um 4“ · „streich das“ |
| Liste | „erstens Milch, zweitens Brot, drittens Eier“ |
| Textbaustein | Stichwort sagen, z. B. „meine Adresse“ |

## Häufige Probleme
- **Taste reagiert nicht:** Unter Bedienungshilfen ist Flüster nicht erlaubt, oder nach einem Update passt die Freigabe nicht mehr. Dann Flüster in der Liste entfernen (–) und neu hinzufügen (+). `INSTALL=1` setzt die Freigabe automatisch zurück.
- **Emoji-Fenster geht auf:** 🌐-Taste auf „Keine Aktion“ stellen (siehe oben).
- **Nichts wird eingefügt:** Manche Felder nehmen kein ⌘V an. Dann unter Einstellungen › Allgemein die Methode „Tippen“ wählen. Der Text liegt außerdem im Verlauf.
- **Menü zeigt „Ältere Apple-Erkennung aktiv“:** Das neue Sprachpaket lädt noch oder ließ sich nicht laden. Unter Einstellungen › Erkennung auf „Jetzt laden“ klicken, der Grund steht daneben. Diktieren geht in der Zwischenzeit trotzdem. Wer es sofort am genauesten will, wechselt auf Whisper lokal.
- **Apple Intelligence nicht verfügbar:** Systemeinstellungen › Apple Intelligence & Siri einschalten. Bis dahin gelten automatisch die Regeln.

## Entwicklung
```bash
swift test                 # Tests für Textbereinigung, Gesten, Prompts, API, Audio
swift build                # Debug-Build
./scripts/build-app.sh     # Release-App + ZIP
```
Aufbau:
- `Sources/FluesterCore`: reine Logik, getestet. Textbereinigung (`TextPipeline`), Wörterbuch und Snippets (`Vocabulary`), Stile je App, Prompts, Gesten-Erkennung für die Taste, WAV, OpenAI-kompatibler Client, Statistik.
- `Sources/Fluester`: die App. Hotkey (CGEventTap), Mikrofon (AVAudioEngine), Engines (SpeechAnalyzer, SFSpeech, whisper-server, Cloud), Apple Intelligence, Einfügen, Flow-Leiste, Menüleiste, Einstellungen, Einrichtung.

Datenschutz: Mit Apple oder Whisper lokal plus Apple Intelligence, Ollama oder „Nur Regeln“ verlässt kein Ton und kein Text den Mac. Verlauf und Einstellungen liegen in `~/Library/Application Support/Fluester`, API-Schlüssel im Schlüsselbund.
