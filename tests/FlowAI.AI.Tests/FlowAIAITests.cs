using FlowAI.AI;
using FlowAI.AI.Providers;
using FlowAI.AI.Security;
using FlowAI.Core.Configuration;
using Xunit;

namespace FlowAI.AI.Tests;

public class FlowAIAITests
{
    [Fact]
    public void CredentialStore_EncryptsAndDecrypts_WithDPAPI()
    {
        string testProvider = "TestProvider_" + Guid.NewGuid().ToString("N");
        string testSecret = "sk-moonshot-test-key-123456789";

        try
        {
            CredentialStore.SaveSecret(testProvider, testSecret);
            string? loaded = CredentialStore.LoadSecret(testProvider);

            Assert.Equal(testSecret, loaded);
        }
        finally
        {
            CredentialStore.DeleteSecret(testProvider);
            Assert.Null(CredentialStore.LoadSecret(testProvider));
        }
    }

    [Fact]
    public void AIProviderFactory_SwitchesBetweenProviders_Correctly()
    {
        var ollama = new OllamaProvider();
        var kimi = new KimiProvider();
        var factory = new AIProviderFactory(ollama, kimi);

        FlowAISettings.Current.AIProvider = "Ollama";
        Assert.Equal("Ollama (Local)", factory.GetActiveProvider().ProviderName);

        FlowAISettings.Current.AIProvider = "Kimi";
        Assert.Equal("Kimi (Moonshot AI)", factory.GetActiveProvider().ProviderName);

        // Reset to default
        FlowAISettings.Current.AIProvider = "Ollama";
    }

    [Fact]
    public void AIProviderFactory_RecommendsLightweightVisionModel_For16GB()
    {
        string recommended = AIProviderFactory.RecommendDefaultModel();
        Assert.Equal("qwen3-vl:2b", recommended);
    }

    [Fact]
    public void AIProviderFactory_OperatingModeOffline_AlwaysForcesLocalProvider()
    {
        var ollama = new OllamaProvider();
        var kimi = new KimiProvider();
        var factory = new AIProviderFactory(ollama, kimi);

        string originalMode = FlowAISettings.Current.OperatingMode;
        string originalProvider = FlowAISettings.Current.AIProvider;

        try
        {
            FlowAISettings.Current.OperatingMode = "Offline";
            FlowAISettings.Current.AIProvider = "Kimi";

            var provider = factory.GetActiveProvider();
            Assert.True(provider.IsLocal);
            Assert.Equal("Ollama (Local)", provider.ProviderName);
        }
        finally
        {
            FlowAISettings.Current.OperatingMode = originalMode;
            FlowAISettings.Current.AIProvider = originalProvider;
        }
    }

    [Fact]
    public void AIProviderFactory_OperatingModeOnline_RoutesToCloudProvider()
    {
        var ollama = new OllamaProvider();
        var kimi = new KimiProvider();
        var gemini = new GeminiProvider();
        var factory = new AIProviderFactory(ollama, kimi, geminiProvider: gemini);

        string originalMode = FlowAISettings.Current.OperatingMode;
        string originalCloud = FlowAISettings.Current.ActiveCloudProvider;
        string originalProvider = FlowAISettings.Current.AIProvider;

        try
        {
            FlowAISettings.Current.OperatingMode = "Online";
            FlowAISettings.Current.AIProvider = ""; // Don't override
            FlowAISettings.Current.ActiveCloudProvider = "Gemini";

            var provider = factory.GetActiveProvider();
            Assert.False(provider.IsLocal);
            Assert.Contains("Gemini", provider.ProviderName);
        }
        finally
        {
            FlowAISettings.Current.OperatingMode = originalMode;
            FlowAISettings.Current.ActiveCloudProvider = originalCloud;
            FlowAISettings.Current.AIProvider = originalProvider;
        }
    }

    [Fact]
    public void AIProviders_DeclareCorrectCapabilitiesAndLocalFlags()
    {
        var ollama = new OllamaProvider();
        var kimi = new KimiProvider();
        var gemini = new GeminiProvider();

        Assert.True(ollama.IsLocal);
        Assert.True(ollama.Capabilities.HasFlag(Core.Interfaces.AICapabilities.LocalInference));

        Assert.False(kimi.IsLocal);
        Assert.False(gemini.IsLocal);
        Assert.True(kimi.Capabilities.HasFlag(Core.Interfaces.AICapabilities.Vision));
        Assert.True(gemini.Capabilities.HasFlag(Core.Interfaces.AICapabilities.Vision));
    }
}
