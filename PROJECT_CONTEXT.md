# PROJECT_CONTEXT.md — FlowAI

This file captures the full context for cross-session and cross-account handoffs.

---

## What is FlowAI?

FlowAI is a Windows desktop AI companion that:
1. Listens to your voice (Ctrl+Alt+Space push-to-talk)
2. Captures your screen
3. Sends both to a local vision AI (qwen3-vl:2b via Ollama)
4. Flies an animated green cursor to any UI element the AI identifies
5. Shows a bounding-box highlight and speaks the response
6. Learns your corrections and remembers your workflows

It is a native Windows .NET 8 WPF application, NOT Electron, NOT a web app.

---

## Target Hardware

| Spec | Value |
|---|---|
| RAM | 8 GB (design target) |
| GPU | Integrated only (no discrete GPU assumed) |
| AI model | qwen3-vl:2b (Q4_K_M, 1.9 GB VRAM/RAM) |
| STT model | Whisper Base (~145 MB) |
| OS | Windows 10/11 x64 |

---

## Repository State

- **Git remote**: original project remote preserved
- **Main branch**: `main`
- **Last commit**: `feat: implement FlowAI.App - complete WPF application entry point`
- **Build**: ✅ Debug + Release, 0 errors, 0 warnings
- **Tests**: ✅ 20/20 passing

---

## Required Environment

```
%APPDATA%\FlowAI\             ← Settings, DB, crash log, model cache
  settings.json               ← Auto-created on first run
  flowai_memory.db            ← SQLite (created on first run)
  kimi_secure.dat             ← Kimi API key (DPAPI-encrypted, only if set)
  models\                     ← Whisper model files (auto-downloaded)
  crash.log                   ← Crash log
```

No environment variables required at runtime. Settings are in `settings.json`.

---

## Runtime Dependencies

| Dependency | How to satisfy |
|---|---|
| .NET 8 runtime | Bundled with the app (self-contained publish) or installed separately |
| Ollama | `ollama serve` must be running; `qwen3-vl:2b` model pulled |
| Windows 10/11 | Required for WASAPI, UI Automation, DPAPI |

```powershell
# Check Ollama
ollama list   # should show qwen3-vl:2b
ollama serve  # starts the API server

# Pull model if missing
ollama pull qwen3-vl:2b
```

---

## Build & Launch

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$env:DOTNET_CLI_HOME = "C:\Users\Ethan David\OneDrive\Desktop\FlowAI\.dotnet_cli"
$env:DOTNET_NOLOGO = "1"

# Build
& $dotnet build "C:\Users\Ethan David\OneDrive\Desktop\FlowAI\FlowAI.sln"

# Test
& $dotnet test "C:\Users\Ethan David\OneDrive\Desktop\FlowAI\FlowAI.sln" -v minimal

# Run (Release)
Start-Process "C:\Users\Ethan David\OneDrive\Desktop\FlowAI\src\FlowAI.App\bin\Release\net8.0-windows\FlowAI.exe"
```

**Always run dotnet commands with `BypassSandbox: true`** in the Antigravity agent.

---

## Architecture Summary

```
CompanionManager (orchestrator)
    ├── GlobalHotkeyMonitor ──────────── Ctrl+Alt+Space → HotkeyPressed event
    ├── WasapiAudioRecorder ──────────── PCM16 audio capture
    ├── WhisperTranscriptionService ───── PCM16 → text
    ├── ScreenCaptureService ──────────── All screens → JPEG bytes
    ├── OllamaProvider ────────────────── text + images → AI response
    ├── PointProtocolParser ───────────── [POINT:x,y:label] extraction
    ├── OverlayWindowManager ──────────── FlyToTarget → BezierFlightPath
    └── EdgeTtsService ────────────────── text → neural speech

App.xaml.cs (entry point)
    ├── Builds DI container
    ├── Starts TrayIconManager on STA thread (Windows Forms)
    ├── Initializes OverlayWindowManager (one WPF window per monitor)
    ├── Calls CompanionManager.InitialiseAsync()
    └── Shows CompanionPanelWindow
```

---

## Remaining Work

Priority order:

1. **Full memory browser** — view/delete preferences, corrections, workflows via DataGrid UI
2. **Streaming response display** — show AI tokens as they arrive (wire `StreamChatAsync`)
3. **Workflow execution engine** — replay recorded workflows step-by-step with confirmation
4. **Audio device selector** — populate Settings mic/speaker dropdowns with actual device names
5. **High-DPI fix** — multi-monitor coordinate mapping refinement for non-100% scaling
6. **Release packaging** — MSIX or WiX installer
7. **Privacy blur** — OCR-based masking of password fields before screen capture

---

## Known Blockers for Future Agents

- None currently blocking — application builds, runs, and all tests pass.
- Edge TTS may return 403 if `EdgeVersion` constant in `EdgeTtsService.cs` is stale.
  Workaround: change `TtsProvider` to `WindowsSpeech` in Settings, or bump `EdgeVersion`.

---

## Key Files for Future Reference

| File | Purpose |
|---|---|
| `AGENTS.md` | Architecture reference for AI agents |
| `IMPLEMENTATION_STATUS.md` | Feature completion tracking |
| `KNOWN_ISSUES.md` | Active bugs and workarounds |
| `TESTING.md` | Test suite reference |
| `src/FlowAI.App/CompanionManager.cs` | Main pipeline orchestrator |
| `src/FlowAI.App/App.xaml.cs` | DI composition root |
| `src/FlowAI.Core/Interfaces/CoreInterfaces.cs` | All service interfaces |
| `src/FlowAI.Core/Parsing/PointProtocolParser.cs` | AI coordinate protocol |
| `src/FlowAI.Overlay/OverlayWindow.cs` | Animated transparent overlay |
