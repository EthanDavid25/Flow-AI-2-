using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using FlowAI.AI.Security;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using FlowAI.Core.Parsing;
using Microsoft.Extensions.Logging;

namespace FlowAI.AI.Providers;

/// <summary>
/// Anthropic Claude API provider adapter.
/// Prepared for hybrid multi-provider architecture using official Anthropic Messages REST endpoints.
/// </summary>
public sealed class ClaudeProvider : IAIProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<ClaudeProvider>? logger;

    public string ProviderName => "Anthropic Claude";
    public string CurrentModel => FlowAISettings.Current.ClaudeModel;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());
    public bool IsLocal => false;
    public AICapabilities Capabilities => AICapabilities.TextGeneration | AICapabilities.Vision | AICapabilities.Streaming | AICapabilities.StructuredOutput | AICapabilities.ToolCalling;

    public ClaudeProvider(HttpClient? httpClient = null, ILogger<ClaudeProvider>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        this.logger = logger;
    }

    public string? GetApiKey() => CredentialStore.LoadSecret("Claude");
    public void SetApiKey(string key) => CredentialStore.SaveSecret("Claude", key);

    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        string? apiKey = GetApiKey();
        return Task.FromResult(!string.IsNullOrWhiteSpace(apiKey));
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> models = new List<string> { "claude-3-5-sonnet-20241022", "claude-3-5-haiku-20241022" };
        return Task.FromResult(models);
    }

    public async Task<AIResponse> SendChatAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AIResponse
            {
                IsSuccess = false,
                ErrorMessage = "Claude API key is not configured. Please enter your Anthropic API key in Settings.",
                ModelUsed = CurrentModel
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = BuildPayload(request);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            string rawText = json?["content"]?[0]?["text"]?.GetValue<string>() ?? string.Empty;

            var targets = PointProtocolParser.ParseTargets(rawText, out string cleanText);
            stopwatch.Stop();

            return new AIResponse
            {
                RawText = rawText,
                CleanSpokenText = cleanText,
                PointTargets = targets,
                Duration = stopwatch.Elapsed,
                ModelUsed = CurrentModel,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogError(ex, "Claude chat inference failed");
            return new AIResponse
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                ModelUsed = CurrentModel
            };
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        AIRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var direct = await SendChatAsync(request, cancellationToken);
        if (direct.IsSuccess && !string.IsNullOrWhiteSpace(direct.RawText))
        {
            yield return direct.RawText;
        }
    }

    private JsonObject BuildPayload(AIRequest request)
    {
        var userContent = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = request.Prompt }
        };

        foreach (var capture in request.ScreenCaptures)
        {
            if (capture.ImageBytes != null && capture.ImageBytes.Length > 0)
            {
                string b64 = Convert.ToBase64String(capture.ImageBytes);
                userContent.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = "image/jpeg",
                        ["data"] = b64
                    }
                });
            }
        }

        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = userContent
            }
        };

        return new JsonObject
        {
            ["model"] = CurrentModel,
            ["system"] = request.SystemPrompt ?? "You are FlowAI, a desktop assistant.",
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens > 0 ? request.MaxTokens : 1024
        };
    }
}
