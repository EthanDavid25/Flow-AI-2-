using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.Ggml;

namespace FlowAI.Audio.Transcription;

public sealed class WhisperTranscriptionService : ITranscriptionService
{
    private readonly ILogger<WhisperTranscriptionService>? logger;
    private WhisperFactory? whisperFactory;
    private readonly SemaphoreSlim initLock = new(1, 1);

    public string ProviderName => "Whisper (Local)";
    public bool IsAvailable => true;

    public event Action<string>? StatusChanged;

    public WhisperTranscriptionService(ILogger<WhisperTranscriptionService>? logger = null)
    {
        this.logger = logger;
    }

    public static string ModelsDirectory =>
        Path.Combine(FlowAISettings.AppDataDirectory, "models");

    public static string GetModelFilePath()
    {
        string modelName = FlowAISettings.Current.WhisperModel.ToLowerInvariant();
        if (modelName != "tiny" && modelName != "base" && modelName != "small")
        {
            modelName = "base";
        }
        return Path.Combine(ModelsDirectory, $"ggml-{modelName}.bin");
    }

    public async Task EnsureModelReadyAsync(CancellationToken cancellationToken = default)
    {
        if (whisperFactory != null) return;

        await initLock.WaitAsync(cancellationToken);
        try
        {
            if (whisperFactory != null) return;

            string modelPath = GetModelFilePath();
            if (!File.Exists(modelPath))
            {
                await DownloadModelAsync(modelPath, cancellationToken);
            }

            StatusChanged?.Invoke("Initializing Whisper engine...");
            whisperFactory = WhisperFactory.FromPath(modelPath);
            StatusChanged?.Invoke("Whisper ready");
        }
        finally
        {
            initLock.Release();
        }
    }

    public async Task<string> TranscribeAudioAsync(byte[] pcm16Audio, CancellationToken cancellationToken = default)
    {
        // 16kHz 16-bit mono = 32,000 bytes/sec. Require at least ~250ms of audio (8,000 bytes)
        if (pcm16Audio.Length < 8000)
        {
            logger?.LogInformation("Audio buffer too short ({Bytes} bytes) — ignoring", pcm16Audio.Length);
            return string.Empty;
        }

        // Quick silence check: calculate RMS of the raw audio data (skip 44-byte WAV header if present)
        int offset = (pcm16Audio.Length >= 44 && pcm16Audio[0] == 0x52 && pcm16Audio[1] == 0x49) ? 44 : 0;
        double sumSquares = 0;
        int sampleCount = 0;
        for (int i = offset; i < pcm16Audio.Length - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(pcm16Audio, i);
            double norm = sample / 32768.0;
            sumSquares += norm * norm;
            sampleCount++;
        }
        double rms = sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0;
        if (rms < 0.005)
        {
            logger?.LogInformation("Audio buffer is silent (RMS={Rms:F4}) — ignoring", rms);
            return string.Empty;
        }

        try
        {
            await EnsureModelReadyAsync(cancellationToken);
            if (whisperFactory == null) return string.Empty;

            // Use await using because WhisperProcessor implements IAsyncDisposable;
            // synchronous Dispose() throws "Cannot dispose while processing, please use DisposeAsync instead".
            // Explicitly set language to "en" for instant processing and avoiding language-detection overhead.
            await using var processor = whisperFactory.CreateBuilder()
                .WithLanguage("en")
                .Build();

            using var audioStream = new MemoryStream(pcm16Audio);
            var recognizedSegments = new List<string>();

            await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
            {
                string text = segment.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(text) && !IsNonSpeechMarker(text))
                {
                    recognizedSegments.Add(text);
                }
            }

            string fullTranscript = string.Join(" ", recognizedSegments).Trim();
            logger?.LogInformation("Whisper transcribed: '{Transcript}'", fullTranscript);

            if (string.IsNullOrWhiteSpace(fullTranscript))
            {
                logger?.LogInformation("Whisper returned empty, attempting Windows Speech fallback...");
                var fallback = new WindowsSpeechTranscriptionService();
                fullTranscript = await fallback.TranscribeAudioAsync(pcm16Audio, cancellationToken);
            }

            return fullTranscript;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Whisper transcription failed, attempting Windows Speech fallback...");
            try
            {
                var fallback = new WindowsSpeechTranscriptionService();
                return await fallback.TranscribeAudioAsync(pcm16Audio, cancellationToken);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private async Task DownloadModelAsync(string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string modelSize = FlowAISettings.Current.WhisperModel.ToLowerInvariant();
        string fileName = Path.GetFileName(destinationPath);
        string downloadUrl = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{fileName}";
        string tempPath = destinationPath + ".downloading";

        StatusChanged?.Invoke($"Downloading speech model ({modelSize})...");
        logger?.LogInformation("Downloading Whisper model from {Url}", downloadUrl);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? 140_000_000;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[65536];
        long downloaded = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
            downloaded += read;
            int percentage = (int)Math.Clamp((downloaded * 100.0) / totalBytes, 0, 100);
            StatusChanged?.Invoke($"Downloading speech model... {percentage}%");
        }

        fileStream.Close();
        File.Move(tempPath, destinationPath, overwrite: true);
        StatusChanged?.Invoke("Model download complete");
    }

    private static bool IsNonSpeechMarker(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) return true;
        if (trimmed.StartsWith("(") && trimmed.EndsWith(")")) return true;
        if (trimmed.Equals("*silence*", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
