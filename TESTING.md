# TESTING.md — FlowAI

## Running Tests

```powershell
# Full test suite (all projects)
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$env:DOTNET_CLI_HOME = "$pwd\.dotnet_cli"
$env:DOTNET_NOLOGO = "1"

& $dotnet test FlowAI.sln -v minimal
```

Run with `BypassSandbox: true` in Antigravity agent (WASAPI/DPAPI require real permissions).

## Test Projects

### FlowAI.Core.Tests (7 tests)
Tests the state machine, protocol parser, coordinate geometry, and settings.

| Test | What it covers |
|---|---|
| `StateMachine_TransitionsCorrectly_AndFiresEvent` | State machine transitions + event |
| `PointProtocolParser_ParsesPointTags_AndCleansSpokenText` | POINT tag parsing |
| `PointProtocolParser_ParsesBoxTags_AndComputesCenter` | BOX tag + center computation |
| `PointProtocolParser_HandlesMultipleTags_Cleanly` | Multiple mixed tags |
| `CoordinateTransform_ConvertsDipsAndPixelsAccurately` | DIP↔pixel conversion |
| `CoordinateTransform_MapsNormalizedCoordinates_WithNegativeOrigins` | Multi-monitor |
| `Settings_DefaultValues_AreCorrect` | Settings defaults |

### FlowAI.AI.Tests (3 tests)

| Test | What it covers |
|---|---|
| `CredentialStore_EncryptsAndDecrypts_WithDPAPI` | DPAPI round-trip |
| `AIProviderFactory_SwitchesBetweenProviders_Correctly` | Provider switching |
| `AIProviderFactory_RecommendsLightweightVisionModel_For16GB` | Model recommendation |

### FlowAI.Vision.Tests (3 tests)

| Test | What it covers |
|---|---|
| `ScreenCaptureService_EnumeratesDisplays` | Display enumeration |
| `ScreenCaptureService_CapturesPrimaryScreen` | Capture with GDI fallback |
| `UIAutomationDiscoveryService_RevalidatesTarget` | Target revalidation |

### FlowAI.Cursor.Tests (3 tests)

| Test | What it covers |
|---|---|
| `SpringDampedMotion_ConvergesToTarget` | Spring physics convergence |
| `BezierFlightPath_ReachesTarget_AtT1` | Bézier accuracy |
| `EmeraldCursorGeometry_ProducesValidGeometry` | Cursor path geometry |

### FlowAI.Integration.Tests (6 tests)

| Test | What it covers |
|---|---|
| `SqliteMemoryStore_SavesAndRetrieves_Preferences` | SQLite persistence |
| `LearningEngine_RecordsAndRetrieves_TaughtWorkflow` | Workflow recording+retrieval |
| `LearningEngine_LearnsCorrection_AndReturnsCorrectedTarget` | Correction learning |
| `AutomationEngine_BlocksHighRiskAction_WhenUnconfirmed` | Risk gate |
| `SqliteMemoryStore_DeletesCorrection_AndPreferences` | Deletion of corrections and preferences |
| `SqliteMemoryStore_ClearsAllData` | Complete memory wipe |

## Manual Testing Checklist

### Voice pipeline
- [ ] Press Ctrl+Alt+Space → listening animation appears
- [ ] Release → transcription → thinking animation
- [ ] Ask: "Where is the File menu?" → cursor flies to File menu
- [ ] Press Escape mid-query → cancels immediately

### Screen understanding
- [ ] AI response includes [POINT:...] or [BOX:...] coordinates
- [ ] Overlay bounding box appears at correct screen location
- [ ] Multi-monitor: cursor flies to correct monitor

### Learning
- [ ] Click Teach → record 2 steps → save → workflow appears in memory
- [ ] Ask same query again → FlowAI prefers learned target

### Settings
- [ ] Change Ollama model → takes effect on next query
- [ ] Switch TTS → new voice used on next response
- [ ] Save settings → persisted across restart

## Expected Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Expected Test Output

```
Passed!  - Failed: 0, Passed: 7  - FlowAI.Core.Tests.dll
Passed!  - Failed: 0, Passed: 3  - FlowAI.AI.Tests.dll
Passed!  - Failed: 0, Passed: 3  - FlowAI.Vision.Tests.dll
Passed!  - Failed: 0, Passed: 3  - FlowAI.Cursor.Tests.dll
Passed!  - Failed: 0, Passed: 4  - FlowAI.Integration.Tests.dll
```
