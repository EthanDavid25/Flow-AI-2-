using System.Text.Json;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlowAI.Learning;

public sealed class LearningEngine : ILearningEngine
{
    private readonly IMemoryStore memoryStore;
    private readonly ILogger<LearningEngine>? logger;
    private LearnedWorkflow? activeTeachingWorkflow;
    private readonly object sessionLock = new();

    public bool IsTeachingSessionActive
    {
        get
        {
            lock (sessionLock) return activeTeachingWorkflow != null;
        }
    }

    public LearnedWorkflow? CurrentTeachingWorkflow
    {
        get
        {
            lock (sessionLock) return activeTeachingWorkflow;
        }
    }

    public LearningEngine(IMemoryStore memoryStore, ILogger<LearningEngine>? logger = null)
    {
        this.memoryStore = memoryStore;
        this.logger = logger;
    }

    public void StartTeachingSession(string workflowName, string description, string targetApp)
    {
        lock (sessionLock)
        {
            activeTeachingWorkflow = new LearnedWorkflow
            {
                Name = workflowName,
                Description = description,
                TargetApplication = targetApp,
                Preconditions = $"Active application is {targetApp}",
                ExpectedResult = $"Completed workflow '{workflowName}'",
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };
            logger?.LogInformation("Started 'Teach FlowAI' session for workflow: {Name}", workflowName);
        }
    }

    public void RecordTeachingStep(WorkflowStep step)
    {
        lock (sessionLock)
        {
            if (activeTeachingWorkflow == null) return;

            // Sanitize step: Mask sensitive values
            if (IsSensitiveInput(step.InputValue) || IsSensitiveInput(step.TargetDescription))
            {
                step.InputValue = "[MASKED_SENSITIVE]";
            }

            step.Sequence = activeTeachingWorkflow.Steps.Count + 1;
            activeTeachingWorkflow.Steps.Add(step);
            logger?.LogInformation("Recorded step #{Sequence}: {Action} on '{Target}'",
                step.Sequence, step.ActionType, step.TargetDescription);
        }
    }

    public async Task<LearnedWorkflow> FinishTeachingSessionAsync()
    {
        LearnedWorkflow workflowToSave;
        lock (sessionLock)
        {
            if (activeTeachingWorkflow == null)
            {
                throw new InvalidOperationException("No active teaching session to finish.");
            }
            workflowToSave = activeTeachingWorkflow;
            activeTeachingWorkflow = null;
        }

        long id = await memoryStore.SaveWorkflowAsync(workflowToSave);
        workflowToSave.Id = id;
        logger?.LogInformation("Saved taught workflow '{Name}' (ID: {Id}) with {Count} steps",
            workflowToSave.Name, id, workflowToSave.Steps.Count);
        return workflowToSave;
    }

    public void CancelTeachingSession()
    {
        lock (sessionLock)
        {
            if (activeTeachingWorkflow != null)
            {
                logger?.LogInformation("Cancelled teaching session for '{Name}'", activeTeachingWorkflow.Name);
                activeTeachingWorkflow = null;
            }
        }
    }

    public async Task RecordCorrectionAsync(
        string appName,
        string windowTitle,
        string userQuery,
        string wrongTarget,
        DetectedTarget correctTarget)
    {
        var selectorObj = new
        {
            correctTarget.AutomationId,
            correctTarget.ControlType,
            correctTarget.Label,
            correctTarget.TargetPoint.X,
            correctTarget.TargetPoint.Y
        };

        var correction = new CorrectionRecord
        {
            ApplicationName = appName,
            WindowTitle = windowTitle,
            UserQuery = userQuery,
            WrongTargetLabel = wrongTarget,
            CorrectedTargetLabel = correctTarget.Label,
            ElementSelectorJson = JsonSerializer.Serialize(selectorObj),
            CreatedAt = DateTime.UtcNow
        };

        await memoryStore.SaveCorrectionAsync(correction);
        logger?.LogInformation("Recorded user correction in {App}: '{Query}' -> '{Corrected}' (was '{Wrong}')",
            appName, userQuery, correctTarget.Label, wrongTarget);
    }

    public async Task<DetectedTarget?> CheckForLearnedCorrectionAsync(string appName, string userQuery)
    {
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(userQuery)) return null;

        var corrections = await memoryStore.GetCorrectionsForAppAsync(appName);
        string cleanQuery = userQuery.Trim().ToLowerInvariant();

        foreach (var c in corrections)
        {
            if (c.UserQuery.Trim().ToLowerInvariant() == cleanQuery ||
                cleanQuery.Contains(c.UserQuery.Trim().ToLowerInvariant()))
            {
                try
                {
                    var doc = JsonDocument.Parse(c.ElementSelectorJson);
                    double x = doc.RootElement.GetProperty("X").GetDouble();
                    double y = doc.RootElement.GetProperty("Y").GetDouble();
                    string label = doc.RootElement.GetProperty("Label").GetString() ?? c.CorrectedTargetLabel;

                    logger?.LogInformation("Found learned correction for '{Query}' in {App}: target '{Label}'",
                        userQuery, appName, label);

                    return new DetectedTarget(x, y, label)
                    {
                        Source = DetectionSource.LearnedWorkflow,
                        Confidence = 0.95
                    };
                }
                catch { }
            }
        }

        return null;
    }

    public async Task<LearnedWorkflow?> MatchWorkflowAsync(string userIntent, string activeApp)
    {
        if (string.IsNullOrWhiteSpace(userIntent)) return null;

        var workflows = await memoryStore.GetWorkflowsAsync();
        string cleanIntent = userIntent.Trim().ToLowerInvariant();

        foreach (var wf in workflows)
        {
            string wfName = wf.Name.ToLowerInvariant();
            if (cleanIntent.Contains(wfName) || wfName.Contains(cleanIntent))
            {
                return wf;
            }
        }

        return null;
    }

    private static bool IsSensitiveInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string lower = text.ToLowerInvariant();
        return lower.Contains("password") || lower.Contains("token") || lower.Contains("api_key") ||
               lower.Contains("secret") || lower.Contains("creditcard") || lower.Contains("cvv");
    }
}
