using System.Net.Http;
using System.Windows;
using FlowAI.AI.Security;
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
        // AI
        AIProviderCombo.SelectedIndex = _settings.AIProvider == "Kimi" ? 1 : 0;
        OllamaEndpointBox.Text = _settings.OllamaEndpoint;
        OllamaModelBox.Text = _settings.OllamaModel;

        // Try loading stored Kimi key (don't fail if not stored)
        try
        {
            var key = CredentialStore.LoadSecret("kimi");
            if (!string.IsNullOrEmpty(key))
                KimiKeyBox.Password = key;
        }
        catch { }

        // Voice
        HotkeyBox.Text = _settings.Hotkey;
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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // AI
        var provItem = AIProviderCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        _settings.AIProvider = provItem?.Tag?.ToString() ?? "Ollama";
        _settings.OllamaEndpoint = OllamaEndpointBox.Text.Trim();
        _settings.OllamaModel = OllamaModelBox.Text.Trim();

        // Store Kimi key securely if provided
        if (!string.IsNullOrWhiteSpace(KimiKeyBox.Password))
        {
            try { CredentialStore.SaveSecret("kimi", KimiKeyBox.Password); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save Kimi key"); }
        }

        // Voice
        var sttItem = SttCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        _settings.SttProvider = sttItem?.Tag?.ToString() ?? "Whisper";

        var whisperItem = WhisperModelCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
        _settings.WhisperModel = whisperItem?.Tag?.ToString() ?? "base";

        var ttsItem = TtsCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
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
        _logger?.LogInformation("Settings saved");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
