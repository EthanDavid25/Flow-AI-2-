using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowAI.Core.Configuration;

public sealed class FlowAISettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object FileLock = new();
    private static FlowAISettings? current;

    public static FlowAISettings Current
    {
        get
        {
            if (current == null)
            {
                lock (FileLock)
                {
                    current ??= LoadOrCreate();
                }
            }
            return current;
        }
    }

    public static string AppDataDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowAI");

    public static string SettingsFilePath =>
        System.IO.Path.Combine(AppDataDirectory, "settings.json");

    // ── Operating Mode Settings ─────────────────────────────────────
    public string OperatingMode { get; set; } = "Auto"; // "Auto", "Online", "Offline"

    // ── AI Provider Settings ─────────────────────────────────────────
    public string AIProvider { get; set; } = "Ollama"; // "Ollama", "Kimi", "Gemini", "OpenAI", "Claude"
    public string ActiveCloudProvider { get; set; } = "Kimi"; // "Kimi", "Gemini", "OpenAI", "Claude"
    public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string OllamaModel { get; set; } = "qwen3-vl:2b";
    public string KimiEndpoint { get; set; } = "https://api.moonshot.cn/v1";
    public string KimiModel { get; set; } = "moonshot-v1-auto";
    public string GeminiModel { get; set; } = "gemini-1.5-flash";
    public string OpenAIModel { get; set; } = "gpt-4o-mini";
    public string ClaudeModel { get; set; } = "claude-3-5-sonnet";

    // ── Voice & Audio Settings ───────────────────────────────────────
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public string SttProvider { get; set; } = "Whisper"; // "Whisper", "WindowsSpeech"
    public string WhisperModel { get; set; } = "base";   // "tiny", "base", "small"
    public string TtsProvider { get; set; } = "EdgeTts"; // "EdgeTts", "WindowsSpeech"
    public string TtsVoice { get; set; } = "en-US-AriaNeural";
    public double TtsSpeed { get; set; } = 1.0;
    public double TtsVolume { get; set; } = 1.0;
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }

    // ── Appearance ───────────────────────────────────────────────────
    public string Theme { get; set; } = "Dark";
    public string GreenAccentHex { get; set; } = "#00FF88";
    public double CursorSize { get; set; } = 18.0;
    public double CursorFollowStrength { get; set; } = 1.0;
    public double AnimationSpeed { get; set; } = 1.0;
    public bool ShowCursorGlow { get; set; } = true;
    public bool ShowBoundingBox { get; set; } = true;

    // ── Self-Learning Engine ─────────────────────────────────────────
    public bool LearningEnabled { get; set; } = true;
    public bool StoreScreenshotsWithWorkflows { get; set; } = false; // Never store screenshots by default
    public bool AutoRetrieveCorrections { get; set; } = true;

    // ── Automation & Confirmation ────────────────────────────────────
    public bool RequireConfirmationForConsequential { get; set; } = true;
    public bool VerifyActionOutcomes { get; set; } = true;

    // ── Privacy ──────────────────────────────────────────────────────
    public bool ExcludeFromScreenCapture { get; set; } = true;
    public bool MaskSensitiveFormFields { get; set; } = true;

    // ── Onboarding ───────────────────────────────────────────────────
    public bool FirstRunCompleted { get; set; } = false;

    public void Save()
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }

    public static FlowAISettings LoadOrCreate()
    {
        lock (FileLock)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<FlowAISettings>(json, JsonOptions);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            var fresh = new FlowAISettings();
            fresh.Save();
            return fresh;
        }
    }
}
