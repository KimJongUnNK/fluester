<p align="center">
  <img src="docs/icon.svg" width="128" height="128" alt="Flüster">
</p>

<h1 align="center">Flüster</h1>

<p align="center">
  <b>Dictate into any Mac app. Hold Fn, speak, release.</b><br>
  Free. Fully offline if you want. An open alternative to Wispr Flow.
</p>

<p align="center">
  <a href="#install"><b>Install</b></a> ·
  <a href="#set-up">Set up</a> ·
  <a href="#usage">Usage</a> ·
  <a href="#troubleshooting">Help</a> ·
  <a href="README.md">Deutsch</a>
</p>

---

Hold **Fn**, talk, let go. Clean text lands wherever your cursor is: WhatsApp, Mail, Slack, Notion, Word, the browser, the terminal. Filler words disappear and punctuation is added. A self-correction like "at 3, I mean at 4" becomes "at 4".

> The app's interface is in **German**. This guide gives the German labels in quotes so you can find every setting.

## Features

- **Hold to talk.** Double-tap for hands-free. Esc cancels.
- **Cleanup.** Fillers and stutters are removed and self-corrections applied. "neue Zeile" (new line) works as a command, and spoken lists are formatted.
- **Command Mode.** Select text, hold **Fn + ⌃**, then say "make this more formal" or "translate to German".
- **Style per app.** Casual in WhatsApp, formal in Mail, neutral in Slack.
- **Personal dictionary** for names and brands.
- **Snippets.** Say "my address" and the full address is inserted.
- **History and stats.** Words per minute and time saved, stored locally only.
- **Languages.** German, English, mixed, and 100+ more.

| Recognition | Cost | Offline | Setup |
| --- | --- | --- | --- |
| **Apple** (SpeechAnalyzer, macOS 26) | free | ✓ | none |
| **Local Whisper** (whisper.cpp, Large v3 Turbo) | free | ✓ | 2 commands, 547 MB |
| **Cloud** (Groq, OpenAI, custom server) | Groq free tier | – | API key |

Cleanup runs through one of three options:
- **Apple Intelligence**, on device.
- **An LLM:** Groq, OpenAI or **local Ollama**.
- **Rules only.**

---

## Install

**Requirements:** a Mac with **Apple silicon** (M1 or later) and **macOS 14+**. macOS 26 is recommended.

### Option A: one command (recommended)

Open **Terminal** (**⌘ + Space**, type "Terminal", Enter) and paste:

```bash
curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash
```

The script downloads the latest release, installs it to **Applications** and launches it. If it asks for a password, it means your Mac login password. Nothing is shown while you type.

### Option B: manual

1. **[Download Fluester-macOS.zip](https://github.com/KimJongUnNK/fluester/releases/latest/download/Fluester-macOS.zip)** and double-click it.
2. Drag **Flüster.app** into **Applications**.
3. Flüster is not notarized by Apple, so macOS blocks the first launch. Choose one:
   - **Terminal:**
     ```bash
     xattr -dr com.apple.quarantine "/Applications/Flüster.app"
     ```
   - **Without Terminal:** open Flüster and dismiss the warning. Then go to **System Settings › Privacy & Security**, scroll down, click **"Open Anyway"** and confirm.

---

## Set up

> **Flüster has no Dock icon.** It lives in the **menu bar** (top right) as a waveform symbol.

The setup window opens on first launch:

1. **Microphone** ("Mikrofon"): click **"Erlauben"** (Allow).
2. **Accessibility** ("Bedienungshilfen"): click **"Erlauben"**, then switch **Flüster** on in System Settings. Flüster needs this for the Fn key and for inserting text.
3. **Speech Recognition** ("Spracherkennung"): allow it. This is only used by the Apple engine.
4. **Fn/🌐 key:** in **System Settings › Keyboard**, set **"Press 🌐 key to"** to **"Do Nothing"**. Otherwise the emoji picker opens every time.
   Instead of Fn you can use **right ⌥**, **right ⌘** or **right ⌃** as the dictation key.
5. Click into the test field, hold **Fn**, speak and release. Then click **"Los geht's"** (Let's go).

To start Flüster at login: **menu bar icon › Einstellungen › Allgemein › "Bei Anmeldung starten"**.

### Better recognition: local Whisper (recommended)

```bash
brew install whisper-cpp
```

Then go to **Einstellungen › Erkennung**, choose **"Whisper lokal (whisper.cpp)"** and click **"Laden"** next to **"Large v3 Turbo (empfohlen)"**.

### Cloud (Groq)

1. Create a free key at [console.groq.com/keys](https://console.groq.com/keys).
2. Choose **"Cloud (Groq / OpenAI)"** and paste the key into **"API-Schlüssel"**.
3. Click **"Verbindung testen"** to check the connection.

---

## Usage

| What | How |
| --- | --- |
| Dictate | **Hold Fn** → speak → release |
| Hands-free | **Double-tap Fn** → speak → **tap Fn** to finish |
| Command Mode | select text → **hold Fn + ⌃** → "make it shorter" |
| Cancel | **Esc** |
| New line | "new line" / "neue Zeile" |
| Self-correction | "at 3, I mean at 4" → "at 4" |

## Update

Run the install command again. Afterwards, re-allow **Accessibility** once, because every build has a new signature.

## Uninstall

```bash
curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash -s -- --uninstall
rm -rf ~/Library/Application\ Support/Fluester   # history, settings, models
```

---

## Troubleshooting

- **Fn does nothing.**
  - Accessibility is missing or stale after an update.
  - Fix: in **System Settings › Privacy & Security › Accessibility**, remove Flüster (–), add it again (+) and switch it on.
- **The emoji picker opens.**
  - Set **"Press 🌐 key to" → "Do Nothing"**.
- **"Flüster is damaged" or "can't be opened".**
  - Run the `xattr` command from Option B, or use Option A.
- **`brew` says "/opt/homebrew is not writable".**
  - Run `sudo chown -R $(whoami) /opt/homebrew`, then `brew update`.
- **`brew` says "No available formula".**
  - The formula is spelled **`whisper-cpp`**.
- **Nothing gets inserted.**
  - Go to **Einstellungen › Allgemein › Einfügen** and switch to **"Tippen"** (typing).
  - Every dictation is also kept in the history.

Anything else? [Open an issue](https://github.com/KimJongUnNK/fluester/issues).

## Privacy

With Apple or local Whisper, plus Apple Intelligence, Ollama or rules for cleanup, no audio or text leaves your Mac.

- History and settings stay in `~/Library/Application Support/Fluester`.
- API keys live in the macOS Keychain.
- No account, no tracking.

## Build from source

Requires **Xcode 26** (macOS 26 SDK).

```bash
git clone https://github.com/KimJongUnNK/fluester.git && cd fluester
INSTALL=1 ./scripts/build-app.sh
```

## License

[MIT](LICENSE). Flüster is an independent project. It contains no Wispr Flow code and is not affiliated with Wispr. "Wispr Flow" is a trademark of its respective owner.
