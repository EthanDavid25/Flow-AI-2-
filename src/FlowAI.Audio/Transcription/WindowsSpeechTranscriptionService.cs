using System.Speech.Recognition;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlowAI.Audio.Transcription;

public sealed class WindowsSpeechTranscriptionService : ITranscriptionService
{
    private readonly ILogger<WindowsSpeechTranscriptionService>? logger;

    public string ProviderName => "Windows Speech (Offline Fallback)";
    public bool IsAvailable => SpeechRecognitionEngine.InstalledRecognizers().Count > 0;

    public event Action<string>? StatusChanged;

    public WindowsSpeechTranscriptionService(ILogger<WindowsSpeechTranscriptionService>? logger = null)
    {
        this.logger = logger;
    }

    public Task EnsureModelReadyAsync(CancellationToken cancellationToken = default)
    {
        // Windows Speech uses built-in OS recognizers; no download needed
        StatusChanged?.Invoke("Windows Speech engine ready");
        return Task.CompletedTask;
    }

    public Task<string> TranscribeAudioAsync(byte[] pcm16Audio, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (pcm16Audio.Length == 0 || !IsAvailable) return string.Empty;

            try
            {
                using var engine = new SpeechRecognitionEngine();
                engine.LoadGrammar(new DictationGrammar());

                int offset = 0;
                if (pcm16Audio.Length >= 44 &&
                    pcm16Audio[0] == 0x52 && pcm16Audio[1] == 0x49 && pcm16Audio[2] == 0x46 && pcm16Audio[3] == 0x46)
                {
                    offset = 44; // Skip RIFF/WAVE header for SAPI raw audio stream
                }

                using var stream = new MemoryStream(pcm16Audio, offset, pcm16Audio.Length - offset);
                engine.SetInputToAudioStream(
                    stream,
                    new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono));

                var result = engine.Recognize();
                string transcript = result?.Text?.Trim() ?? string.Empty;
                logger?.LogInformation("Windows Speech recognized: '{Transcript}'", transcript);
                return transcript;
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Windows Speech recognition failed: {Message}", ex.Message);
                return string.Empty;
            }
        }, cancellationToken);
    }
}
