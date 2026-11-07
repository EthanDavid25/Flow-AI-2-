using System.Speech.Synthesis;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlowAI.Audio.Tts;

public sealed class WindowsSpeechTtsService : ITextToSpeechService
{
    private readonly ILogger<WindowsSpeechTtsService>? logger;
    private SpeechSynthesizer? synthesizer;
    private readonly object lockObj = new();

    public string ProviderName => "Windows Speech TTS (Offline Fallback)";
    public bool IsPlaying
    {
        get
        {
            lock (lockObj)
            {
                return synthesizer?.State == SynthesizerState.Speaking;
            }
        }
    }

    public WindowsSpeechTtsService(ILogger<WindowsSpeechTtsService>? logger = null)
    {
        this.logger = logger;
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Stop();

            lock (lockObj)
            {
                try
                {
                    synthesizer = new SpeechSynthesizer();
                    synthesizer.SetOutputToDefaultAudioDevice();
                    synthesizer.SpeakAsync(text);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("Windows Speech TTS failed: {Message}", ex.Message);
                }
            }
        }, cancellationToken);
    }

    public void Stop()
    {
        lock (lockObj)
        {
            try
            {
                synthesizer?.SpeakAsyncCancelAll();
                synthesizer?.Dispose();
            }
            catch { }
            synthesizer = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
