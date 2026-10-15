<p align="center"><img src="icon.svg" width="96" height="96" alt="Flüster"></p>

<h1 align="center">Flüster für Linux</h1>

<p align="center">
  <a href="#installation"><b>Installieren</b></a> ·
  <a href="#bedienung">Bedienung</a> ·
  <a href="#einstellungen">Einstellungen</a> ·
  <a href="#häufige-probleme">Hilfe</a> ·
  <a href="../README.md">Alle Systeme</a>
</p>

---

Du hältst **Strg rechts**, sprichst und lässt los. Der Text landet sauber formuliert dort, wo dein Cursor steht. Das klappt in jeder App unter **X11 und Wayland**, auf GNOME, KDE, Cinnamon, XFCE, Sway, Hyprland und anderen Oberflächen. Flüster läuft als schlanker Hintergrunddienst mit **Flow-Leiste** unten in der Bildschirmmitte, wie auf Mac und Windows. Eingerichtet wird es mit einem Assistenten im Terminal.

**So funktioniert es:**
- **Diktat-Taste:** direkt über die Eingabegeräte des Kernels (`/dev/input`). Damit klappt sie unabhängig vom Desktop.
- **Einfügen:** Flüster legt den Text in die Zwischenablage und drückt Strg+V über eine eigene virtuelle Tastatur (`/dev/uinput`). In Terminals nimmt es automatisch Strg+Umschalt+V.
- **Erkennung:** whisper.cpp direkt im Programm, offline und kostenlos. Mit Grafikkarte läuft sie über Vulkan.

## Installation

**Du brauchst:**
- Linux auf x86_64 oder arm64 mit glibc, zum Beispiel Ubuntu 22.04+, Debian 12+, Fedora 39+, Arch, openSUSE oder Mint.
- PipeWire oder PulseAudio.
- Rund 1 GB Platz.

1. Terminal öffnen und einfügen:

   ```bash
   curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash
   ```

2. Der Assistent (`fluester setup`) startet von selbst und erledigt:
   - **Rechte:** Er nimmt dich in die Gruppe `input` auf und legt eine udev-Regel für `/dev/uinput` an. Das geht einmalig mit `sudo`, der Assistent fragt vorher.
   - **Werkzeuge:** Er schlägt fehlende Pakete passend zu deiner Distribution vor (`wl-clipboard` bzw. `xclip`, `pulseaudio-utils`, `libnotify`) und installiert sie auf Wunsch.
   - **Mikrofon:** Er listet alle Mikrofone mit Nummer auf. Du wählst eins und siehst den Pegel live, während du sprichst.
   - **Taste, Erkennung und Modell:** Er lädt **Large v3 Turbo** (547 MB) oder ein kleineres Modell herunter.
   - **Autostart:** Flüster startet ab dann bei jeder Anmeldung.
3. **Einmal ab- und wieder anmelden**, falls der Assistent dich in die Gruppe `input` aufgenommen hat. Flüster startet danach von selbst.

Installiert wird nur für deinen Nutzer, nach `~/.local/share/fluester/app`. Der Befehl `fluester` liegt in `~/.local/bin`.

---

## Bedienung

| Was | Wie |
| --- | --- |
| Diktieren | **Strg rechts halten** → sprechen → loslassen |
| Freihändig | **Strg rechts doppelt tippen** → sprechen → **einmal tippen** zum Beenden |
| Command Mode | Text markieren → **Strg rechts + Umschalt halten** → „mach das kürzer“ |
| Abbrechen | **Esc** |
| Zeilenumbruch | „neue Zeile“, „neuer Absatz“ |
| Korrigieren | „um 3, ich meine um 4“ → „um 4“ · „streich das“ |
| Liste | „erstens Milch, zweitens Brot, drittens Eier“ |

### Flow-Leiste

Während du diktierst, zeigt unten in der Bildschirmmitte eine schwarze Leiste, was gerade passiert:

| Leiste | Bedeutung |
| --- | --- |
| ● ▮▮▮▮▮ 0:03 | hört zu, Wellenform live (violett im Command Mode) |
| ••• Schreibt … | erkennt und bereinigt |
| ✓ 12 Wörter | eingefügt |
| ! Hinweis | z. B. „Strg rechts halten zum Sprechen“ nach zu kurzem Tippen, „Nichts verstanden“ oder ein Fehler |

Die Leiste nimmt nie den Fokus, der Text landet also immer im richtigen Fenster, und Klicks gehen durch sie hindurch. Sie läuft unter X11 und unter Wayland über Xwayland (GNOME, KDE, Sway, Hyprland). Zur Probe zeigt `fluester bar` sie 7 Sekunden lang. Gibt es keine X11-/Xwayland-Anzeige, meldet sich Flüster stattdessen über Benachrichtigungen. Ein leiser Ton markiert zusätzlich Start und Ende der Aufnahme.

### Mikrofon wählen

```bash
fluester mic
```

Das zeigt alle Mikrofone mit Nummer. Das aktuelle ist mit ● markiert, Lautsprecher-Monitore sind ausgeblendet:

```text
Mikrofone (Aufnahme über parecord):
  ● 0  Systemstandard – gerade: Eingebautes Tonsystem Analog Stereo
    1  Eingebautes Tonsystem Analog Stereo
    2  Yeti Stereo Microphone Analog Stereo
  Nummer wählen [Enter = so lassen]: 2
✓ Mikrofon: Yeti Stereo Microphone Analog Stereo
  Pegel testen? [J/n]
Pegel von Yeti Stereo Microphone Analog Stereo – sprich ein paar Worte …
  ▕██████████████████░░░░░░░░░░░░▏  61 %   noch 3 s
```

- **Direkt wählen:** mit Nummer (`fluester mic 2`) oder einem Stück vom Namen (`fluester mic yeti`).
- **Zurück auf den Systemstandard:** `fluester mic 0`.
- **Nur Pegel prüfen:** `fluester mic test`. Ob die Erkennung klappt, zeigt `fluester test`.
- **Laufendes Flüster:** Es nimmt die Wahl ab dem nächsten Diktat, ohne Neustart.
- **Mikrofon abgesteckt:** Flüster nimmt solange den Systemstandard und sagt einmal Bescheid. Steckst du es wieder an, gilt wieder deine Wahl.
- **Stummgeschaltet:** Ist das gewählte Mikrofon stumm, bietet `fluester mic` an, die Stummschaltung aufzuheben.

### Befehle

```text
fluester setup              Einrichtung erneut starten
fluester mic                Mikrofon wählen und Pegel testen
fluester mic list | test | <nummer>
fluester bar                Flow-Leiste zur Probe zeigen
fluester status             läuft er? Taste, Mikrofon, Erkennung, Probleme
fluester start | stop       Dienst starten / beenden (läuft unabhängig vom Terminal weiter)
fluester run                im Vordergrund laufen lassen (zeigt alle Meldungen, endet mit dem Terminal)
fluester toggle             freihändig starten/beenden – für eigene Tastenkürzel
fluester paste-last         letztes Diktat erneut einfügen
fluester test               5 Sekunden aufnehmen und erkennen (ohne Einfügen)
fluester transcribe a.wav   WAV-Datei erkennen
fluester model list | download <id> | delete <id>
fluester key groq <schlüssel>
fluester config             Einstellungen im Editor öffnen
fluester autostart on|off
```

**Ohne Gruppe `input`:** Leg im Desktop ein eigenes Tastenkürzel an, das `fluester toggle` ausführt. Unter GNOME geht das über **Einstellungen › Tastatur › Eigene Tastenkürzel**. Ein Druck startet das Diktat, der nächste beendet es. Der Eintrag „Flüster: Diktat an/aus“ steht nach der Einrichtung auch im App-Menü.

---

## Einstellungen

`fluester config` öffnet `~/.config/fluester/settings.json` im Editor. Ein laufendes Flüster übernimmt Änderungen sofort. Die wichtigsten Felder:

| Feld | Werte |
| --- | --- |
| `hotkey` | `rightCtrl` (Standard), `ctrlWin` (Strg + Super), `capsLock`, `rightAlt`, `f8` |
| `engine` | `whisperLocal` (Standard) oder `cloud` |
| `whisperModelId` | `turbo-q5` (Standard), `turbo`, `small`, `base` |
| `useGpu` | `true`: Grafikkarte über Vulkan nutzen, wenn vorhanden |
| `language` | `auto` oder z. B. `de-DE`, `en-US` |
| `cleanup` | `rules` (Standard) oder `llm` (Sprachmodell) |
| `cloudChat` | Anbieter fürs Sprachmodell, z. B. `{"providerId":"ollama","baseUrl":"http://localhost:11434/v1","model":"qwen2.5:7b"}` |
| `pasteShortcut` | `auto` (Terminals: Strg+Umschalt+V), `ctrl+v`, `ctrl+shift+v`, `shift+insert` |
| `insertMethod` | `paste` (Standard) oder `type` (tippt über `wtype`/`xdotool`) |
| `microphoneId` | leer = Systemstandard. Am einfachsten setzt du es mit `fluester mic` |
| `dictionary`, `snippets`, `styles` | Wörterbuch, Textbausteine und Stil je App-Gruppe, wie auf dem Mac |
| `sounds`, `notifications` | Töne und Benachrichtigungen an/aus |

**API-Schlüssel** setzt du mit `fluester key groq gsk_…`. Er liegt in `~/.config/fluester/keys.json`, die Datei ist nur für dich lesbar. Alternativ wirken auch die Umgebungsvariablen `GROQ_API_KEY` und `OPENAI_API_KEY`.

---

## Aktualisieren und entfernen

**Aktualisieren:** den Installationsbefehl nochmal ausführen. Ein laufender Dienst wird dabei neu gestartet.

**Entfernen:**
```bash
curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash -s -- --uninstall
```
Einstellungen (`~/.config/fluester`), Verlauf und Modelle (`~/.local/share/fluester`) bleiben dabei liegen. Das Skript zeigt den Befehl, um sie ganz zu löschen.

---

## Häufige Probleme

Der erste Schritt ist immer **`fluester status`**. Der Befehl listet erkannte Probleme samt Lösung auf. Jeder Schritt jedes Diktats steht außerdem im Protokoll: `tail -40 ~/.local/share/fluester/fluester.log`. Dort stehen Aufnahme, Erkennung mit Dauer und Einfügen, aber nie der diktierte Text.

**Es klappt einmal, danach nicht mehr.**
Ab Version 1.1.2 fängt Flüster das selbst ab:
- **Terminal geschlossen:** `fluester start` startet Flüster als eigenen Dienst (systemd-Einheit `fluester`, sonst in eigener Sitzung). Schließt du das Terminal, läuft Flüster weiter. Nur `fluester run` hängt bewusst am Terminal.
- **Erkennung hängt**, etwa weil der Grafikkarten-Treiber bei Vulkan klemmt: Nach einer Zeitgrenze startet Flüster neu und rechnet ab dann mit dem Prozessor (`useGpu` wird `false`). Du bekommst dazu eine Meldung.
- **Dienst hängt** an einem Werkzeug, das nicht antwortet: Ein Wächter startet Flüster nach 25 Sekunden neu.
- **Absturz:** Als systemd-Einheit startet Flüster nach 2 Sekunden von selbst wieder. Der Grund steht im Protokoll unter „ABSTURZ“.

Drückst du die Taste, während das letzte Diktat noch erkannt wird, kommt die Meldung „Moment …“, statt dass nichts passiert. Dauert die Erkennung spürbar lange, nimm ein kleineres Modell (`fluester model download small`).

**Die Taste reagiert nicht, oder „Kein Zugriff auf /dev/input/event…“.**
Flüster liest die Tastatur nur mit der Gruppe `input`. Die wirkt erst in Programmen, die nach dem nächsten Anmelden starten.
- **Nicht in der Gruppe:** `fluester setup` trägt dich ein. Von Hand geht es mit `sudo usermod -aG input $USER`.
- **Fedora Silverblue/Kinoite:** Meldet `usermod` „group 'input' does not exist in /etc/group“, übernimm die Gruppe zuerst mit `grep '^input:' /usr/lib/group | sudo tee -a /etc/group`. Das Setup erledigt das selbst.
- **Eingetragen, aber noch nicht neu angemeldet:** `fluester stop; fluester start`. Flüster startet dann über `sg input` sofort mit Tastaturzugriff. Nach dem nächsten Anmelden läuft es wieder ganz normal.
- **Prüfen:** `fluester selftest` zeigt in der Zeile „Gruppe „input““, ob die Gruppe fehlt, eingetragen oder aktiv ist. Mit `fluester run` siehst du alle Meldungen live.

**Keine Leiste zu sehen.**
- `fluester bar` zeigt sie zur Probe. Erscheint sie dort, aber nicht beim Diktieren, starte Flüster neu: `fluester stop; fluester start`.
- `fluester status` zeigt in der Zeile „Leiste“, wo sie sitzt oder warum sie aus ist, etwa wenn es keine X11-/Xwayland-Anzeige gibt. Dann kommt die Rückmeldung über Benachrichtigungen.
- Unter GNOME startet Xwayland bei Bedarf von selbst. Ist es ganz abgeschaltet, bleibt nur der Weg über Benachrichtigungen.

**Nur ein Ton, sonst passiert nichts.**
Die Taste wurde nur kurz getippt. Zum Diktieren **halten**, sprechen, loslassen. Für freihändig **doppelt tippen**. Die Leiste zeigt dazu einen Hinweis.

**Der Text landet nur in der Zwischenablage.**
`/dev/uinput` ist nicht beschreibbar. Führ `fluester setup` erneut aus. Danach zeigt `ls -l /dev/uinput` die Gruppe `input` mit den Rechten `crw-rw----`.

**Im Terminal erscheint `^V` statt des Texts.**
Mit `pasteShortcut` auf `auto` erkennt Flüster Terminals unter X11, Sway und Hyprland selbst. Unter GNOME und KDE auf Wayland lässt sich das aktive Fenster nicht abfragen. Stell dort `"pasteShortcut": "ctrl+shift+v"` ein, wenn du vor allem im Terminal diktierst.

**Die Aufnahme ist stumm, „Nichts verstanden“, oder das falsche Mikrofon nimmt auf.**
- Mit `fluester mic` wählst du das Mikrofon und siehst live, ob Ton ankommt.
- Bleibt der Pegel bei 0 %, ist das Mikrofon stummgeschaltet oder in den Ton-Einstellungen sehr leise gestellt.
- `fluester selftest` nimmt jedes Mikrofon kurz auf und zeigt den Pegel. So siehst du auf einen Blick, welches wirklich Ton liefert.

**„Aufnahme fehlgeschlagen“.**
Flüster nimmt über `parecord` auf, ohne das über `pw-record` (PipeWire), sonst über `arecord`. Welches Werkzeug läuft, zeigt `fluester mic list` in der ersten Zeile. Ein bestimmtes erzwingst du mit `FLUESTER_RECORDER=pw-record fluester run`. Fehlt `parecord`, installierst du es mit `sudo dnf install pulseaudio-utils` (Fedora) bzw. `sudo apt install pulseaudio-utils` (Ubuntu/Debian).

**Die Feststelltaste schaltet beim Diktieren um.**
Unter Linux kann Flüster Tasten lesen, aber nicht schlucken. Nimm daher lieber **Strg rechts**.

**Der Stil je App greift nicht.**
Unter GNOME und KDE auf Wayland ist das aktive Fenster nicht abfragbar. Dann gilt der Stil für „Alle anderen Apps“. Unter X11 (mit `xdotool`), Sway und Hyprland funktioniert die Zuordnung.

**Die Erkennung ist langsam.**
Prüf mit `fluester status`, welche Laufzeit läuft. „Vulkan“ heißt Grafikkarte, „Cpu“ heißt Prozessor. Für Vulkan brauchst du einen Vulkan-Treiber (Paket `mesa-vulkan-drivers` bzw. `vulkan-radeon`/`vulkan-intel`). Ohne Grafikkarte nimmst du `fluester model download small`.

Bleibt ein Problem, schick die Ausgabe von `fluester status` und `tail -50 ~/.local/share/fluester/fluester.log` in einem [Issue](https://github.com/KimJongUnNK/fluester/issues).

---

## Datenschutz

- Mit **Whisper lokal** und **Regeln** oder **Ollama** verlassen weder Ton noch Text deinen Rechner.
- Flüster liest die Tastatur nur, um die Diktat-Taste zu erkennen. Es speichert keine Tastendrücke.
- Kein Konto, kein Tracking.

---

## Selbst bauen

Du brauchst das [.NET 10 SDK](https://dotnet.microsoft.com/download). Unter Ubuntu installierst du es mit `sudo apt install dotnet-sdk-10.0`.

```bash
git clone https://github.com/KimJongUnNK/fluester.git && cd fluester
dotnet test dotnet/Fluester.Core.Tests                     # gemeinsame Logik
dotnet run --project linux/Fluester.Linux -- run           # Dienst im Vordergrund
scripts/package-dotnet.sh linux-x64 1.2.3 dist             # Paket wie im Release
dist/linux-x64/fluester selftest                           # Selbsttest
```

**Aufbau:**
- `dotnet/Fluester.Core` und `dotnet/Fluester.Engine`: Die Logik ist dieselbe wie unter Windows und auf dem Mac.
- `linux/Fluester.Linux`: Dienst, evdev-Tastatur, uinput-Einfügen, Aufnahme und Mikrofon-Auswahl über `parecord`/`pw-record`/`arecord`, Desktop-Anbindung, Assistent.

## Stand

Die CI prüft jede Version automatisch auf Ubuntu:
- Diktat-Taste über eine virtuelle Tastatur und `/dev/input`
- Mikrofon-Auswahl mit virtuellen Mikrofonen (eins mit Ton, eins stumm), Rückfall bei abgestecktem Mikrofon, Wechsel im laufenden Dienst
- Zwischenablage unter X11
- echte Spracherkennung mit Whisper
- Installer

Von Hand auf echten Desktops wurde Version 1.1 noch nicht getestet. Rückmeldungen zu deiner Distribution helfen.
