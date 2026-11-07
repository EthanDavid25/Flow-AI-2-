namespace FlowAI.Core.Models;

public sealed class ChatMessage
{
    public string Role { get; set; } = "user"; // "system", "user", "assistant"
    public string Content { get; set; } = string.Empty;
    public List<byte[]> Images { get; set; } = new();

    public static ChatMessage User(string text, IEnumerable<byte[]>? images = null) => new()
    {
        Role = "user",
        Content = text,
        Images = images != null ? new List<byte[]>(images) : new List<byte[]>()
    };

    public static ChatMessage System(string text) => new()
    {
        Role = "system",
        Content = text
    };

    public static ChatMessage Assistant(string text) => new()
    {
        Role = "assistant",
        Content = text
    };
}

public sealed class AIRequest
{
    public string Prompt { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public List<CapturedScreen> ScreenCaptures { get; set; } = new();
    public string? SystemPrompt { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
}

public sealed class AIResponse
{
    public string RawText { get; set; } = string.Empty;
    public string CleanSpokenText { get; set; } = string.Empty;
    public List<DetectedTarget> PointTargets { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
