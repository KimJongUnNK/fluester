<p align="center">
  <img src="docs/icon.svg" width="128" height="128" alt="Flüster">
</p>

<h1 align="center">Flüster</h1>

<p align="center">
  <b>Dictate into any app. Hold a key, speak, release.</b><br>
  Free and offline. For macOS, Windows and Linux. An open alternative to Wispr Flow.
</p>

<p align="center"><a href="README.md">Deutsch</a></p>

---

Hold a key, talk, let go. Clean text lands wherever your cursor is. Fillers disappear and punctuation is added. A self-correction like "at 3, I mean at 4" becomes "at 4".

> The apps' interface is in **German**. The guides give the German labels in quotes.

## Install

| System | One command | Guide |
| --- | --- | --- |
| **macOS** (Apple silicon, 14+) | `curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh \| bash` | [macOS](docs/macos.en.md) |
| **Windows** (10/11, 64-bit) | `irm https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.ps1 \| iex` | [Windows (DE)](docs/windows.md) |
| **Linux** (x64/arm64, X11 & Wayland) | `curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh \| bash` | [Linux (DE)](docs/linux.md) |

Manual downloads are on the [releases page](https://github.com/KimJongUnNK/fluester/releases/latest).

## Keys

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Dictate (hold) | Fn / 🌐 | Right Ctrl | Right Ctrl |
| Hands-free | double-tap | double-tap | double-tap |
| Command Mode | Fn + ⌃ | Right Ctrl + Shift | Right Ctrl + Shift |
| Cancel | Esc | Esc | Esc |

## Recognition and cleanup

**Recognition:**
- **Offline:** whisper.cpp on all three systems. Windows and Linux use the GPU through Vulkan; the Mac can also use Apple SpeechAnalyzer.
- **Cloud:** Groq (free tier), OpenAI or any OpenAI-compatible server.

**Cleanup:**
- Rules (instant, offline).
- An LLM through Groq, OpenAI or local Ollama.
- On the Mac, also Apple Intelligence.

## Privacy

With local recognition and local or rule-based cleanup, no audio or text leaves your computer. There's no account and no tracking.

## License

[MIT](LICENSE). Flüster is an independent project. It contains no Wispr Flow code and is not affiliated with Wispr. "Wispr Flow" is a trademark of its respective owner.
