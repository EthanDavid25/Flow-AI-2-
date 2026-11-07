# KNOWN_ISSUES.md — FlowAI

## Active Issues

### KI-001 — Edge TTS 403 if EdgeVersion is stale
**Severity**: Medium
**Component**: `FlowAI.Audio/Tts/EdgeTtsService.cs`
**Symptom**: `EdgeTtsService.SpeakAsync` throws WebSocketException (403).
**Fix**: Bump `EdgeVersion` constant to the latest Edge browser version string.
**Current value**: `"1-143.0.3650.75"`
**Workaround**: Switch TTS provider to `WindowsSpeech` in Settings.

### KI-002 — WASAPI init may hang on Bluetooth audio devices
**Severity**: Low
**Component**: `FlowAI.Audio/Capture/WasapiAudioRecorder.cs`
**Symptom**: App appears to freeze briefly on first push-to-talk if Bluetooth headset is selected as default mic.
**Root cause**: WASAPI exclusive mode init blocks on flaky BT device.
**Workaround**: Select a wired microphone as the default Windows audio input.
**Fix planned**: Add `Task.Run` with 8-second timeout around WASAPI init.

### KI-003 — High-DPI coordinate offset on secondary monitors
**Severity**: Low
**Component**: `FlowAI.Core/Geometry/CoordinateTransform.cs`
**Symptom**: POINT/BOX coordinates may be off by a few pixels on monitors with scaling ≠ 100%.
**Root cause**: GDI captures at physical pixels; WPF overlay uses DIPs; DPI scaling is not yet fully propagated.
**Workaround**: Set both monitors to 100% display scaling temporarily.

### KI-004 — Whisper model download requires network on first use
**Severity**: Info
**Component**: `FlowAI.Audio/Transcription/WhisperTranscriptionService.cs`
**Symptom**: First push-to-talk takes 30–120 seconds (Whisper Base is ~145 MB).
**Expected**: Progress shown in status bar; subsequent uses are instant.

### KI-005 — Memory viewer is a placeholder
**Severity**: Low  
**Component**: `FlowAI.App/App.xaml.cs` → `ShowMemory()`
**Symptom**: Clicking "Memory" shows a MessageBox instead of a real viewer.
**Fix**: Implement `MemoryViewerWindow` with DataGrid for all memory tables.

### KI-006 — Workflow execution not implemented
**Severity**: Low
**Component**: `FlowAI.Learning/LearningEngine.cs`
**Symptom**: Workflows can be recorded and stored but cannot be replayed automatically.
**Fix**: Implement step-by-step execution loop in `CompanionManager`.

---

## Closed Issues

*(none yet — project in initial release)*

---

## Environment Gotchas

- `dotnet` is not on the system PATH — use `$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe`
- Always set `$env:DOTNET_CLI_HOME` to avoid permission errors in temp dirs
- Sandbox mode blocks WASAPI/DPAPI — use `BypassSandbox: true` for `dotnet test/run`
- GDI `CopyFromScreen` returns "Handle is invalid" in test sandbox — handled with graceful fallback
