# FlowAI — Windows AI Cursor Companion

A native Windows desktop application that brings an intelligent, voice-driven AI companion to your cursor. Inspired by the open-source [Clicky](https://github.com/farzaa/clicky) project.

![FlowAI](.assets/banner.png)

## Features

- **Green animated cursor companion** — smooth spring-damped follow, Bézier target flight, bounding-box highlights and arrival pulse
- **Voice interaction** — press `Ctrl+Alt+Space` to talk; FlowAI understands natural language questions about your screen
- **Screen understanding** — GDI screen capture + Windows UI Automation + optional vision AI (qwen3-vl:2b via Ollama)
- **Local-first AI** — runs entirely offline via [Ollama](https://ollama.com); no mandatory cloud subscription
- **Optional Kimi API** — enable for enhanced reasoning; key stored with Windows DPAPI (never hardcoded)
- **Self-learning** — remembers corrections, teaches workflows, persists everything in a local SQLite database
- **Windows automation** — cursor guidance, confirmed clicks, text entry, keyboard shortcuts and scrolling

---

## Quick Start

### Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10/11 (64-bit) | Required |
| .NET 8 SDK | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Ollama | [Download](https://ollama.com) — `ollama serve` must be running |
| `qwen3-vl:2b` model | `ollama pull qwen3-vl:2b` (1.9 GB, vision-capable) |

### Build and Run

```powershell
# Clone
git clone <repo-url>
cd FlowAI

# Restore and build
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet build FlowAI.sln

# Launch
Start-Process "src\FlowAI.App\bin\Debug\net8.0-windows\FlowAI.exe"
```

### First Use

1. Start Ollama: `ollama serve`
2. Launch FlowAI — a green orb appears in your system tray
3. Press `Ctrl+Alt+Space` and speak: *"Where is the Settings button?"*
4. Watch FlowAI fly to the target and highlight it in emerald green

---

## Architecture

```
FlowAI.App/          ← WPF entry point, CompanionManager, hotkey, tray, panel
FlowAI.Core/         ← State machine, models, interfaces, protocol parser, settings
FlowAI.AI/           ← Ollama + Kimi providers, DPAPI credential store
FlowAI.Vision/       ← GDI screen capture, UI Automation discovery, hybrid locator
FlowAI.Audio/        ← WASAPI recorder, Whisper STT, Edge TTS, Windows Speech
FlowAI.Cursor/       ← Spring physics, Bézier flight, emerald design tokens
FlowAI.Overlay/      ← Click-through WPF transparent overlays (one per monitor)
FlowAI.Memory/       ← SQLite persistence for preferences, corrections, workflows
FlowAI.Learning/     ← Teaching sessions, correction engine, workflow matching
FlowAI.Automation/   ← Windows automation engine with confirmation gating
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed component diagrams.

---

## Configuration

Settings are stored in `%APPDATA%\FlowAI\settings.json`.

Key settings:

| Setting | Default | Description |
|---|---|---|
| `AIProvider` | `Ollama` | `Ollama` or `Kimi` |
| `OllamaModel` | `qwen3-vl:2b` | Vision-capable model |
| `SttProvider` | `Whisper` | `Whisper` or `WindowsSpeech` |
| `WhisperModel` | `base` | `tiny`, `base`, or `small` |
| `TtsProvider` | `EdgeTts` | `EdgeTts` or `WindowsSpeech` |
| `Hotkey` | `Ctrl+Alt+Space` | Push-to-talk shortcut |
| `RequireConfirmationForConsequential` | `true` | Confirm before automation |
| `StoreScreenshotsWithWorkflows` | `false` | Privacy: off by default |

---

## Testing

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet test FlowAI.sln -v minimal
```

| Test suite | Tests | Status |
|---|---|---|
| FlowAI.Core.Tests | 7 | ✅ |
| FlowAI.AI.Tests | 3 | ✅ |
| FlowAI.Cursor.Tests | 3 | ✅ |
| FlowAI.Vision.Tests | 3 | ✅ |
| FlowAI.Integration.Tests | 4 | ✅ |

---

## Privacy

- Screen captures are processed locally by default (Ollama, no internet).
- Screenshots are **never stored** unless you explicitly enable workflow screenshots.
- Sensitive form fields are masked before AI processing (configurable).
- Kimi API is **opt-in only** — a warning is shown if you enable it.
- API keys are stored with Windows DPAPI (per-user encryption, never in settings.json).

---

## License

MIT — see [LICENSE](LICENSE).

Inspired by [Clicky](https://github.com/farzaa/clicky) (MIT) by Farza Majeed.
Windows port reference: [clicky-windows](https://github.com/mesomya/clicky-windows) (MIT).
