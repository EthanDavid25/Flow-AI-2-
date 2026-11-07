using FlowAI.Core.Enums;

namespace FlowAI.Core.Events;

public sealed class StateChangedEventArgs : EventArgs
{
    public FlowAIState PreviousState { get; }
    public FlowAIState NewState { get; }
    public string? Reason { get; }
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public object? Metadata { get; }

    public StateChangedEventArgs(FlowAIState previousState, FlowAIState newState, string? reason = null, object? metadata = null)
    {
        PreviousState = previousState;
        NewState = newState;
        Reason = reason;
        Metadata = metadata;
    }
}
