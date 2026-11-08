using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FlowAI.Audio.Tts;

public sealed class EdgeTtsService : ITextToSpeechService
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string EdgeVersion = "1-143.0.3650.75";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";

    private readonly ILogger<EdgeTtsService>? logger;
    private IWavePlayer? audioPlayer;
    private Mp3FileReader? audioReader;
    private MediaFoundationResampler? audioResampler;
    private readonly object playbackLock = new();

    public string ProviderName => "Edge Neural TTS (Free)";
    public bool IsPlaying
    {
        get
        {
            lock (playbackLock)
            {
                return audioPlayer?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public EdgeTtsService(ILogger<EdgeTtsService>? logger = null)
    {
        this.logger = logger;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Stop();

        try
        {
            byte[] mp3Bytes = await SynthesizeToMp3Async(text, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            lock (playbackLock)
            {
                StartPlayback(mp3Bytes);
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("403") || ex.Message.Contains("WebSocketException"))
            {
                logger?.LogWarning("Edge TTS returned 403 — API version may be stale. See KI-001 in KNOWN_ISSUES.md");
            }
            else
            {
                logger?.LogWarning("Edge TTS synthesis or playback failed: {Message}", ex.Message);
            }
            throw; // Re-throw so CompanionManager can fall back to Windows Speech
        }
    }

    public void Stop()
    {
        lock (playbackLock)
        {
            try
            {
                audioPlayer?.Stop();
                audioPlayer?.Dispose();
                audioResampler?.Dispose();
                audioReader?.Dispose();
            }
            catch { }
            audioPlayer = null;
            audioResampler = null;
            audioReader = null;
        }
    }

    private void StartPlayback(byte[] mp3Audio)
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var renderDevice = ResolveOutputDevice(deviceEnumerator);
            var deviceMixFormat = renderDevice.AudioClient.MixFormat;

            audioReader = new Mp3FileReader(new MemoryStream(mp3Audio));
            audioResampler = new MediaFoundationResampler(audioReader, deviceMixFormat) { ResamplerQuality = 60 };

            var wasapiOut = new WasapiOut(renderDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 150);
            wasapiOut.Init(audioResampler);
            wasapiOut.Play();
            audioPlayer = wasapiOut;
            logger?.LogInformation("TTS playing via WASAPI: {Device}", renderDevice.FriendlyName);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("WASAPI TTS failed ({Message}), falling back to WaveOut", ex.Message);
            audioResampler?.Dispose();
            audioResampler = null;
            audioReader?.Dispose();

            audioReader = new Mp3FileReader(new MemoryStream(mp3Audio));
            var waveOut = new WaveOutEvent { DeviceNumber = -1 };
            waveOut.Init(audioReader);
            waveOut.Play();
            audioPlayer = waveOut;
        }
    }

    private static MMDevice ResolveOutputDevice(MMDeviceEnumerator deviceEnumerator)
    {
        string? preferredId = FlowAISettings.Current.OutputDeviceId;
        if (!string.IsNullOrEmpty(preferredId))
        {
            try
            {
                var pinned = deviceEnumerator.GetDevice(preferredId);
                if (pinned is { State: DeviceState.Active }) return pinned;
            }
            catch { }
        }
        return deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static async Task<byte[]> SynthesizeToMp3Async(string text, CancellationToken cancellationToken)
    {
        string connectionId = Guid.NewGuid().ToString("N");
        string requestUrl =
            "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
            $"?TrustedClientToken={TrustedClientToken}" +
            $"&Sec-MS-GEC={ComputeSecMsGecToken()}" +
            $"&Sec-MS-GEC-Version={EdgeVersion}" +
            $"&ConnectionId={connectionId}";

        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Pragma", "no-cache");
        webSocket.Options.SetRequestHeader("Cache-Control", "no-cache");
        webSocket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        webSocket.Options.SetRequestHeader("User-Agent", UserAgent);
        webSocket.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(15));
        await webSocket.ConnectAsync(new Uri(requestUrl), linkedCts.Token);

        string speechConfig =
            "X-Timestamp: " + DateTime.UtcNow.ToString("o") + "\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Path: speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";

        byte[] speechConfigBytes = Encoding.UTF8.GetBytes(speechConfig);
        await webSocket.SendAsync(speechConfigBytes, WebSocketMessageType.Text, true, linkedCts.Token);

        string voice = FlowAISettings.Current.TtsVoice;
        string ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{voice}'>" +
            System.Security.SecurityElement.Escape(text) +
            "</voice></speak>";

        string ssmlMessage =
            "X-RequestId: " + connectionId + "\r\n" +
            "Content-Type: application/ssml+xml\r\n" +
            "X-Timestamp: " + DateTime.UtcNow.ToString("o") + "\r\n" +
            "Path: ssml\r\n\r\n" +
            ssml;

        byte[] ssmlBytes = Encoding.UTF8.GetBytes(ssmlMessage);
        await webSocket.SendAsync(ssmlBytes, WebSocketMessageType.Text, true, linkedCts.Token);

        using var audioStream = new MemoryStream();
        byte[] receiveBuffer = new byte[8192];

        while (webSocket.State == WebSocketState.Open && !linkedCts.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(receiveBuffer, linkedCts.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                // Binary audio chunk has a 2-byte header length, followed by header text, followed by MP3 data
                if (result.Count > 2)
                {
                    int headerLength = (receiveBuffer[0] << 8) | receiveBuffer[1];
                    int audioOffset = 2 + headerLength;
                    if (result.Count > audioOffset)
                    {
                        audioStream.Write(receiveBuffer, audioOffset, result.Count - audioOffset);
                    }
                }
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                string textMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                if (textMessage.Contains("Path:turn.end")) break;
            }
        }

        return audioStream.ToArray();
    }

    private static string ComputeSecMsGecToken()
    {
        long ticks = DateTime.UtcNow.Ticks - 621355968000000000L;
        long seconds = ticks / 10000000L;
        long roundedSeconds = seconds - (seconds % 300);
        long windowsFileTime = (roundedSeconds * 10000000L) + 116444736000000000L;

        string stringToHash = $"{windowsFileTime}{TrustedClientToken}";
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(stringToHash));
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        Stop();
    }
}
