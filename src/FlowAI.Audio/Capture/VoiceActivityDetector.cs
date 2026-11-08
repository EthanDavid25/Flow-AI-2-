using System;

namespace FlowAI.Audio.Capture;

/// <summary>
/// Voice Activity Detector (VAD) that analyzes real-time audio streams (RMS power)
/// to detect start of speech and end-of-speech silence automatically.
/// </summary>
public sealed class VoiceActivityDetector
{
    public float SpeechThresholdRms { get; set; } = 0.018f;
    public float SilenceThresholdRms { get; set; } = 0.009f;
    public TimeSpan MinSpeechDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan SilenceEndpointDuration { get; set; } = TimeSpan.FromMilliseconds(1100);
    public TimeSpan MaxUtteranceDuration { get; set; } = TimeSpan.FromSeconds(25);

    private bool _inSpeech;
    private DateTime _speechStartTime;
    private DateTime _silenceStartTime;
    private DateTime _utteranceStartTime;

    public bool IsInSpeech => _inSpeech;

    public event Action? SpeechStarted;
    public event Action? SpeechEnded;

    public void Reset()
    {
        _inSpeech = false;
        _speechStartTime = DateTime.MinValue;
        _silenceStartTime = DateTime.MinValue;
        _utteranceStartTime = DateTime.MinValue;
    }

    /// <summary>
    /// Feeds the latest measured RMS level into the detector.
    /// Returns true if an end-of-speech endpoint or max timeout was triggered.
    /// </summary>
    public bool ProcessAudioFrame(float rmsLevel)
    {
        var now = DateTime.UtcNow;

        if (!_inSpeech)
        {
            if (rmsLevel >= SpeechThresholdRms)
            {
                if (_speechStartTime == DateTime.MinValue)
                {
                    _speechStartTime = now;
                }
                else if (now - _speechStartTime >= MinSpeechDuration)
                {
                    _inSpeech = true;
                    _utteranceStartTime = now;
                    _silenceStartTime = DateTime.MinValue;
                    SpeechStarted?.Invoke();
                }
            }
            else
            {
                _speechStartTime = DateTime.MinValue;
            }
        }
        else
        {
            // Safety timeout
            if (now - _utteranceStartTime >= MaxUtteranceDuration)
            {
                _inSpeech = false;
                SpeechEnded?.Invoke();
                return true;
            }

            if (rmsLevel < SilenceThresholdRms)
            {
                if (_silenceStartTime == DateTime.MinValue)
                {
                    _silenceStartTime = now;
                }
                else if (now - _silenceStartTime >= SilenceEndpointDuration)
                {
                    _inSpeech = false;
                    SpeechEnded?.Invoke();
                    return true;
                }
            }
            else
            {
                // Voice continued; reset silence endpoint timer
                _silenceStartTime = DateTime.MinValue;
            }
        }

        return false;
    }
}
