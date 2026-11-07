namespace FlowAI.Core.Enums;

/// <summary>
/// Explicit application state machine states for FlowAI.
/// Every state has a dedicated visual representation in the overlay and companion panel.
/// </summary>
public enum FlowAIState
{
    /// <summary>
    /// Calm, minimal cursor-follow behavior.
    /// </summary>
    Idle,

    /// <summary>
    /// Audio-reactive waveform responding to microphone input levels.
    /// </summary>
    Listening,

    /// <summary>
    /// Animated transcription indicator while converting speech to text.
    /// </summary>
    Transcribing,

    /// <summary>
    /// General processing indicator.
    /// </summary>
    Processing,

    /// <summary>
    /// Rotating visual or pulsing emerald thinking indicator.
    /// </summary>
    Thinking,

    /// <summary>
    /// Animated screen scan sweep visualization while capturing and parsing display.
    /// </summary>
    AnalysingScreen,

    /// <summary>
    /// Search-like scanning radar animation while locating target UI element.
    /// </summary>
    FindingElement,

    /// <summary>
    /// Green companion flies toward target element along Bézier curve and renders green bounding box.
    /// </summary>
    Guiding,

    /// <summary>
    /// Animated voice visualizer reacting to spoken audio output.
    /// </summary>
    Speaking,

    /// <summary>
    /// Subtle green learning beacon with active workflow/correction recording status.
    /// </summary>
    Learning,

    /// <summary>
    /// Performing a confirmed desktop computer automation action.
    /// </summary>
    Executing,

    /// <summary>
    /// Graceful error notification state.
    /// </summary>
    Error
}
