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
/// Google Gemini API provider adapter.
/// Prepared for hybrid multi-provider architecture using official Google Gemini REST endpoints.
/// </summary>
public sealed class GeminiProvider : IAIProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<GeminiProvider>? logger;

    public string ProviderName => "Google Gemini";
    public string CurrentModel => FlowAISettings.Current.GeminiModel;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());
    public bool IsLocal => false;
    public AICapabilities Capabilities => AICapabilities.TextGeneration | AICapabilities.Vision | AICapabilities.Streaming | AICapabilities.StructuredOutput | AICapabilities.ToolCalling;

    public GeminiProvider(HttpClient? httpClient = null, ILogger<GeminiProvider>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        this.logger = logger;
    }

    public string? GetApiKey() => CredentialStore.LoadSecret("Gemini");
    public void SetApiKey(string key) => CredentialStore.SaveSecret("Gemini", key);

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        try
        {
            var res = await httpClient.GetAsync($"https://generativelanguage.googleapis.com/v1beta/models/{CurrentModel}?key={apiKey}", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogDebug("Gemini health check failed: {Message}", ex.Message);
            return false;
        }
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> models = new List<string> { "gemini-1.5-flash", "gemini-1.5-pro", "gemini-2.0-flash-exp" };
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
                ErrorMessage = "Gemini API key is not configured. Please enter your Gemini API key in Settings.",
                ModelUsed = CurrentModel
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = BuildPayload(request);
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{CurrentModel}:generateContent?key={apiKey}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            string rawText = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? string.Empty;

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
            logger?.LogError(ex, "Gemini chat inference failed");
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
        var parts = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            parts.Add(new JsonObject { ["text"] = request.SystemPrompt + "\n\n" });
        }

        parts.Add(new JsonObject { ["text"] = request.Prompt });

        foreach (var capture in request.ScreenCaptures)
        {
            if (capture.ImageBytes != null && capture.ImageBytes.Length > 0)
            {
                parts.Add(new JsonObject
                {
                    ["inline_data"] = new JsonObject
                    {
                        ["mime_type"] = "image/jpeg",
                        ["data"] = Convert.ToBase64String(capture.ImageBytes)
                    }
                });
            }
        }

        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = parts
                }
            }
        };
    }
}
