<p align="center">
  <img src="docs/icon.svg" width="128" height="128" alt="Flüster">
</p>

<h1 align="center">Flüster</h1>

<p align="center">
  <b>Diktieren in jeder App. Taste halten, sprechen, loslassen.</b><br>
  Kostenlos und offline. Für macOS, Windows und Linux. Eine freie Alternative zu Wispr Flow.
</p>

<p align="center">
  <a href="https://github.com/KimJongUnNK/fluester/releases/latest"><img src="https://img.shields.io/github/v/release/KimJongUnNK/fluester?label=Version&color=4F29C7" alt="Version"></a>
  <a href="https://github.com/KimJongUnNK/fluester/actions/workflows/macos.yml"><img src="https://github.com/KimJongUnNK/fluester/actions/workflows/macos.yml/badge.svg" alt="macOS"></a>
  <a href="https://github.com/KimJongUnNK/fluester/actions/workflows/dotnet.yml"><img src="https://github.com/KimJongUnNK/fluester/actions/workflows/dotnet.yml/badge.svg" alt="Windows & Linux"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/Lizenz-MIT-DB409E" alt="MIT"></a>
</p>

<p align="center"><a href="README.en.md">English</a></p>

---

Du hältst eine Taste, sprichst und lässt los. Der Text landet sauber formuliert dort, wo dein Cursor steht: in WhatsApp, Mail, Slack, Notion, Word, im Browser oder im Terminal. Füllwörter fliegen raus, Satzzeichen sitzen, und „um 3, ich meine um 4“ wird zu „um 4“.

## Installieren

| System | Ein Befehl | Anleitung |
| --- | --- | --- |
| **macOS**<br>Apple-Chip, macOS 14+ | Im Terminal:<br>`curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh \| bash` | **[macOS-Anleitung →](docs/macos.md)** |
| **Windows**<br>10/11, 64 Bit | In PowerShell:<br>`irm https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.ps1 \| iex` | **[Windows-Anleitung →](docs/windows.md)** |
| **Linux**<br>x64/arm64, X11 & Wayland | Im Terminal:<br>`curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh \| bash` | **[Linux-Anleitung →](docs/linux.md)** |

Lieber von Hand? Alle Dateien liegen unter **[Releases](https://github.com/KimJongUnNK/fluester/releases/latest)**: `Fluester-macOS.zip`, `Fluester-Windows-x64.zip`, `Fluester-Linux-x64.tar.gz`, `Fluester-Linux-arm64.tar.gz`.

## Was Flüster kann

- **Überall diktieren:** Taste halten zum Sprechen, doppelt tippen für freihändig, Esc bricht ab.
- **Saubere Texte:** Füllwörter und Stotterer raus, Selbstkorrekturen übernehmen, „neue Zeile“ als Befehl, Aufzählungen als Liste.
- **Command Mode:** Text markieren, Taste + Zusatztaste halten und „mach das förmlicher“ oder „übersetz das ins Englische“ sagen.
- **Stil je App:** locker im Chat, förmlich in der Mail.
- **Wörterbuch und Textbausteine:** Namen immer richtig, „meine Adresse“ wird zur ganzen Adresse.
- **Verlauf und Statistik:** Wörter pro Minute und gesparte Zeit. Alles bleibt lokal.
- **Sprachen:** Deutsch, Englisch, gemischt und über 100 weitere.

## Je System

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Diktat-Taste | **Fn / 🌐** (oder rechte ⌥ ⌘ ⌃) | **Strg rechts** (oder Strg+Win, ⇪, Alt rechts, F8) | **Strg rechts** (oder Strg+Super, ⇪, Alt rechts, F8) |
| Zusatz für Command Mode | ⌃ | Umschalt | Umschalt |
| Oberfläche | Menüleiste, Flow-Leiste, Einstellungen | App-Fenster mit Seitenleiste (Startmenü), Infobereich, Flow-Leiste | App-Fenster mit Seitenleiste (GTK 4/libadwaita, App-Menü), Flow-Leiste (X11/Xwayland), Hintergrunddienst |
| Erkennung offline | Apple SpeechAnalyzer, whisper.cpp | whisper.cpp in der App (Grafikkarte über Vulkan) | whisper.cpp im Dienst (Grafikkarte über Vulkan) |
| Erkennung Cloud | Groq, OpenAI, eigener Server | Groq, OpenAI, eigener Server | Groq, OpenAI, eigener Server |
| KI-Bereinigung | Apple Intelligence, Groq/OpenAI/Ollama, Regeln | Groq/OpenAI/Ollama, Regeln | Groq/OpenAI/Ollama, Regeln |
| Getestet | automatisch in der CI und von Hand auf einem Mac mini | automatisch in der CI (Taste, Einfügen, Erkennung) | automatisch in der CI (Taste, Zwischenablage, Erkennung, jede Seite des Fensters) |

## Datenschutz

Mit lokaler Erkennung und lokaler oder regelbasierter Bereinigung verlassen weder Ton noch Text deinen Rechner. Es gibt kein Konto, kein Tracking und keine Telemetrie. API-Schlüssel liegen verschlüsselt bzw. nur für dich lesbar:
- **macOS:** im Schlüsselbund
- **Windows:** mit DPAPI verschlüsselt
- **Linux:** in einer Datei mit Rechten 600

## Aufbau

```text
macos/     Mac-App (Swift, AppKit/SwiftUI) – eigene Logik in macos/Sources/FluesterCore
dotnet/    gemeinsame Logik für Windows und Linux (C#): Textbereinigung, Engine, Tests
windows/   Windows-App (WinForms, Tastatur-Hook, WASAPI, SendInput)
linux/     Linux-Dienst (evdev, uinput, PipeWire/PulseAudio/ALSA) und App-Fenster (GTK 4 + libadwaita)
scripts/   Installer (install.sh für macOS/Linux, install.ps1 für Windows) und Paket-Skripte
docs/      Anleitungen je System
```

Die Textbereinigung verhält sich auf allen Systemen gleich, denn die C#-Tests prüfen dieselben Fälle wie die Swift-Tests. Jede Version wird auf GitHub gebaut und auf echten macOS-, Windows- und Linux-Runnern geprüft. Geprüft werden die Tests, eine echte Spracherkennung mit Whisper und die Installer.

**Neue Version:** `CHANGELOG.md` ergänzen, dann auf GitHub **Actions › Release › „Run workflow“** öffnen und die Version eintragen. Das Release mit allen vier Paketen entsteht dann automatisch.

## Lizenz

[MIT](LICENSE). Flüster ist ein eigenständiges Projekt, enthält keinen Code von Wispr Flow und ist nicht mit Wispr verbunden. „Wispr Flow“ ist eine Marke der jeweiligen Inhaber. Die Spracherkennung nutzt [whisper.cpp](https://github.com/ggml-org/whisper.cpp) (MIT) bzw. [Whisper.net](https://github.com/sandrohanea/whisper.net) (MIT).
