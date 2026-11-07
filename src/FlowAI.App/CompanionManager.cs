using FlowAI.AI.Providers;
using FlowAI.Audio.Capture;
using FlowAI.Audio.Transcription;
using FlowAI.Audio.Tts;
using FlowAI.Automation.Engine;
using FlowAI.Core.Configuration;
using FlowAI.Core.Enums;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using FlowAI.Core.Parsing;
using FlowAI.Core.StateMachine;
using FlowAI.Learning;
using FlowAI.Memory;
using FlowAI.Overlay;
using FlowAI.Vision.Capture;
using FlowAI.Vision.Discovery;
using FlowAI.App.Hotkey;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FlowAI.App;

/// <summary>
/// Central orchestrator that owns all FlowAI services and coordinates the full
/// voice → capture → AI → overlay pipeline.
///
/// Lifetime: created on the WPF UI thread, but hotkey callbacks arrive on
/// the hook thread or thread-pool. Every UI update must be dispatched through
/// System.Windows.Application.Current.Dispatcher.
/// </summary>
public sealed class CompanionManager : IDisposable
{
    // ── Services ─────────────────────────────────────────────────────────────
    private readonly ILogger<CompanionManager> _logger;
    private readonly FlowAISettings _settings;
    private readonly FlowAIStateManager _stateManager;
    private readonly GlobalHotkeyMonitor _hotkey;
    private readonly IScreenCaptureService _capture;
    private readonly IAudioRecordingService _recorder;
    private readonly ITranscriptionService _transcription;
    private readonly ITextToSpeechService _tts;
    private readonly IAIProvider _ai;
    private readonly IMemoryStore _memory;
    private readonly ILearningEngine _learning;
    private readonly IComputerAutomationService _automation;
    private readonly OverlayWindowManager _overlay;

    // ── Mutable state ─────────────────────────────────────────────────────────
    private volatile bool _disposed;
    private CancellationTokenSource? _activeCts;

    // ── Public events (UI can subscribe) ──────────────────────────────────────
    public event Action<FlowAIState>? StateChanged;
    public event Action<string>? TranscriptUpdated;   // user utterance
    public event Action<string>? ResponseUpdated;     // AI response text
    public event Action<string>? ResponseStreamChunk; // AI streaming token chunk
    public event Action<string>? StatusUpdated;       // short status line
    public event Action<float>? AudioLevelChanged;    // 0.0 – 1.0

    // ── Readonly accessors ────────────────────────────────────────────────────
    public FlowAIState CurrentState => _stateManager.CurrentState;
    public string CurrentModel => _ai.CurrentModel;
    public string AIProviderName => _ai.ProviderName;
    public bool IsAIConnected { get; private set; }

    public CompanionManager(
        ILogger<CompanionManager> logger,
        FlowAISettings settings,
        FlowAIStateManager stateManager,
        GlobalHotkeyMonitor hotkey,
        IScreenCaptureService capture,
        IAudioRecordingService recorder,
        ITranscriptionService transcription,
        ITextToSpeechService tts,
        IAIProvider ai,
        IMemoryStore memory,
        ILearningEngine learning,
        IComputerAutomationService automation,
        OverlayWindowManager overlay)
    {
        _logger = logger;
        _settings = settings;
        _stateManager = stateManager;
        _hotkey = hotkey;
        _capture = capture;
        _recorder = recorder;
        _transcription = transcription;
        _tts = tts;
        _ai = ai;
        _memory = memory;
        _learning = learning;
        _automation = automation;
        _overlay = overlay;

        _stateManager.StateChanged += OnStateChanged;
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        _hotkey.HotkeyReleased += OnHotkeyReleased;
        _hotkey.EscapePressed += OnEscapePressed;
        _recorder.AudioLevelChanged += OnAudioLevel;
    }

    // ── Initialisation ────────────────────────────────────────────────────────
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("CompanionManager initialising…");

        await _memory.InitializeAsync();
        _logger.LogInformation("SQLite memory store ready");

        IsAIConnected = await _ai.HealthCheckAsync(cancellationToken);
        _logger.LogInformation("AI health {Provider}: {Status}", _ai.ProviderName, IsAIConnected ? "OK" : "OFFLINE");
        StatusUpdated?.Invoke(IsAIConnected
            ? $"Connected — {_ai.ProviderName} ({_ai.CurrentModel})"
            : $"{_ai.ProviderName} offline — start Ollama");

        // Warm-up transcription in background (don't block startup)
        _ = Task.Run(async () =>
        {
            try
            {
                _transcription.StatusChanged += s => StatusUpdated?.Invoke(s);
                await _transcription.EnsureModelReadyAsync(cancellationToken);
                _logger.LogInformation("Transcription ready: {Provider}", _transcription.ProviderName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transcription warm-up failed; will retry on first use");
            }
        }, cancellationToken);

        _hotkey.Start();
        _stateManager.TransitionTo(FlowAIState.Idle, "Initialisation complete");
        _logger.LogInformation("CompanionManager ready");
    }

    // ── Hotkey callbacks (hook thread) ────────────────────────────────────────
    private void OnHotkeyPressed()
    {
        _activeCts?.Cancel();
        _tts.Stop();
        _stateManager.TransitionTo(FlowAIState.Listening, "Hotkey pressed");
        try { _recorder.StartRecording(); }
        catch (Exception ex) { _logger.LogError(ex, "Audio recorder start failed"); }
    }

    private void OnHotkeyReleased()
    {
        if (_stateManager.CurrentState != FlowAIState.Listening) return;

        byte[] pcm;
        try { pcm = _recorder.StopRecordingAndGetPcm16(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recorder stop failed");
            _stateManager.ResetToIdle("Recorder error");
            return;
        }

        _activeCts = new CancellationTokenSource();
        _ = Task.Run(() => RunVoicePipelineAsync(pcm, _activeCts.Token), _activeCts.Token);
    }

    private void OnEscapePressed()
    {
        _activeCts?.Cancel();
        try { _recorder.CancelRecording(); } catch { }
        _tts.Stop();
        _overlay.DismissAll();
        _stateManager.ResetToIdle("Escape pressed");
        StatusUpdated?.Invoke("Cancelled");
    }

    private void OnAudioLevel(float level)
    {
        AudioLevelChanged?.Invoke(level);
        _overlay.UpdateAudioLevel(level);
    }

    private void OnStateChanged(object? sender, Core.Events.StateChangedEventArgs e)
    {
        StateChanged?.Invoke(e.NewState);
        _overlay.UpdateState(e.NewState);
    }

    // ── Voice pipeline (hotkey release → process) ─────────────────────────────
    private async Task RunVoicePipelineAsync(byte[] pcm16Audio, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Transcribe
            _stateManager.TransitionTo(FlowAIState.Transcribing, "Audio captured");
            StatusUpdated?.Invoke("Transcribing…");

            string userText = string.Empty;
            if (pcm16Audio.Length > 0)
                userText = await _transcription.TranscribeAudioAsync(pcm16Audio, cancellationToken);

            userText = userText.Trim();
            if (string.IsNullOrWhiteSpace(userText))
            {
                _stateManager.ResetToIdle("Empty transcription");
                StatusUpdated?.Invoke("Didn't catch that — try again");
                return;
            }

            TranscriptUpdated?.Invoke(userText);
            _logger.LogInformation("User said: {Text}", userText);

            await RunQueryPipelineAsync(userText, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _stateManager.ResetToIdle("Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice pipeline error");
            await HandlePipelineErrorAsync(ex.Message);
        }
    }

    // ── Shared query pipeline (used by voice + text) ──────────────────────────
    private async Task RunQueryPipelineAsync(string userText, CancellationToken cancellationToken)
    {
        string activeApp = GetForegroundAppName();

        // 1. Check learned workflow first!
        if (_settings.LearningEnabled)
        {
            var matchedWorkflow = await _learning.MatchWorkflowAsync(userText, activeApp);
            if (matchedWorkflow != null && matchedWorkflow.Steps.Count > 0)
            {
                await ExecuteWorkflowAsync(matchedWorkflow, cancellationToken);
                return;
            }

            // Check learned correction!
            if (_settings.AutoRetrieveCorrections)
            {
                var correctedTarget = await _learning.CheckForLearnedCorrectionAsync(activeApp, userText);
                if (correctedTarget != null)
                {
                    _logger.LogInformation("Applying learned correction for query '{Query}'", userText);
                    _stateManager.TransitionTo(FlowAIState.Guiding, "Applying learned correction");
                    StatusUpdated?.Invoke($"Found (learned): {correctedTarget.Label}");
                    _overlay.FlyToTarget(correctedTarget);

                    string msg = $"Found {correctedTarget.Label} using your previously learned correction.";
                    ResponseUpdated?.Invoke(msg);
                    _stateManager.TransitionTo(FlowAIState.Speaking, "Speaking correction feedback");
                    await _tts.SpeakAsync(msg, cancellationToken);
                    _stateManager.ResetToIdle();
                    return;
                }
            }
        }

        // 2. Capture screen
        _stateManager.TransitionTo(FlowAIState.AnalysingScreen, "Query ready");
        StatusUpdated?.Invoke("Reading screen…");

        var screens = await _capture.CaptureAllScreensAsync(cancellationToken);

        // 3. Build AI request
        _stateManager.TransitionTo(FlowAIState.Thinking, "Screen captured");
        StatusUpdated?.Invoke("Thinking…");

        string systemPrompt = BuildSystemPrompt(screens);
        var messages = new List<ChatMessage>
        {
            ChatMessage.System(systemPrompt),
            ChatMessage.User(userText, screens.Select(s => s.ImageBytes))
        };

        var request = new AIRequest
        {
            Prompt = userText,
            Messages = messages,
            ScreenCaptures = screens.ToList(),
            SystemPrompt = systemPrompt,
            Temperature = 0.3,
            MaxTokens = 1024
        };

        // 4. Send to AI (with real-time streaming)
        string fullRawResponse = string.Empty;
        var durationWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await foreach (var chunk in _ai.StreamChatAsync(request, cancellationToken))
            {
                fullRawResponse += chunk;
                ResponseStreamChunk?.Invoke(chunk);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "StreamChatAsync fell back to SendChatAsync");
        }

        if (string.IsNullOrWhiteSpace(fullRawResponse))
        {
            var directResponse = await _ai.SendChatAsync(request, cancellationToken);
            if (!directResponse.IsSuccess)
            {
                _logger.LogWarning("AI error: {Error}", directResponse.ErrorMessage);
                _stateManager.TransitionTo(FlowAIState.Error, directResponse.ErrorMessage);
                StatusUpdated?.Invoke($"AI error: {directResponse.ErrorMessage}");
                await Task.Delay(3000, CancellationToken.None);
                _stateManager.ResetToIdle();
                return;
            }
            fullRawResponse = directResponse.RawText;
        }

        durationWatch.Stop();

        // 5. Parse targets
        var targets = PointProtocolParser.ParseTargets(fullRawResponse, out string cleanText);

        ResponseUpdated?.Invoke(cleanText);
        _logger.LogInformation("AI response ({N} targets): {Text}", targets.Count, cleanText);

        // 6. Fly to targets
        if (targets.Count > 0)
        {
            _stateManager.TransitionTo(FlowAIState.Guiding, "Targets found");
            StatusUpdated?.Invoke($"Found: {targets[0].Label}");

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _overlay.FlyToTarget(target);
                await Task.Delay(300, cancellationToken);
            }
        }

        // 7. Speak
        _stateManager.TransitionTo(FlowAIState.Speaking, "Guiding complete");
        StatusUpdated?.Invoke("Speaking…");

        if (!string.IsNullOrWhiteSpace(cleanText))
            await _tts.SpeakAsync(cleanText, cancellationToken);

        // 8. Record history
        if (_settings.LearningEnabled)
        {
            await _memory.RecordTaskHistoryAsync(new TaskHistoryRecord
            {
                TaskQuery = userText,
                ApplicationName = activeApp,
                IsSuccess = true,
                ModelUsed = _ai.CurrentModel,
                DurationMilliseconds = durationWatch.ElapsedMilliseconds
            });
        }

        _stateManager.ResetToIdle("Pipeline complete");
        StatusUpdated?.Invoke("Ready");
    }

    // ── Workflow execution loop ───────────────────────────────────────────────
    private async Task ExecuteWorkflowAsync(LearnedWorkflow workflow, CancellationToken cancellationToken)
    {
        _stateManager.TransitionTo(FlowAIState.Executing, "Executing workflow");
        string startMsg = $"Executing workflow '{workflow.Name}' ({workflow.Steps.Count} steps)…";
        StatusUpdated?.Invoke(startMsg);
        ResponseUpdated?.Invoke(startMsg);

        for (int i = 0; i < workflow.Steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = workflow.Steps[i];

            StatusUpdated?.Invoke($"Step {step.Sequence}/{workflow.Steps.Count}: {step.ActionType} {step.TargetDescription}");

            DetectedTarget? stepTarget = null;
            // If selector or coordinates exist, guide the cursor to the target
            if (!string.IsNullOrWhiteSpace(step.TargetSelectorJson))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(step.TargetSelectorJson);
                    if (doc.RootElement.TryGetProperty("X", out var xProp) &&
                        doc.RootElement.TryGetProperty("Y", out var yProp))
                    {
                        stepTarget = new DetectedTarget(xProp.GetDouble(), yProp.GetDouble(), step.TargetDescription);
                        _overlay.FlyToTarget(stepTarget);
                        await Task.Delay(300, cancellationToken);
                    }
                }
                catch { }
            }

            var action = new AutomationAction
            {
                ActionType = step.ActionType,
                Description = $"{step.ActionType} {step.TargetDescription}",
                Target = stepTarget,
                Parameter = step.InputValue,
                RiskLevel = step.RequiresConfirmation ? ActionRiskLevel.High : ActionRiskLevel.Low,
                UserConfirmed = !step.RequiresConfirmation
            };

            var result = await _automation.ExecuteActionAsync(action, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("Step {Seq} paused: {Msg}", step.Sequence, result.Message);
                StatusUpdated?.Invoke($"Step {step.Sequence} paused: {result.Message}");
                ResponseUpdated?.Invoke($"Workflow paused at step {step.Sequence}: {result.Message}");
                _stateManager.ResetToIdle("Step paused");
                return;
            }

            await Task.Delay(350, cancellationToken);
        }

        string finishMsg = $"Completed workflow '{workflow.Name}'.";
        StatusUpdated?.Invoke("Ready");
        ResponseUpdated?.Invoke(finishMsg);
        _stateManager.TransitionTo(FlowAIState.Speaking, "Workflow finished");
        await _tts.SpeakAsync(finishMsg, cancellationToken);
        _stateManager.ResetToIdle("Workflow complete");
    }

    // ── Public text query API (from CompanionPanel text box) ─────────────────
    public async Task AskTextAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        _activeCts?.Cancel();
        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TranscriptUpdated?.Invoke(query);

        try
        {
            await RunQueryPipelineAsync(query, _activeCts.Token);
        }
        catch (OperationCanceledException)
        {
            _stateManager.ResetToIdle("Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text pipeline error");
            await HandlePipelineErrorAsync(ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task HandlePipelineErrorAsync(string message)
    {
        _stateManager.TransitionTo(FlowAIState.Error, message);
        StatusUpdated?.Invoke($"Error: {message}");
        await Task.Delay(3000, CancellationToken.None);
        _stateManager.ResetToIdle("Error recovery");
    }

    private string BuildSystemPrompt(IReadOnlyList<CapturedScreen> screens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are FlowAI, an intelligent Windows desktop companion.");
        sb.AppendLine("You can see the user's screen and help them find UI elements, explain what's visible, and guide them.");
        sb.AppendLine();
        sb.AppendLine("COORDINATE PROTOCOL:");
        sb.AppendLine("When pointing to a specific UI element, embed coordinates using:");
        sb.AppendLine("  [POINT:x,y:label]      — for a single point");
        sb.AppendLine("  [BOX:x,y,w,h:label]    — for a bounding box (x,y = top-left, w = width, h = height)");
        sb.AppendLine("All coordinates are in absolute screen pixels.");
        sb.AppendLine("For multi-monitor: append :screenN (e.g. :screen1) where N is 0-based.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("- Be concise. Answers under 3 sentences unless detail is explicitly requested.");
        sb.AppendLine("- When guiding: always include at least one [POINT] or [BOX] tag for the target.");
        sb.AppendLine("- Never include coordinates for text responses that don't involve pointing.");
        sb.AppendLine("- Respect user privacy — do not describe sensitive personal data.");

        if (screens.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Screen information ({screens.Count} monitor(s)):");
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                sb.AppendLine($"  Screen {i}: {s.Width}×{s.Height} at origin ({s.ScreenInfo.PhysicalBounds.X},{s.ScreenInfo.PhysicalBounds.Y})");
            }
        }

        return sb.ToString();
    }

    private static string GetForegroundAppName()
    {
        try
        {
            var hwnd = AppNativeMethods.GetForegroundWindow();
            AppNativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch { return "Unknown"; }
    }

    // ── Teaching session helpers ──────────────────────────────────────────────
    public void StartTeachingSession(string workflowName, string description, string targetApp)
    {
        _learning.StartTeachingSession(workflowName, description, targetApp);
        _stateManager.TransitionTo(FlowAIState.Learning, "Teaching session started");
        StatusUpdated?.Invoke($"Recording workflow: {workflowName}");
    }

    public async Task<string> StopTeachingSessionAsync()
    {
        try
        {
            var workflow = await _learning.FinishTeachingSessionAsync();
            _stateManager.ResetToIdle("Teaching complete");
            StatusUpdated?.Invoke($"Workflow saved: {workflow.Name} ({workflow.Steps.Count} steps)");
            return workflow.Name;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finish teaching session");
            _learning.CancelTeachingSession();
            _stateManager.ResetToIdle("Teaching error");
            return string.Empty;
        }
    }

    public void CancelTeachingSession()
    {
        _learning.CancelTeachingSession();
        _stateManager.ResetToIdle("Teaching cancelled");
    }

    // ── Health check ──────────────────────────────────────────────────────────
    public async Task<bool> CheckAIHealthAsync()
    {
        IsAIConnected = await _ai.HealthCheckAsync();
        return IsAIConnected;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _hotkey.Dispose();
        _recorder.Dispose();
        _tts.Dispose();
        _memory.Dispose();
        _overlay.Dispose();
    }
}

// ── Minimal Win32 helpers (private to App assembly) ───────────────────────────
internal static class AppNativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
