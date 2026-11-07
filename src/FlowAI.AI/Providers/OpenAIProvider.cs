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
/// OpenAI API provider adapter.
/// Prepared for hybrid multi-provider architecture using official OpenAI chat completions REST endpoints.
/// </summary>
public sealed class OpenAIProvider : IAIProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<OpenAIProvider>? logger;

    public string ProviderName => "OpenAI";
    public string CurrentModel => FlowAISettings.Current.OpenAIModel;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());
    public bool IsLocal => false;
    public AICapabilities Capabilities => AICapabilities.TextGeneration | AICapabilities.Vision | AICapabilities.Streaming | AICapabilities.StructuredOutput | AICapabilities.ToolCalling;

    public OpenAIProvider(HttpClient? httpClient = null, ILogger<OpenAIProvider>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        this.logger = logger;
    }

    public string? GetApiKey() => CredentialStore.LoadSecret("OpenAI");
    public void SetApiKey(string key) => CredentialStore.SaveSecret("OpenAI", key);

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var res = await httpClient.SendAsync(req, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogDebug("OpenAI health check failed: {Message}", ex.Message);
            return false;
        }
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> models = new List<string> { "gpt-4o-mini", "gpt-4o", "o1-mini" };
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
                ErrorMessage = "OpenAI API key is not configured. Please enter your OpenAI API key in Settings.",
                ModelUsed = CurrentModel
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = BuildPayload(request);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            string rawText = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;

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
            logger?.LogError(ex, "OpenAI chat inference failed");
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
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt ?? "You are FlowAI, a desktop assistant."
            }
        };

        var userParts = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = request.Prompt }
        };

        foreach (var capture in request.ScreenCaptures)
        {
            if (capture.ImageBytes != null && capture.ImageBytes.Length > 0)
            {
                string b64 = Convert.ToBase64String(capture.ImageBytes);
                userParts.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:image/jpeg;base64,{b64}" }
                });
            }
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = userParts
        });

        return new JsonObject
        {
            ["model"] = CurrentModel,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens
        };
    }
}
