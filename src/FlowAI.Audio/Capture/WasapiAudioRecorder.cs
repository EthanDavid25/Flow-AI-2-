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

    public static IReadOnlyList<(string id, string name)> GetCaptureDevices()
    {
        var list = new List<(string, string)>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                list.Add((dev.ID, dev.FriendlyName));
            }
        }
        catch { }
        return list;
    }

    public static IReadOnlyList<(string id, string name)> GetRenderDevices()
    {
        var list = new List<(string, string)>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add((dev.ID, dev.FriendlyName));
            }
        }
        catch { }
        return list;
    }

    public void StartRecording()
    {
        lock (syncLock)
        {
            if (isRecording) return;

            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                MMDevice? targetDevice = null;

                string? configuredId = FlowAI.Core.Configuration.FlowAISettings.Current.InputDeviceId;
                if (!string.IsNullOrWhiteSpace(configuredId))
                {
                    try { targetDevice = deviceEnumerator.GetDevice(configuredId); } catch { }
                }

                if (targetDevice == null)
                {
                    try { targetDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { }
                }
                if (targetDevice == null)
                {
                    try { targetDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); } catch { }
                }
                if (targetDevice == null)
                {
                    try { targetDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); } catch { }
                }
                if (targetDevice == null)
                {
                    var activeEndpoints = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                    targetDevice = activeEndpoints.FirstOrDefault();
                }

                if (targetDevice == null)
                {
                    logger?.LogWarning("No active audio capture device found on this system");
                    return;
                }

                capture = new WasapiCapture(targetDevice);
                capture.DataAvailable += HandleDataAvailable;
                capture.RecordingStopped += HandleRecordingStopped;

                recordedAudioStream = new MemoryStream();
                capture.StartRecording();
                isRecording = true;
                logger?.LogInformation("Started WASAPI mic recording on {DeviceName} ({Rate}Hz {Ch}ch)",
                    targetDevice.FriendlyName, capture.WaveFormat.SampleRate, capture.WaveFormat.Channels);
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
            logger?.LogInformation("Raw captured audio: {Bytes} bytes from WASAPI", rawBytes.Length);

            // Resample/convert to 16kHz Mono 16-bit PCM for Whisper
            byte[] pcm16 = ConvertTo16kHzMono16Bit(rawBytes, capture?.WaveFormat);
            byte[] wavAudio = WrapInWavHeader(pcm16, 16000, 1, 16);
            DisposeCapture();
            return wavAudio;
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

    private byte[] ConvertTo16kHzMono16Bit(byte[] rawAudio, WaveFormat? sourceFormat)
    {
        if (rawAudio.Length == 0 || sourceFormat == null)
        {
            logger?.LogWarning("ConvertTo16kHzMono16Bit: empty input or null format");
            return Array.Empty<byte>();
        }

        try
        {
            // MediaFoundation must be initialized before using MediaFoundationResampler
            NAudio.MediaFoundation.MediaFoundationApi.Startup();

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

            byte[] result = outputStream.ToArray();
            if (result.Length > 0)
            {
                logger?.LogInformation("PCM conversion (MF): {InputBytes} raw → {OutputBytes} PCM16 @ 16kHz",
                    rawAudio.Length, result.Length);
                return result;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "MediaFoundationResampler failed; falling back to pure managed resampler");
        }

        // Pure managed fallback: works unconditionally across all audio formats
        try
        {
            byte[] managedResult = ConvertManagedTo16kHzMono16Bit(rawAudio, sourceFormat);
            logger?.LogInformation("PCM conversion (Managed): {InputBytes} raw → {OutputBytes} PCM16 @ 16kHz",
                rawAudio.Length, managedResult.Length);
            return managedResult;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Both MF and Managed PCM conversions failed on {Bytes} bytes", rawAudio.Length);
            return Array.Empty<byte>();
        }
    }

    private static byte[] ConvertManagedTo16kHzMono16Bit(byte[] rawAudio, WaveFormat sourceFormat)
    {
        int channels = Math.Max(1, sourceFormat.Channels);
        int sampleRate = sourceFormat.SampleRate;
        int bitsPerSample = sourceFormat.BitsPerSample;
        bool isFloat = sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat;

        int bytesPerSample = Math.Max(1, bitsPerSample / 8);
        int frameSize = channels * bytesPerSample;
        int totalFrames = rawAudio.Length / frameSize;
        if (totalFrames == 0) return Array.Empty<byte>();

        float[] monoSamples = new float[totalFrames];

        for (int f = 0; f < totalFrames; f++)
        {
            float channelSum = 0f;
            int frameOffset = f * frameSize;

            for (int ch = 0; ch < channels; ch++)
            {
                int sampleOffset = frameOffset + ch * bytesPerSample;
                float sampleVal = 0f;

                if (isFloat && bytesPerSample == 4 && sampleOffset + 4 <= rawAudio.Length)
                {
                    sampleVal = BitConverter.ToSingle(rawAudio, sampleOffset);
                }
                else if (bitsPerSample == 16 && sampleOffset + 2 <= rawAudio.Length)
                {
                    short s = BitConverter.ToInt16(rawAudio, sampleOffset);
                    sampleVal = s / 32768f;
                }
                else if (bitsPerSample == 24 && sampleOffset + 3 <= rawAudio.Length)
                {
                    int s = (rawAudio[sampleOffset] << 8) | (rawAudio[sampleOffset + 1] << 16) | (rawAudio[sampleOffset + 2] << 24);
                    sampleVal = s / 2147483648f;
                }
                else if (bitsPerSample == 32 && sampleOffset + 4 <= rawAudio.Length)
                {
                    int s = BitConverter.ToInt32(rawAudio, sampleOffset);
                    sampleVal = s / 2147483648f;
                }

                channelSum += sampleVal;
            }

            monoSamples[f] = channelSum / channels;
        }

        // Resample from sourceRate to 16,000 Hz
        int targetRate = 16000;
        int outputCount = (int)((long)monoSamples.Length * targetRate / sampleRate);
        byte[] output = new byte[outputCount * 2];
        double ratio = (double)sampleRate / targetRate;

        for (int outIdx = 0; outIdx < outputCount; outIdx++)
        {
            double srcIdx = outIdx * ratio;
            int idxFloor = (int)srcIdx;
            int idxCeil = Math.Min(idxFloor + 1, monoSamples.Length - 1);
            double frac = srcIdx - idxFloor;

            float interpolated = (float)(monoSamples[idxFloor] * (1.0 - frac) + monoSamples[idxCeil] * frac);
            interpolated = Math.Clamp(interpolated, -1.0f, 1.0f);
            short shortSample = (short)Math.Round(interpolated * 32767.0);

            output[outIdx * 2] = (byte)(shortSample & 0xFF);
            output[outIdx * 2 + 1] = (byte)((shortSample >> 8) & 0xFF);
        }

        return output;
    }

    /// <summary>
    /// Wraps raw 16-bit PCM audio in a valid RIFF/WAVE header so wave parsers (such as Whisper.net)
    /// can parse format metadata directly from the stream without failing.
    /// </summary>
    public static byte[] WrapInWavHeader(byte[] pcmData, int sampleRate = 16000, short channels = 1, short bitsPerSample = 16)
    {
        if (pcmData.Length == 0) return Array.Empty<byte>();

        // If already has RIFF header, return as-is
        if (pcmData.Length >= 4 &&
            pcmData[0] == 0x52 && pcmData[1] == 0x49 && pcmData[2] == 0x46 && pcmData[3] == 0x46)
        {
            return pcmData;
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int subChunk2Size = pcmData.Length;
        int chunkSize = 36 + subChunk2Size;

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(chunkSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // "fmt " subchunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // subchunk1 size for PCM
        bw.Write((short)1); // audio format: 1 = PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);

        // "data" subchunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(subChunk2Size);
        bw.Write(pcmData);

        return ms.ToArray();
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
