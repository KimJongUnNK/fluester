<p align="center">
  <img src="docs/icon.svg" width="128" height="128" alt="Flüster">
</p>

<h1 align="center">Flüster</h1>

<p align="center">
  <b>Diktieren in jeder Mac-App. Fn halten, sprechen, loslassen.</b><br>
  Kostenlos. Auf Wunsch komplett offline. Eine freie Alternative zu Wispr Flow.
</p>

<p align="center">
  <a href="https://github.com/KimJongUnNK/fluester/releases/latest"><img src="https://img.shields.io/github/v/release/KimJongUnNK/fluester?label=Version&color=4F29C7" alt="Version"></a>
  <a href="https://github.com/KimJongUnNK/fluester/actions/workflows/ci.yml"><img src="https://github.com/KimJongUnNK/fluester/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/macOS-14%2B%20%C2%B7%20Apple%20Silicon-111111?logo=apple" alt="macOS 14+ · Apple Silicon">
  <a href="LICENSE"><img src="https://img.shields.io/badge/Lizenz-MIT-DB409E" alt="MIT"></a>
</p>

<p align="center">
  <a href="#installation"><b>Installieren</b></a> ·
  <a href="#einrichten">Einrichten</a> ·
  <a href="#bedienung">Bedienung</a> ·
  <a href="#häufige-probleme">Hilfe</a> ·
  <a href="README.en.md">English</a>
</p>

---

Du hältst **Fn**, sprichst und lässt los. Der Text landet sauber formuliert dort, wo dein Cursor steht: in WhatsApp, Mail, Slack, Notion, Word, im Browser oder im Terminal. Füllwörter fliegen raus, Satzzeichen sitzen, und „um 3, ich meine um 4“ wird zu „um 4“.

## Was Flüster kann

- **Überall diktieren:** Taste halten zum Sprechen, doppelt tippen zum freihändigen Diktieren, Esc bricht ab.
- **Saubere Texte:** Füllwörter und Stotterer raus, Selbstkorrekturen übernehmen, „neue Zeile“ als Befehl, Aufzählungen als Liste.
- **Command Mode:** Text markieren, **Fn + ⌃** halten, „mach das förmlicher“ oder „übersetz das ins Englische“ sagen.
- **Stil je App:** Locker in WhatsApp, förmlich in Mail, nüchtern in Slack. Alles einstellbar.
- **Persönliches Wörterbuch:** Namen und Marken immer richtig geschrieben.
- **Textbausteine:** Du sagst „meine Adresse“, Flüster schreibt die ganze Adresse.
- **Verlauf und Statistik:** Wörter pro Minute und gesparte Zeit. Alles bleibt lokal.
- **Sprachen:** Deutsch, Englisch, gemischt und über 100 weitere Sprachen.
- **Wählbare Technik:**

| Erkennung | Kosten | Offline | Einrichtung |
| --- | --- | --- | --- |
| **Apple** (SpeechAnalyzer, macOS 26) | 0 € | ✓ | keine |
| **Whisper lokal** (whisper.cpp, Large v3 Turbo) | 0 € | ✓ | 2 Befehle, 547 MB |
| **Cloud** (Groq, OpenAI, eigener Server) | Groq: Gratis-Kontingent | – | API-Schlüssel |

Bereinigt wird mit **Apple Intelligence** (auf dem Mac), einem **Sprachmodell** (Groq, OpenAI oder **Ollama lokal**) oder **nur mit Regeln** (sofort, offline).

---

## Installation

**Du brauchst:** einen Mac mit **Apple-Chip** (M1 oder neuer) und **macOS 14** oder neuer. Für die beste Apple-Erkennung empfiehlt sich macOS 26.

### Weg A: ein Befehl (empfohlen)

1. **Terminal** öffnen: **⌘ + Leertaste** drücken, „Terminal“ tippen, Enter.
2. Diese Zeile einfügen und Enter drücken:

   ```bash
   curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash
   ```

3. Fertig. Das Skript lädt die neueste Version, legt sie in **Programme** ab und startet Flüster. Weiter geht es mit [Einrichten](#einrichten).

> Fragt das Terminal nach einem Passwort, ist dein **Mac-Passwort** gemeint. Beim Tippen siehst du nichts, das ist normal.

### Weg B: manuell

1. **[Fluester-macOS.zip herunterladen](https://github.com/KimJongUnNK/fluester/releases/latest/download/Fluester-macOS.zip)**. Alle Versionen findest du unter [Releases](https://github.com/KimJongUnNK/fluester/releases).
2. Die ZIP im Ordner **Downloads** doppelklicken. Heraus kommt **Flüster.app**.
3. **Flüster.app** in den Ordner **Programme** ziehen.
4. Flüster ist nicht bei Apple notarisiert, das kostet 99 $ im Jahr. Deshalb blockiert macOS den ersten Start. Du hast zwei Möglichkeiten:
   - **Terminal:** einfügen und Enter drücken.
     ```bash
     xattr -dr com.apple.quarantine "/Applications/Flüster.app"
     ```
   - **Ohne Terminal:** Flüster doppelklicken und die Warnung schließen. Dann **Systemeinstellungen › Datenschutz & Sicherheit** öffnen, ganz nach unten scrollen, **„Dennoch öffnen“** klicken und mit deinem Passwort bestätigen.
5. Flüster aus **Programme** starten.

---

## Einrichten

> **Flüster hat kein Dock-Symbol.** Es sitzt oben rechts in der **Menüleiste** als Wellen-Symbol. Darüber erreichst du Einstellungen, Verlauf und „Beenden“.

Beim ersten Start öffnet sich die Einrichtung. Sie dauert eine Minute:

1. **Mikrofon** → **Erlauben**. Flüster hört nur zu, solange du die Taste hältst.
2. **Bedienungshilfen** → **Erlauben**. In den Systemeinstellungen bei **Flüster** den Schalter einschalten. Das braucht die App für die Fn-Taste und zum Einfügen in andere Apps.
3. **Spracherkennung** → **Erlauben**. Das gilt nur für die Apple-Erkennung.
4. **🌐/Fn-Taste umstellen:** **Systemeinstellungen › Tastatur** öffnen und bei **„🌐-Taste drücken zum“** die Option **„Keine Aktion“** wählen. Sonst öffnet sich bei jedem Druck das Emoji-Fenster.
   *Keine Fn-Taste, oder du willst sie behalten?* Als Diktat-Taste geht auch **Rechte ⌥ Option**, **Rechte ⌘ Command** oder **Rechte ⌃ Control**.
5. **Ausprobieren:** Ins Testfeld klicken, **Fn halten**, sprechen, loslassen. Dann auf **„Los geht's“** klicken.

Damit Flüster nach jedem Neustart bereitsteht: **Menüleisten-Symbol › Einstellungen › Allgemein › „Bei Anmeldung starten“** einschalten.

---

## Erkennung wählen

Zu finden unter **Menüleisten-Symbol › Einstellungen › Erkennung**.

### Apple: sofort startklar
Diese Erkennung ist voreingestellt und braucht keine Einrichtung. Unter macOS 26 nutzt Flüster die neue SpeechAnalyzer-Erkennung. Ihr Sprachpaket lädt beim ersten Start im Hintergrund. Bis es fertig ist, springt automatisch die ältere Apple-Erkennung ein.

### Whisper lokal: die beste Qualität, offline (empfohlen)
Whisper versteht **Deutsch-Englisch-Mix**, Namen und Fachbegriffe deutlich besser.

1. **Homebrew** installieren, falls noch nicht vorhanden (Anleitung auf [brew.sh](https://brew.sh)):
   ```bash
   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
   ```
2. **whisper.cpp** installieren:
   ```bash
   brew install whisper-cpp
   ```
3. In Flüster unter **Einstellungen › Erkennung** die Option **„Whisper lokal (whisper.cpp)“** wählen. Dann beim Modell **„Large v3 Turbo (empfohlen)“** auf **Laden** klicken. Das sind 547 MB und passiert nur einmal.

Das erste Diktat nach dem Start dauert ein paar Sekunden länger, weil das Modell in den Speicher geladen wird. Danach geht es flott.

### Cloud: am schnellsten
1. Bei **[console.groq.com/keys](https://console.groq.com/keys)** kostenlos einen API-Schlüssel erstellen.
2. In Flüster **„Cloud (Groq / OpenAI)“** wählen, als Anbieter **Groq** einstellen und den Schlüssel bei **API-Schlüssel** einfügen.
3. Auf **„Verbindung testen“** klicken.

Der Schlüssel liegt im macOS-Schlüsselbund. Bei Cloud-Erkennung geht deine Aufnahme an den Anbieter.

### KI-Bereinigung
Zu finden unter **Einstellungen › KI-Bereinigung**:
- **Apple Intelligence:** auf dem Mac und kostenlos. Braucht macOS 26 und eingeschaltetes Apple Intelligence.
- **Sprachmodell:** Groq (gratis, gleicher Schlüssel), OpenAI oder **Ollama** komplett lokal. Für Ollama zuerst `brew install ollama && brew services start ollama && ollama pull qwen2.5:7b` ausführen, dann als Anbieter „Ollama (lokal)“ wählen.
- **Nur Regeln:** sofort und offline.

Fällt die KI aus oder ist sie zu langsam, greifen automatisch die Regeln. Ein Diktat geht nie verloren.

---

## Bedienung

| Was | Wie |
| --- | --- |
| Diktieren | **Fn halten** → sprechen → loslassen |
| Freihändig | **Fn doppelt tippen** → sprechen → **Fn tippen** zum Beenden |
| Command Mode | Text markieren → **Fn + ⌃ halten** → „mach das kürzer“ |
| Abbrechen | **Esc** |
| Zeilenumbruch | „neue Zeile“, „neuer Absatz“ |
| Korrigieren | „um 3, ich meine um 4“ → „um 4“ · „streich das“ |
| Liste | „erstens Milch, zweitens Brot, drittens Eier“ |
| Letztes Diktat nochmal | Menüleisten-Symbol › „Letztes Diktat erneut einfügen“ |

### Wörterbuch
Unter **Einstellungen › Wörterbuch** trägst du Namen, Marken und Fachbegriffe ein. Bei **„klingt wie“** gibst du an, wie die Erkennung sie falsch schreibt. Beispiel: Schreibweise **Wispr Flow**, klingt wie **Whisperflow**. Die Begriffe helfen außerdem der Erkennung.

### Textbausteine
Unter **Einstellungen › Textbausteine** legst du ein Stichwort fest, etwa „mein Calendly-Link“, und den Text, der dafür eingesetzt wird.

### Stile
Unter **Einstellungen › Stile** wählst du pro App-Gruppe einen Stil: **Formell**, **Locker**, **Sehr locker**, **Begeistert** oder **Wörtlich**. Die Gruppen sind private Nachrichten, E-Mail, Arbeits-Chats, Code & Terminal und alle anderen Apps.

---

## Aktualisieren

Den Installationsbefehl einfach nochmal ausführen:
```bash
curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash
```
Danach **Bedienungshilfen einmal neu erlauben**. Jede Version hat eine neue Signatur, deshalb fragt macOS erneut. Die Einrichtung öffnet sich dafür von selbst.

## Deinstallieren

```bash
curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash -s -- --uninstall
```
Verlauf, Einstellungen und Modelle löschst du so:
```bash
rm -rf ~/Library/Application\ Support/Fluester
```

---

## Häufige Probleme

**Die Fn-Taste reagiert nicht.**
Flüster ist unter Bedienungshilfen nicht erlaubt, oder die Freigabe passt nach einem Update nicht mehr. So behebst du das: **Systemeinstellungen › Datenschutz & Sicherheit › Bedienungshilfen** öffnen, Flüster mit **–** entfernen, mit **+** wieder hinzufügen und einschalten. Prüfen kannst du es unter **Einstellungen › Berechtigungen** in Flüster.

**Es geht das Emoji-Fenster auf.**
Stell **Tastatur › „🌐-Taste drücken zum“** auf **„Keine Aktion“** (siehe [Einrichten](#einrichten)).

**„Flüster ist beschädigt“ oder „kann nicht geöffnet werden“.**
Das ist die Gatekeeper-Sperre. Führ den `xattr`-Befehl aus [Weg B](#weg-b-manuell) aus oder installier mit [Weg A](#weg-a-ein-befehl-empfohlen).

**`brew install` meldet „/opt/homebrew is not writable“.**
Dann gehört der Homebrew-Ordner nicht dir. So reparierst du ihn:
```bash
sudo chown -R $(whoami) /opt/homebrew
brew update
```

**`brew install` meldet „No available formula“.**
Prüf die Schreibweise. Richtig ist **`whisper-cpp`**, nicht `whisper-ccp` oder `whisper.cpp`.

**Unter Erkennung steht „whisper.cpp ist noch nicht installiert“.**
Führ `brew install whisper-cpp` aus und prüf das Ergebnis mit `ls /opt/homebrew/bin/whisper-server`. Danach das Einstellungsfenster neu öffnen. Dort steht dann „gefunden ✓“.

**Es wird nichts eingefügt.**
Manche Felder nehmen kein ⌘V an. Dann unter **Einstellungen › Allgemein › Einfügen** die Methode **„Tippen“** wählen. Jedes Diktat liegt außerdem im **Verlauf**.

**Das falsche Mikrofon nimmt auf.**
Wähl unter **Einstellungen › Allgemein › Mikrofon** den richtigen **Eingang**.

**Im Menü steht „Ältere Apple-Erkennung aktiv“.**
Das neue Sprachpaket lädt noch oder wird auf diesem Mac nicht unterstützt. Diktieren klappt trotzdem. Wer die beste Qualität will, nimmt Whisper lokal.

**„Apple Intelligence nicht verfügbar“.**
Schalt es unter **Systemeinstellungen › Apple Intelligence & Siri** ein. Bis dahin bereinigt Flüster mit Regeln.

Dein Problem ist nicht dabei? Dann [ein Issue öffnen](https://github.com/KimJongUnNK/fluester/issues) und am besten die Meldung aus dem Menü mitschicken.

---

## Datenschutz

- Mit **Apple** oder **Whisper lokal** plus **Apple Intelligence**, **Ollama** oder **Regeln** verlassen weder Ton noch Text deinen Mac.
- Verlauf und Einstellungen liegen nur lokal in `~/Library/Application Support/Fluester`. API-Schlüssel liegen im macOS-Schlüsselbund.
- Kein Konto, kein Tracking, keine Telemetrie.

---

## Selbst bauen

Dafür brauchst du **Xcode 26** oder die passenden Command Line Tools (`xcode-select --install`), denn Flüster nutzt das macOS-26-SDK.

```bash
git clone https://github.com/KimJongUnNK/fluester.git
cd fluester
INSTALL=1 ./scripts/build-app.sh   # baut, signiert, installiert nach /Applications und startet
```

Weitere Befehle:
```bash
swift test                          # Tests: Textbereinigung, Gesten, Prompts, API, Audio
./scripts/build-app.sh              # nur build/Flüster.app + build/Fluester-macOS.zip
build/Flüster.app/Contents/MacOS/Fluester --selftest   # Selbsttest mit echter Spracherkennung
```

**Aufbau:**
- `Sources/FluesterCore`: die reine Logik, mit Tests abgedeckt. Dazu gehören Textbereinigung, Wörterbuch, Stile, Prompts, die Gesten der Diktat-Taste, der OpenAI-kompatible Client und die Statistik.
- `Sources/Fluester`: die App selbst. Sie umfasst:
  - Hotkey (CGEventTap) und Mikrofon (AVAudioEngine)
  - Erkennung über SpeechAnalyzer, SFSpeechRecognizer, whisper-server und Cloud
  - Apple Intelligence
  - Einfügen in andere Apps, Flow-Leiste, Menüleiste, Einstellungen und Einrichtung

**Neue Version veröffentlichen:** `CHANGELOG.md` ergänzen, dann `git tag v1.2.3 && git push origin v1.2.3`. GitHub testet, baut und veröffentlicht das Release automatisch.

---

## Lizenz

[MIT](LICENSE). Nutzen, ändern und weitergeben ist erlaubt.

Flüster ist ein eigenständiges Projekt, enthält keinen Code von Wispr Flow und ist nicht mit Wispr verbunden. „Wispr Flow“ ist eine Marke der jeweiligen Inhaber.
