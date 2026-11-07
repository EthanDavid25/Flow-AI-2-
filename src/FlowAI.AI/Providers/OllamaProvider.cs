using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using FlowAI.Core.Parsing;
using Microsoft.Extensions.Logging;

namespace FlowAI.AI.Providers;

public sealed class OllamaProvider : IAIProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<OllamaProvider>? logger;

    public string ProviderName => "Ollama (Local)";
    public string CurrentModel => FlowAISettings.Current.OllamaModel;
    public bool IsConfigured => true; // Local default, no API key needed

    public OllamaProvider(HttpClient? httpClient = null, ILogger<OllamaProvider>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        this.logger = logger;
    }

    private string Endpoint => FlowAISettings.Current.OllamaEndpoint.TrimEnd('/');

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var response = await httpClient.GetAsync($"{Endpoint}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogDebug("Ollama health check failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var modelNames = new List<string>();
        try
        {
            var response = await httpClient.GetFromJsonAsync<JsonObject>($"{Endpoint}/api/tags", cancellationToken);
            if (response?["models"] is JsonArray modelsArray)
            {
                foreach (var modelNode in modelsArray)
                {
                    if (modelNode?["name"]?.GetValue<string>() is { } name)
                    {
                        modelNames.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Failed to list Ollama models: {Message}", ex.Message);
        }

        return modelNames;
    }

    public async Task<AIResponse> SendChatAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fullTextBuilder = new StringBuilder();

        try
        {
            var payload = BuildChatPayload(request, stream: false);
            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{Endpoint}/api/chat", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            string rawText = json?["message"]?["content"]?.GetValue<string>() ?? string.Empty;

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
            logger?.LogError(ex, "Ollama chat inference failed");
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
        var payload = BuildChatPayload(request, stream: true);
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/api/chat")
        {
            Content = content
        };

        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? token = null;
            try
            {
                var node = JsonNode.Parse(line);
                token = node?["message"]?["content"]?.GetValue<string>();
            }
            catch { }

            if (!string.IsNullOrEmpty(token))
            {
                yield return token;
            }
        }
    }

    private JsonObject BuildChatPayload(AIRequest request, bool stream)
    {
        string systemInstructions = request.SystemPrompt ??
            "You are FlowAI, a friendly and intelligent Windows desktop companion that guides users.\n" +
            "You can see the user's screen through screenshots.\n" +
            "When the user asks where something is, or asks you to find, show, or click an element:\n" +
            "1. Give a brief, helpful answer (1-2 sentences).\n" +
            "2. Identify the pixel coordinates on screen and embed a point tag: [POINT:x,y:label:screen0] or [BOX:x,y,w,h:label:screen0].\n" +
            "Coordinates must match the pixel positions in the screenshot.";

        var messagesArray = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemInstructions
            }
        };

        foreach (var msg in request.Messages)
        {
            var msgObj = new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            };

            if (msg.Images.Count > 0)
            {
                var imagesArray = new JsonArray();
                foreach (var img in msg.Images)
                {
                    imagesArray.Add(Convert.ToBase64String(img));
                }
                msgObj["images"] = imagesArray;
            }

            messagesArray.Add(msgObj);
        }

        // If there are screen captures attached to request, attach the latest screen image
        if (request.ScreenCaptures.Count > 0)
        {
            var lastScreen = request.ScreenCaptures[^1];
            if (lastScreen.ImageBytes.Length > 0)
            {
                var userPromptMsg = new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = string.IsNullOrWhiteSpace(request.Prompt) ? "Inspect this screen." : request.Prompt,
                    ["images"] = new JsonArray { Convert.ToBase64String(lastScreen.ImageBytes) }
                };
                messagesArray.Add(userPromptMsg);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Prompt) && request.Messages.Count == 0)
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
            ["options"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxTokens
            }
        };
    }
}
