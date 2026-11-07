using System.IO;
using FlowAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FlowAI.Audio.Capture;

public sealed class WasapiAudioRecorder : IAudioRecordingService
{
    private readonly ILogger<WasapiAudioRecorder>? logger;
    private WasapiCapture? capture;
    private MemoryStream? recordedAudioStream;
    private readonly object syncLock = new();
    private bool isRecording;

    public event Action<float>? AudioLevelChanged;

    public bool IsRecording
    {
        get
        {
            lock (syncLock) return isRecording;
        }
    }

    public WasapiAudioRecorder(ILogger<WasapiAudioRecorder>? logger = null)
    {
        this.logger = logger;
    }

    public void StartRecording()
    {
        lock (syncLock)
        {
            if (isRecording) return;

            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

                capture = new WasapiCapture(defaultDevice);
                capture.DataAvailable += HandleDataAvailable;
                capture.RecordingStopped += HandleRecordingStopped;

                recordedAudioStream = new MemoryStream();
                capture.StartRecording();
                isRecording = true;
                logger?.LogInformation("Started WASAPI mic recording on {DeviceName}", defaultDevice.FriendlyName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to start WASAPI microphone capture");
                isRecording = false;
            }
        }
    }

    public byte[] StopRecordingAndGetPcm16()
    {
        lock (syncLock)
        {
            if (!isRecording) return Array.Empty<byte>();

            try
            {
                capture?.StopRecording();
            }
            catch { }

            isRecording = false;
            byte[] rawBytes = recordedAudioStream?.ToArray() ?? Array.Empty<byte>();

            // Resample/convert to 16kHz Mono 16-bit PCM for Whisper
            byte[] pcm16 = ConvertTo16kHzMono16Bit(rawBytes, capture?.WaveFormat);
            DisposeCapture();
            return pcm16;
        }
    }

    public void CancelRecording()
    {
        lock (syncLock)
        {
            if (!isRecording) return;
            try
            {
                capture?.StopRecording();
            }
            catch { }
            isRecording = false;
            DisposeCapture();
        }
    }

    private void HandleDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        lock (syncLock)
        {
            recordedAudioStream?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        // Calculate RMS power for audio-reactive waveform
        float rms = CalculateRmsLevel(e.Buffer, e.BytesRecorded, capture?.WaveFormat);
        AudioLevelChanged?.Invoke(rms);
    }

    private void HandleRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            logger?.LogWarning("WASAPI recording stopped with error: {Message}", e.Exception.Message);
        }
    }

    private static float CalculateRmsLevel(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        if (format == null || bytesRecorded <= 0) return 0f;

        double sum = 0;
        int sampleCount = 0;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int index = 0; index < bytesRecorded - 3; index += 4)
            {
                float sample = BitConverter.ToSingle(buffer, index);
                sum += sample * sample;
                sampleCount++;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (int index = 0; index < bytesRecorded - 1; index += 2)
            {
                short sample = BitConverter.ToInt16(buffer, index);
                float normalized = sample / 32768f;
                sum += normalized * normalized;
                sampleCount++;
            }
        }

        if (sampleCount == 0) return 0f;
        double rms = Math.Sqrt(sum / sampleCount);
        // Boost low levels so soft voice is clearly visible on waveform
        return (float)Math.Clamp(rms * 4.0, 0.0, 1.0);
    }

    private static byte[] ConvertTo16kHzMono16Bit(byte[] rawAudio, WaveFormat? sourceFormat)
    {
        if (rawAudio.Length == 0 || sourceFormat == null) return Array.Empty<byte>();

        try
        {
            using var inputStream = new MemoryStream(rawAudio);
            using var rawReader = new RawSourceWaveStream(inputStream, sourceFormat);

            var outFormat = new WaveFormat(16000, 16, 1);
            using var resampler = new MediaFoundationResampler(rawReader, outFormat)
            {
                ResamplerQuality = 60
            };

            using var outputStream = new MemoryStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                outputStream.Write(buffer, 0, read);
            }

            return outputStream.ToArray();
        }
        catch
        {
            return rawAudio;
        }
    }

    private void DisposeCapture()
    {
        capture?.Dispose();
        capture = null;
        recordedAudioStream?.Dispose();
        recordedAudioStream = null;
    }

    public void Dispose()
    {
        CancelRecording();
    }
}
