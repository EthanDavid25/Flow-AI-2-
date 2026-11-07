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
}
