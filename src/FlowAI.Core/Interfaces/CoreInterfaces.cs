using FlowAI.Core.Models;

namespace FlowAI.Core.Interfaces;

public interface IAIProvider
{
    string ProviderName { get; }
    string CurrentModel { get; }
    bool IsConfigured { get; }

    Task<AIResponse> SendChatAsync(AIRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamChatAsync(AIRequest request, CancellationToken cancellationToken = default);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
}

public interface IScreenCaptureService
{
    IReadOnlyList<ScreenInfo> GetDisplays();
    Task<CapturedScreen> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CapturedScreen>> CaptureAllScreensAsync(CancellationToken cancellationToken = default);
    Task<CapturedScreen?> CaptureForegroundWindowAsync(CancellationToken cancellationToken = default);
}

public interface IElementDiscoveryService
{
    Task<IReadOnlyList<DetectedTarget>> DiscoverElementsInForegroundAsync(string query, CancellationToken cancellationToken = default);
    Task<DetectedTarget?> FindElementByQueryAsync(string query, CancellationToken cancellationToken = default);
    bool RevalidateTarget(DetectedTarget target);
}

public interface IAudioRecordingService : IDisposable
{
    event Action<float>? AudioLevelChanged; // RMS level (0.0 to 1.0)
    bool IsRecording { get; }
    void StartRecording();
    byte[] StopRecordingAndGetPcm16();
    void CancelRecording();
}

public interface ITranscriptionService
{
    string ProviderName { get; }
    bool IsAvailable { get; }
    event Action<string>? StatusChanged;
    Task EnsureModelReadyAsync(CancellationToken cancellationToken = default);
    Task<string> TranscribeAudioAsync(byte[] pcm16Audio, CancellationToken cancellationToken = default);
}

public interface ITextToSpeechService : IDisposable
{
    string ProviderName { get; }
    bool IsPlaying { get; }
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
    void Stop();
}

public interface IMemoryStore : IDisposable
{
    Task InitializeAsync();
    Task SavePreferenceAsync(UserPreference preference);
    Task<string?> GetPreferenceAsync(string key);
    Task<IReadOnlyList<UserPreference>> GetAllPreferencesAsync();
    Task DeletePreferenceAsync(string key);
    Task SaveFactAsync(LearnedFact fact);
    Task<IReadOnlyList<LearnedFact>> SearchFactsAsync(string query);
    Task DeleteFactAsync(long id);
    Task SaveCorrectionAsync(CorrectionRecord correction);
    Task<IReadOnlyList<CorrectionRecord>> GetCorrectionsForAppAsync(string appName);
    Task<IReadOnlyList<CorrectionRecord>> GetAllCorrectionsAsync();
    Task DeleteCorrectionAsync(long id);
    Task<long> SaveWorkflowAsync(LearnedWorkflow workflow);
    Task<IReadOnlyList<LearnedWorkflow>> GetWorkflowsAsync();
    Task<LearnedWorkflow?> GetWorkflowByNameAsync(string name);
    Task DeleteWorkflowAsync(long id);
    Task RecordTaskHistoryAsync(TaskHistoryRecord record);
    Task<IReadOnlyList<TaskHistoryRecord>> GetRecentTaskHistoryAsync(int limit = 50);
    Task ClearAllDataAsync();
}

public interface ILearningEngine
{
    bool IsTeachingSessionActive { get; }
    LearnedWorkflow? CurrentTeachingWorkflow { get; }

    void StartTeachingSession(string workflowName, string description, string targetApp);
    void RecordTeachingStep(WorkflowStep step);
    Task<LearnedWorkflow> FinishTeachingSessionAsync();
    void CancelTeachingSession();

    Task RecordCorrectionAsync(string appName, string windowTitle, string userQuery, string wrongTarget, DetectedTarget correctTarget);
    Task<DetectedTarget?> CheckForLearnedCorrectionAsync(string appName, string userQuery);
    Task<LearnedWorkflow?> MatchWorkflowAsync(string userIntent, string activeApp);
}

public interface IComputerAutomationService
{
    Task<AutomationResult> ExecuteActionAsync(AutomationAction action, CancellationToken cancellationToken = default);
    Task<bool> VerifyResultAsync(AutomationAction action, CancellationToken cancellationToken = default);
}
