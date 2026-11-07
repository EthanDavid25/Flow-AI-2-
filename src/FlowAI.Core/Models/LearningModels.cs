namespace FlowAI.Core.Models;

public sealed class UserPreference
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LearnedFact
{
    public long Id { get; set; }
    public string Fact { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CorrectionRecord
{
    public long Id { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string UserQuery { get; set; } = string.Empty;
    public string WrongTargetLabel { get; set; } = string.Empty;
    public string CorrectedTargetLabel { get; set; } = string.Empty;
    public string ElementSelectorJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class WorkflowStep
{
    public int Sequence { get; set; }
    public string ActionType { get; set; } = "Click"; // Click, Type, Shortcut, Wait, Verify
    public string TargetDescription { get; set; } = string.Empty;
    public string TargetSelectorJson { get; set; } = string.Empty;
    public string InputValue { get; set; } = string.Empty;
    public string VerificationRule { get; set; } = string.Empty;
    public bool RequiresConfirmation { get; set; } = false;
}

public sealed class LearnedWorkflow
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetApplication { get; set; } = string.Empty;
    public string Preconditions { get; set; } = string.Empty;
    public List<WorkflowStep> Steps { get; set; } = new();
    public string ExpectedResult { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool IsApproved { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TaskHistoryRecord
{
    public long Id { get; set; }
    public string TaskQuery { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public double DurationMilliseconds { get; set; }
    public string? Feedback { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
