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
/// Moonshot AI Kimi API provider adapter.
/// Uses official Moonshot chat completions format with secure DPAPI credential storage.
/// </summary>
public sealed class KimiProvider : IAIProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<KimiProvider>? logger;

    public string ProviderName => "Kimi (Moonshot AI)";
    public string CurrentModel => FlowAISettings.Current.KimiModel;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());

    public const string CostDisclosure = "Note: Kimi uses remote Moonshot AI APIs which may incur usage costs according to your API key plan.";

    public KimiProvider(HttpClient? httpClient = null, ILogger<KimiProvider>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        this.logger = logger;
    }

    public string? GetApiKey() => CredentialStore.LoadSecret("Kimi");

    public void SetApiKey(string apiKey) => CredentialStore.SaveSecret("Kimi", apiKey);

    private string Endpoint => FlowAISettings.Current.KimiEndpoint.TrimEnd('/');

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/models");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var res = await httpClient.SendAsync(req, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogDebug("Kimi health check failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = new List<string> { "moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k", "moonshot-v1-auto" };
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) return models;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/models");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var res = await httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
                if (json?["data"] is JsonArray dataArr)
                {
                    var fetched = new List<string>();
                    foreach (var item in dataArr)
                    {
                        if (item?["id"]?.GetValue<string>() is { } id) fetched.Add(id);
                    }
                    if (fetched.Count > 0) return fetched;
                }
            }
        }
        catch { }

        return models;
    }

    public async Task<AIResponse> SendChatAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AIResponse
            {
                IsSuccess = false,
                ErrorMessage = "Kimi API key is not configured. Please enter your Moonshot API key in Settings.",
                ModelUsed = CurrentModel
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = BuildPayload(request, stream: false);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/chat/completions")
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
            logger?.LogError(ex, "Kimi chat inference failed");
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
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) yield break;

        var payload = BuildPayload(request, stream: true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("data: "))
            {
                string jsonPart = line.Substring(6).Trim();
                if (jsonPart == "[DONE]") break;

                string? contentToken = null;
                try
                {
                    var node = JsonNode.Parse(jsonPart);
                    contentToken = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
                }
                catch { }

                if (!string.IsNullOrEmpty(contentToken))
                {
                    yield return contentToken;
                }
            }
        }
    }

    private JsonObject BuildPayload(AIRequest request, bool stream)
    {
        string systemMsg = request.SystemPrompt ??
            "You are FlowAI, a friendly Windows desktop companion.\n" +
            "When helping the user find or click elements, embed point tags: [POINT:x,y:label:screen0] or [BOX:x,y,w,h:label:screen0].";

        var messagesArray = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemMsg
            }
        };

        foreach (var msg in request.Messages)
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Prompt) && request.Messages.Count == 0)
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = request.Prompt
            });
        }

        return new JsonObject
        {
            ["model"] = CurrentModel,
            ["messages"] = messagesArray,
            ["stream"] = stream,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens
        };
    }
}
