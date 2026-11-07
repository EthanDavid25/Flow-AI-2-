using FlowAI.AI.Providers;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlowAI.AI;

/// <summary>
/// Hybrid AI provider factory.
/// Enforces operating modes (Auto, Online, Offline) and routes intelligence requests
/// between primary cloud AI (Kimi) and local offline AI (Ollama).
/// Prepared with clean integrations for Gemini, OpenAI, and Claude.
/// </summary>
public sealed class AIProviderFactory
{
    private readonly OllamaProvider ollamaProvider;
    private readonly KimiProvider kimiProvider;
    private readonly GeminiProvider? geminiProvider;
    private readonly OpenAIProvider? openAIProvider;
    private readonly ClaudeProvider? claudeProvider;
    private readonly ILogger<AIProviderFactory>? logger;

    public OllamaProvider LocalProvider => ollamaProvider;
    public KimiProvider RemoteProvider => kimiProvider;

    public AIProviderFactory(
        OllamaProvider ollamaProvider,
        KimiProvider kimiProvider,
        GeminiProvider? geminiProvider = null,
        OpenAIProvider? openAIProvider = null,
        ClaudeProvider? claudeProvider = null,
        ILogger<AIProviderFactory>? logger = null)
    {
        this.ollamaProvider = ollamaProvider;
        this.kimiProvider = kimiProvider;
        this.geminiProvider = geminiProvider;
        this.openAIProvider = openAIProvider;
        this.claudeProvider = claudeProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Returns the active provider according to OperatingMode (Offline, Online, Auto) and user configuration.
    /// </summary>
    public IAIProvider GetActiveProvider()
    {
        string mode = FlowAISettings.Current.OperatingMode ?? "Auto";

        // 1. Strict OFFLINE Mode — Never allow remote calls, always route to local Ollama
        if (mode.Equals("Offline", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogDebug("OperatingMode is Offline — strictly using local provider ({Provider})", ollamaProvider.ProviderName);
            return ollamaProvider;
        }

        // 2. Explicit provider selection
        string selected = FlowAISettings.Current.AIProvider ?? "Ollama";
        if (selected.Equals("Kimi", StringComparison.OrdinalIgnoreCase))
            return kimiProvider;
        if (selected.Equals("Gemini", StringComparison.OrdinalIgnoreCase) && geminiProvider != null)
            return geminiProvider;
        if (selected.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) && openAIProvider != null)
            return openAIProvider;
        if (selected.Equals("Claude", StringComparison.OrdinalIgnoreCase) && claudeProvider != null)
            return claudeProvider;

        // 3. Explicit ONLINE Mode — Route to configured cloud provider
        if (mode.Equals("Online", StringComparison.OrdinalIgnoreCase))
        {
            string cloudName = FlowAISettings.Current.ActiveCloudProvider ?? "Kimi";
            var cloudProvider = GetCloudProviderByName(cloudName);
            logger?.LogDebug("OperatingMode is Online — selected cloud provider: {Provider}", cloudProvider.ProviderName);
            return cloudProvider;
        }

        // 4. AUTO Mode (Hybrid Intelligence) —
        // Prefer Kimi (or active cloud provider) when configured with API key.
        // If not configured, seamlessly route to local Ollama.
        string preferredCloud = FlowAISettings.Current.ActiveCloudProvider ?? "Kimi";
        var preferredProvider = GetCloudProviderByName(preferredCloud);

        if (preferredProvider.IsConfigured)
        {
            logger?.LogDebug("AUTO mode: Cloud provider '{Provider}' is configured — selecting as primary", preferredProvider.ProviderName);
            return preferredProvider;
        }

        logger?.LogDebug("AUTO mode: Cloud provider not configured — using local {Provider}", ollamaProvider.ProviderName);
        return ollamaProvider;
    }

    /// <summary>
    /// Gets a specific cloud provider by name.
    /// </summary>
    public IAIProvider GetCloudProviderByName(string name)
    {
        if (name.Contains("Gemini", StringComparison.OrdinalIgnoreCase) && geminiProvider != null)
            return geminiProvider;

        if (name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) && openAIProvider != null)
            return openAIProvider;

        if (name.Contains("Claude", StringComparison.OrdinalIgnoreCase) && claudeProvider != null)
            return claudeProvider;

        // Default primary cloud provider is Kimi
        return kimiProvider;
    }

    /// <summary>
    /// Returns all registered providers for inspection and settings.
    /// </summary>
    public IReadOnlyList<IAIProvider> GetAllProviders()
    {
        var list = new List<IAIProvider> { ollamaProvider, kimiProvider };
        if (geminiProvider != null) list.Add(geminiProvider);
        if (openAIProvider != null) list.Add(openAIProvider);
        if (claudeProvider != null) list.Add(claudeProvider);
        return list;
    }

    /// <summary>
    /// Recommends a quantized vision model for 8 GB RAM laptop systems.
    /// </summary>
    public static string RecommendDefaultModel()
    {
        // 8 GB RAM laptop safe recommendation: 2B quantized vision model (qwen3-vl:2b)
        return "qwen3-vl:2b";
    }
}
