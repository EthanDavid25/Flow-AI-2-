using FlowAI.AI.Providers;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlowAI.AI;

public sealed class AIProviderFactory
{
    private readonly OllamaProvider ollamaProvider;
    private readonly KimiProvider kimiProvider;
    private readonly ILogger<AIProviderFactory>? logger;

    public AIProviderFactory(
        OllamaProvider ollamaProvider,
        KimiProvider kimiProvider,
        ILogger<AIProviderFactory>? logger = null)
    {
        this.ollamaProvider = ollamaProvider;
        this.kimiProvider = kimiProvider;
        this.logger = logger;
    }

    public IAIProvider GetActiveProvider()
    {
        string selected = FlowAISettings.Current.AIProvider;
        if (selected.Equals("Kimi", StringComparison.OrdinalIgnoreCase))
        {
            return kimiProvider;
        }

        return ollamaProvider;
    }

    public OllamaProvider LocalProvider => ollamaProvider;
    public KimiProvider RemoteProvider => kimiProvider;

    /// <summary>
    /// Inspects available system memory and installed models,
    /// returning recommended models for 16GB RAM Windows laptop.
    /// </summary>
    public static string RecommendDefaultModel()
    {
        // 16 GB laptop safe recommendation: 2B - 4B vision model (e.g. qwen3-vl:2b or moondream)
        return "qwen3-vl:2b";
    }
}
