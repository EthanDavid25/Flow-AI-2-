using FlowAI.Core.Enums;
using FlowAI.Core.Events;
using Microsoft.Extensions.Logging;

namespace FlowAI.Core.StateMachine;

/// <summary>
/// Central, explicit, thread-safe application state machine.
/// Handles transitions, maintains history, logs state changes, and triggers reactive visual updates.
/// </summary>
public sealed class FlowAIStateManager
{
    private readonly object syncLock = new();
    private readonly ILogger<FlowAIStateManager>? logger;
    private FlowAIState currentState = FlowAIState.Idle;
    private CancellationTokenSource? currentOperationCts;

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public FlowAIState CurrentState
    {
        get
        {
            lock (syncLock)
            {
                return currentState;
            }
        }
    }

    public FlowAIStateManager(ILogger<FlowAIStateManager>? logger = null)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Attempts to transition to a new state.
    /// Returns true if the transition occurred, false if already in the target state.
    /// </summary>
    public bool TransitionTo(FlowAIState newState, string? reason = null, object? metadata = null)
    {
        FlowAIState previousState;
        lock (syncLock)
        {
            if (currentState == newState)
            {
                return false;
            }

            previousState = currentState;
            currentState = newState;
        }

        logger?.LogInformation("State transition: {PreviousState} -> {NewState} (Reason: {Reason})",
            previousState, newState, reason ?? "None");

        StateChanged?.Invoke(this, new StateChangedEventArgs(previousState, newState, reason, metadata));
        return true;
    }

    /// <summary>
    /// Resets the state to Idle and cancels any ongoing operation.
    /// </summary>
    public void ResetToIdle(string? reason = "Manual or completed reset")
    {
        CancelCurrentOperation();
        TransitionTo(FlowAIState.Idle, reason);
    }

    /// <summary>
    /// Creates and returns a new linked cancellation token for an active state operation.
    /// </summary>
    public CancellationToken BeginOperation(CancellationToken externalToken = default)
    {
        lock (syncLock)
        {
            currentOperationCts?.Cancel();
            currentOperationCts?.Dispose();
            currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            return currentOperationCts.Token;
        }
    }

    /// <summary>
    /// Cancels any active operation (e.g. on user pressing Escape or releasing hotkey).
    /// </summary>
    public void CancelCurrentOperation()
    {
        lock (syncLock)
        {
            if (currentOperationCts != null && !currentOperationCts.IsCancellationRequested)
            {
                logger?.LogDebug("Cancelling active state operation");
                currentOperationCts.Cancel();
            }
        }
    }
}
