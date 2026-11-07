using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using FlowAI.AI.Security;
using FlowAI.Audio.Capture;
using FlowAI.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowAI.App.UI;

public partial class SettingsWindow : Window
{
    private readonly FlowAISettings _settings;
    private readonly ILogger<SettingsWindow>? _logger;

    public SettingsWindow(FlowAISettings settings, ILogger<SettingsWindow>? logger = null)
    {
        _settings = settings;
        _logger = logger;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Operating Mode
        OperatingModeCombo.SelectedIndex = _settings.OperatingMode?.ToLowerInvariant() switch
        {
            "online" => 1,
            "offline" => 2,
            _ => 0 // Auto
        };
        UpdateOperatingModeDescription();

        // Local AI (Ollama)
        OllamaEndpointBox.Text = _settings.OllamaEndpoint;
        OllamaModelBox.Text = _settings.OllamaModel;

        // Cloud AI
        CloudProviderCombo.SelectedIndex = _settings.ActiveCloudProvider?.ToLowerInvariant() switch
        {
            "gemini" => 1,
            "openai" => 2,
            "claude" => 3,
            _ => 0 // Kimi
        };

        // Try loading stored keys from DPAPI
        LoadProviderKey("kimi", KimiKeyBox);
        LoadProviderKey("gemini", GeminiKeyBox);
        LoadProviderKey("openai", OpenAIKeyBox);
        LoadProviderKey("claude", ClaudeKeyBox);

        // Voice
        HotkeyBox.Text = _settings.Hotkey;

        try
        {
            var devices = WasapiAudioRecorder.GetCaptureDevices();
            InputDeviceCombo.Items.Clear();
            var defaultItem = new ComboBoxItem { Content = "Default Windows Microphone", Tag = "" };
            InputDeviceCombo.Items.Add(defaultItem);
            int selectedIndex = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                var item = new ComboBoxItem { Content = devices[i].name, Tag = devices[i].id };
                InputDeviceCombo.Items.Add(item);
                if (!string.IsNullOrEmpty(_settings.InputDeviceId) && _settings.InputDeviceId == devices[i].id)
                {
                    selectedIndex = i + 1;
                }
            }
            InputDeviceCombo.SelectedIndex = selectedIndex;
        }
        catch { }

        SttCombo.SelectedIndex = _settings.SttProvider == "WindowsSpeech" ? 1 : 0;
        WhisperModelCombo.SelectedIndex = _settings.WhisperModel switch
        {
            "tiny"  => 0,
            "small" => 2,
            _       => 1  // base
        };
        TtsCombo.SelectedIndex = _settings.TtsProvider == "WindowsSpeech" ? 1 : 0;
        TtsVoiceBox.Text = _settings.TtsVoice;
        SpeedSlider.Value = _settings.TtsSpeed;

        // Appearance
        CursorSizeSlider.Value = _settings.CursorSize;
        ShowGlowCheck.IsChecked = _settings.ShowCursorGlow;
        ShowBboxCheck.IsChecked = _settings.ShowBoundingBox;
        AnimSpeedSlider.Value = _settings.AnimationSpeed;

        // Learning
        LearningEnabledCheck.IsChecked = _settings.LearningEnabled;
        AutoCorrectCheck.IsChecked = _settings.AutoRetrieveCorrections;
        StoreScreenshotsCheck.IsChecked = _settings.StoreScreenshotsWithWorkflows;

        // Privacy
        ExcludeFromCaptureCheck.IsChecked = _settings.ExcludeFromScreenCapture;
        MaskSensitiveCheck.IsChecked = _settings.MaskSensitiveFormFields;
        RequireConfirmCheck.IsChecked = _settings.RequireConfirmationForConsequential;

        // Diagnostics
        string logsDir = Path.Combine(FlowAISettings.AppDataDirectory, "Logs");
        LogsPathBox.Text = logsDir;
    }

    private static void LoadProviderKey(string providerKey, PasswordBox box)
    {
        try
        {
            string? secret = CredentialStore.LoadSecret(providerKey);
            if (!string.IsNullOrEmpty(secret))
                box.Password = secret;
        }
        catch { }
    }

    private void OperatingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOperatingModeDescription();
    }

    private void UpdateOperatingModeDescription()
    {
        if (OperatingModeDescText == null) return;

        int idx = OperatingModeCombo?.SelectedIndex ?? 0;
        OperatingModeDescText.Text = idx switch
        {
            1 => "ONLINE: FlowAI routes all queries to your active Cloud AI. Local inference is disabled.",
            2 => "OFFLINE: FlowAI runs 100% locally. Zero remote requests, zero screenshot uploads, SAPI voice synthesis.",
            _ => "AUTO (Recommended): FlowAI uses Cloud AI when available and configured, seamlessly falling back to local Ollama if offline."
        };
    }

    private async void CheckOllama_Click(object sender, RoutedEventArgs e)
    {
        OllamaStatusText.Text = "Checking…";
        OllamaStatusText.Foreground = System.Windows.Media.Brushes.Gray;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync(OllamaEndpointBox.Text.TrimEnd('/') + "/api/tags");
            if (resp.IsSuccessStatusCode)
            {
                OllamaStatusText.Text = "✓ Ollama is running";
                OllamaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 230, 100));
            }
            else
            {
                OllamaStatusText.Text = $"Ollama returned {(int)resp.StatusCode}";
                OllamaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 82, 82));
            }
        }
        catch (Exception ex)
        {
            OllamaStatusText.Text = $"Cannot reach Ollama: {ex.Message}";
            OllamaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 82, 82));
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string logsDir = Path.Combine(FlowAISettings.AppDataDirectory, "Logs");
            Directory.CreateDirectory(logsDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open logs directory");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Operating Mode & Cloud Provider
        var modeItem = OperatingModeCombo.SelectedItem as ComboBoxItem;
        _settings.OperatingMode = modeItem?.Tag?.ToString() ?? "Auto";

        var cloudItem = CloudProviderCombo.SelectedItem as ComboBoxItem;
        _settings.ActiveCloudProvider = cloudItem?.Tag?.ToString() ?? "Kimi";

        // Local Ollama
        _settings.OllamaEndpoint = OllamaEndpointBox.Text.Trim();
        _settings.OllamaModel = OllamaModelBox.Text.Trim();

        // Save DPAPI secrets if provided
        SaveKeyIfProvided("kimi", KimiKeyBox.Password);
        SaveKeyIfProvided("gemini", GeminiKeyBox.Password);
        SaveKeyIfProvided("openai", OpenAIKeyBox.Password);
        SaveKeyIfProvided("claude", ClaudeKeyBox.Password);

        // Voice
        if (InputDeviceCombo.SelectedItem is ComboBoxItem devItem)
        {
            _settings.InputDeviceId = string.IsNullOrWhiteSpace(devItem.Tag?.ToString()) ? null : devItem.Tag.ToString();
        }

        var sttItem = SttCombo.SelectedItem as ComboBoxItem;
        _settings.SttProvider = sttItem?.Tag?.ToString() ?? "Whisper";

        var whisperItem = WhisperModelCombo.SelectedItem as ComboBoxItem;
        _settings.WhisperModel = whisperItem?.Tag?.ToString() ?? "base";

        var ttsItem = TtsCombo.SelectedItem as ComboBoxItem;
        _settings.TtsProvider = ttsItem?.Tag?.ToString() ?? "EdgeTts";

        _settings.TtsVoice = TtsVoiceBox.Text.Trim();
        _settings.TtsSpeed = SpeedSlider.Value;

        // Appearance
        _settings.CursorSize = CursorSizeSlider.Value;
        _settings.ShowCursorGlow = ShowGlowCheck.IsChecked == true;
        _settings.ShowBoundingBox = ShowBboxCheck.IsChecked == true;
        _settings.AnimationSpeed = AnimSpeedSlider.Value;

        // Learning
        _settings.LearningEnabled = LearningEnabledCheck.IsChecked == true;
        _settings.AutoRetrieveCorrections = AutoCorrectCheck.IsChecked == true;
        _settings.StoreScreenshotsWithWorkflows = StoreScreenshotsCheck.IsChecked == true;

        // Privacy
        _settings.ExcludeFromScreenCapture = ExcludeFromCaptureCheck.IsChecked == true;
        _settings.MaskSensitiveFormFields = MaskSensitiveCheck.IsChecked == true;
        _settings.RequireConfirmationForConsequential = RequireConfirmCheck.IsChecked == true;

        _settings.Save();
        _logger?.LogInformation("Settings saved: Mode={Mode}, CloudProvider={Cloud}", _settings.OperatingMode, _settings.ActiveCloudProvider);
        DialogResult = true;
    }

    private void SaveKeyIfProvided(string provider, string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            try
            {
                CredentialStore.SaveSecret(provider, key.Trim());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save API key for {Provider}", provider);
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
