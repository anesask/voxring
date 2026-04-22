# VoxRing

**Hold. Speak. Release. Your voice, anywhere.**

VoxRing is a voice-to-everything plugin for Logitech MX Master 4 and MX Creative Console. It turns a single mouse button into a dictation engine, an AI writing assistant, a voice commander, and a conversational AI: all offline by default.

Built for the [Logitech DevStudio Challenge 2026](https://devstudiologitech2026.devpost.com), Actions SDK category. By Anes Mulalic.

---

## What it does

Hold the side button, speak, release. VoxRing transcribes your voice and routes the result to wherever you need it: clipboard, email, Slack, Discord, Teams, calendar, WhatsApp, Notion, or Telegram. AI reformats the transcript for each destination automatically. No keyboard required.

**33 actions across 8 groups:**

- **Voice**: Dictate, Push to Talk, Voice Assistant, Voice Note, Read Back, Replay Last
- **Send**: AI-formatted send to 9 destinations, transcript history folder
- **Quick Send**: one-gesture voice-to-destination for 7 targets
- **Text Tools**: AI toggle, Noise Gate, Filler Cleaner, Case Transform, Translate, Append, Edit, Regenerate
- **Controls**: Mute Mic, Cancel, Open Voice Notes
- **Settings**: Language Toggle, full settings panel
- **Meters**: live audio level dial, rolling waveform, word count
- **Utilities**: Guitar Tuner easter egg

**9 destinations:** Clipboard, Type Out, Email, Slack, Discord, Teams, Calendar, WhatsApp, Notion, Telegram.

**5 AI providers:** Claude Sonnet, GPT-4o-mini, Gemini, DeepSeek, Perplexity: all behind a single interface.

---

## Installation

Download **[VoxRing-1.0.0.lplug4](https://github.com/anesask/voxring/releases/latest)** and double-click to install via Logi Options+. Requires Logi Options+ 6.0 or later. Windows only.

On first launch, VoxRing downloads the speech recognition models (~500 MB total) and installs Piper TTS in the background. Recording is available immediately using the built-in Vosk model; Whisper loads once its download completes.

---

## Setup

Open the VoxRing Settings action in Logi Options+ to configure:

- **Microphone**: select your preferred input device
- **Speech engine**: Vosk (fast, 40 MB) or Whisper Small (accurate, 460 MB)
- **Language**: Auto-detect, English, or German
- **AI provider**: paste your API key for Claude, OpenAI, or any supported provider
- **Destinations**: Slack and Discord webhook URLs, Teams webhook URL

All API keys and webhook URLs are encrypted at rest using Windows DPAPI.

---

## How it works

Voice is captured via NAudio at 16 kHz. Speech recognition runs fully offline using Vosk or Whisper.net: no audio leaves your machine unless you opt into cloud AI formatting. When AI formatting is enabled, the transcript is sent to your chosen provider with a destination-specific prompt (email tone for email, Slack style for Slack, etc.). Text-to-speech for Voice Assistant mode uses Piper neural voices offline.

See the repository wiki for architecture details, extension points, and how to add new destinations or AI providers.

---

## Building from source

Prerequisites: .NET 8 SDK, Logi Options+ installed.

```bash
git clone https://github.com/anesask/voxring.git
cd voxring
dotnet build src/VoxRingPlugin.csproj
```

The post-build step deploys the plugin to Logi Plugin Service automatically. Restart Logi Options+ to load the updated plugin.

For VS Code users, the `.vscode/` folder includes a pre-configured build task and a "Attach to Logi Plugin Service" debug launch configuration.

---

## Third-party components

VoxRing is built on the following open-source libraries. Full license texts are in [NOTICES.md](NOTICES.md).

| Component | License | Purpose |
|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | MIT | Microphone capture |
| [Vosk](https://github.com/alphacep/vosk-api) | Apache 2.0 | Offline speech recognition (fast engine) |
| [Vosk model (en-us-small)](https://alphacephei.com/vosk/models) | Apache 2.0 | English speech model |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) | MIT | Offline speech recognition (accurate engine) |
| [whisper.cpp](https://github.com/ggerganov/whisper.cpp) | MIT | Native Whisper inference |
| [OpenAI Whisper model weights](https://github.com/openai/whisper) | MIT | Speech model weights |
| [Piper TTS](https://github.com/rhasspy/piper) | MIT | Offline neural text-to-speech |
| [Piper voice models](https://huggingface.co/rhasspy/piper-voices) | CC0 1.0 | English (Amy) and German (Thorsten) voices |
| [Logi Actions SDK](https://github.com/Loupedeck/PluginSdk) | Logitech SDK License | Plugin API |

---

## License

MIT License. Copyright (c) 2026 Anes Mulalic. See [LICENSE](LICENSE) for details.

Third-party components are subject to their own licenses as listed in [NOTICES.md](NOTICES.md).
