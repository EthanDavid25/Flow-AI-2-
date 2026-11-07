# AGENTS.md — FlowAI Architecture Reference

This file is the primary reference for AI coding agents continuing FlowAI development.
Read this before making any changes.

---

## Project Layout

```
FlowAI/
├── src/
│   ├── FlowAI.App/           ← WPF app entry point (owns everything)
│   ├── FlowAI.Core/          ← Shared models, interfaces, enums, state machine
│   ├── FlowAI.AI/            ← AI provider abstraction (Ollama + Kimi)
│   ├── FlowAI.Vision/        ← Screen capture + UI element discovery
│   ├── FlowAI.Audio/         ← Microphone capture + STT + TTS
│   ├── FlowAI.Cursor/        ← Physics simulation + design tokens
│   ├── FlowAI.Overlay/       ← Click-through WPF transparent overlays
│   ├── FlowAI.Memory/        ← SQLite persistence
│   ├── FlowAI.Learning/      ← Teaching sessions + correction engine
│   └── FlowAI.Automation/    ← Windows desktop automation engine
├── tests/
│   ├── FlowAI.Core.Tests/
│   ├── FlowAI.AI.Tests/
│   ├── FlowAI.Vision.Tests/
│   ├── FlowAI.Cursor.Tests/
│   └── FlowAI.Integration.Tests/
├── references/
│   ├── clicky-original/      ← macOS Swift reference (do not copy Swift)
│   └── clicky-windows/       ← C# Windows port reference
└── docs/
```

---

## Critical Architecture Rules

### Thread Model

| Thread | Purpose |
|---|---|
| WPF UI thread (STA) | WPF windows, Dispatcher, `CompanionPanelWindow`, overlays |
| Hook thread (STA) | `GlobalHotkeyMonitor` — WH_KEYBOARD_LL + message pump |
| Tray thread (STA) | `TrayIconManager` — NotifyIcon + Windows Forms message pump |
| ThreadPool | All async pipelines (AI calls, capture, STT, TTS) |

**CRITICAL**: WH_KEYBOARD_LL hook **must** run on its own STA thread with its own message pump.
Installing it on the UI thread causes deadlocks. See `GlobalHotkeyMonitor.cs`.

### Coordinate Protocol

The AI returns target coordinates embedded in its response:
```
[POINT:x,y:label]           — single point (absolute screen pixels)
[POINT:x,y:label:screenN]   — with monitor index
[BOX:x,y,w,h:label]        — bounding box
[BOX:x,y,w,h:label:screenN]
```

Parsed by `FlowAI.Core.Parsing.PointProtocolParser`.
Converted to WPF DIPs by `FlowAI.Core.Geometry.CoordinateTransform`.

### Overlay Capture Exclusion

```csharp
// In OverlayWindow.OnSourceInitialized:
DisplayAffinity affinity = WDA_EXCLUDEFROMCAPTURE;  // 0x11
SetWindowDisplayAffinity(hwnd, (uint)affinity);
```

This prevents the overlay from appearing in GDI screen captures, so the AI never sees its own UI.

### DI Composition Root

All services are composed in `App.xaml.cs` → `BuildServices()`.
The composition root is the ONLY place where concrete types should be named.
Everywhere else use interfaces from `FlowAI.Core.Interfaces`.

### Settings

`FlowAISettings.Current` is a thread-safe singleton loaded from `%APPDATA%\FlowAI\settings.json`.
Never hardcode configuration values — always read from settings.

### DPAPI Key Storage

Kimi API key → `CredentialStore.SaveSecret("kimi", key)` / `LoadSecret("kimi")`.
Uses Windows DPAPI (per-user, machine-bound). The file is `%APPDATA%\FlowAI\kimi_secure.dat`.

---

## Service Implementations

| Interface | Concrete Class | Notes |
|---|---|---|
| `IAIProvider` | `OllamaProvider` / `KimiProvider` | Selected by `AIProviderFactory` |
| `IScreenCaptureService` | `ScreenCaptureService` | GDI, resizes to 1280px for vision |
| `IElementDiscoveryService` | `UIAutomationDiscoveryService` | Also has `HybridElementLocator` |
| `IAudioRecordingService` | `WasapiAudioRecorder` | NAudio WASAPI |
| `ITranscriptionService` | `WhisperTranscriptionService` | Whisper.net, auto-downloads model |
| `ITextToSpeechService` | `EdgeTtsService` | Free Microsoft Edge neural TTS |
| `IMemoryStore` | `SqliteMemoryStore` | Microsoft.Data.Sqlite |
| `ILearningEngine` | `LearningEngine` | Teaching, corrections, workflows |
| `IComputerAutomationService` | `WindowsAutomationEngine` | Win32 + UI Automation |

---

## Key File Locations

| File | Purpose |
|---|---|
| `src/FlowAI.App/App.xaml.cs` | Entry point, DI composition, lifecycle |
| `src/FlowAI.App/CompanionManager.cs` | Central orchestrator |
| `src/FlowAI.App/Hotkey/GlobalHotkeyMonitor.cs` | WH_KEYBOARD_LL Ctrl+Alt+Space |
| `src/FlowAI.App/Tray/TrayIconManager.cs` | System tray icon |
| `src/FlowAI.App/UI/CompanionPanelWindow.xaml` | Main companion panel |
| `src/FlowAI.App/UI/SettingsWindow.xaml` | Settings UI |
| `src/FlowAI.Core/StateMachine/FlowAIStateManager.cs` | 12-state machine |
| `src/FlowAI.Core/Parsing/PointProtocolParser.cs` | POINT/BOX tag parser |
| `src/FlowAI.Overlay/OverlayWindow.cs` | Transparent animated overlay |
| `src/FlowAI.Memory/SqliteMemoryStore.cs` | Full SQLite CRUD |
| `src/FlowAI.Learning/LearningEngine.cs` | Teaching + correction logic |

---

## Build Commands

```powershell
# Set up paths (required — dotnet not on PATH by default)
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$env:DOTNET_CLI_HOME = "$pwd\.dotnet_cli"
$env:DOTNET_NOLOGO = "1"

# Build
& $dotnet build FlowAI.sln

# Test
& $dotnet test FlowAI.sln -v minimal

# Release build
& $dotnet build FlowAI.sln -c Release

# Run
Start-Process "src\FlowAI.App\bin\Release\net8.0-windows\FlowAI.exe"
```

**Always run dotnet commands with `BypassSandbox: true`** in the Antigravity agent.

---

## Remaining Work (as of Nov 2026)

- [ ] Memory viewer window (currently shows MessageBox placeholder)
- [ ] Real-time streaming response display in companion panel
- [ ] Audio device selector in Settings (mic/speaker dropdowns)
- [ ] `qwen3-vl:2b` prompt tuning for more consistent BOX/POINT output
- [ ] High-DPI coordinate mapping refinement for multi-monitor setups
- [ ] Workflow execution engine (run saved workflows step-by-step)
- [ ] Release packaging (WiX installer or MSIX)

---

## Known Issues

See [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
