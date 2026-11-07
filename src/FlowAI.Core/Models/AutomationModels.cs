namespace FlowAI.Core.Models;

public enum ActionRiskLevel
{
    /// <summary>
    /// Safe, read-only actions like moving cursor, highlighting element, reading UI tree.
    /// Executed automatically without blocking user.
    /// </summary>
    Low,

    /// <summary>
    /// Moderate actions like scrolling, focusing window, non-destructive navigation.
    /// </summary>
    Medium,

    /// <summary>
    /// Consequential actions like clicking buttons, submitting forms, typing text, deleting, or changing settings.
    /// Requires explicit user confirmation.
    /// </summary>
    High
}

public sealed class AutomationAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ActionType { get; set; } = string.Empty; // "Highlight", "MoveCursor", "Click", "TypeText", "Shortcut", "Scroll"
    public string Description { get; set; } = string.Empty;
    public DetectedTarget? Target { get; set; }
    public string? Parameter { get; set; }
    public ActionRiskLevel RiskLevel { get; set; } = ActionRiskLevel.Low;
    public string? ExpectedOutcome { get; set; }
    public bool UserConfirmed { get; set; }
}

public sealed class AutomationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public TimeSpan Duration { get; set; }
}
