using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FlowAI.Core.Enums;
using Microsoft.Extensions.Logging;

namespace FlowAI.App.UI;

/// <summary>
/// The always-on-top companion panel: chat transcript, text input, state indicator and controls.
/// ShowActivated=True so the TextBox can receive keyboard focus normally.
/// </summary>
public partial class CompanionPanelWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────
    private readonly ILogger<CompanionPanelWindow>? _logger;
    private CompanionManager? _manager;
    private bool _isTeachingSession;
    private TextBlock? _currentStreamingTextBlock;
    private readonly List<(string role, string text)> _chatHistory = new();
    private const int MaxChatMessages = 20;
    private MiniCompanionWindow? _miniCompanion;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action? SettingsRequested;
    public event Action? MemoryRequested;
    public event Action? QuitRequested;

    public CompanionPanelWindow(ILogger<CompanionPanelWindow>? logger = null)
    {
        _logger = logger;
        InitializeComponent();
        PositionNearTaskbar();
    }

    // ── Companion manager binding ─────────────────────────────────────────────
    public void BindManager(CompanionManager manager)
    {
        _manager = manager;
        _manager.StateChanged += OnStateChanged;
        _manager.TranscriptUpdated += OnTranscriptUpdated;
        _manager.ResponseUpdated += OnResponseUpdated;
        _manager.ResponseStreamChunk += OnResponseStreamChunk;
        _manager.StatusUpdated += OnStatusUpdated;
        _manager.AudioLevelChanged += OnAudioLevel;

        // Initial values
        UpdateBadges();
        StatusText.Text = manager.IsAIConnected ? $"Connected — {manager.CurrentModel}" : "Offline — start Ollama";
    }

    public void UpdateBadges()
    {
        if (_manager == null) return;
        string mode = FlowAI.Core.Configuration.FlowAISettings.Current.OperatingMode?.ToUpperInvariant() ?? "AUTO";
        ModeBadge.Text = mode;
        if (mode == "OFFLINE")
        {
            ModeBadge.Foreground = new SolidColorBrush(Color.FromRgb(255, 171, 64)); // Orange/amber
            ModeBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(50, 255, 171, 64));
        }
        else if (mode == "ONLINE")
        {
            ModeBadge.Foreground = new SolidColorBrush(Color.FromRgb(66, 165, 245)); // Blue
            ModeBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(50, 66, 165, 245));
        }
        else
        {
            ModeBadge.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
            ModeBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(50, 0, 230, 118));
        }

        ProviderBadge.Text = _manager.AIProviderName;
    }

    // ── Window chrome ─────────────────────────────────────────────────────────
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // No WS_EX_NOACTIVATE here — we need the TextBox to receive keyboard focus.
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        QueryInput.Focus();
        Keyboard.Focus(QueryInput);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (!IsActive)
        {
            Activate();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => EnterMiniMode();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

    // ── Mini Companion Mode ───────────────────────────────────────────────────
    public void EnterMiniMode()
    {
        Hide();
        if (_miniCompanion == null && _manager != null)
            _miniCompanion = new MiniCompanionWindow(this, _manager);
        _miniCompanion?.ShowMini();
    }

    public void RestoreFromMini()
    {
        _miniCompanion?.Hide();
        Show();
        Activate();
        QueryInput.Focus();
        Keyboard.Focus(QueryInput);
    }

    // ── Position on first show ────────────────────────────────────────────────
    private void PositionNearTaskbar()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
    }

    // ── CompanionManager callbacks (arrive on any thread → dispatch) ──────────
    private void OnStateChanged(FlowAIState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateStateIndicator(state);
        }, DispatcherPriority.Normal);
    }

    private void OnTranscriptUpdated(string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _currentStreamingTextBlock = null;
            // For voice queries the transcript arrives here; for text queries the bubble
            // was already added in SendQuery() so skip duplicates.
            bool alreadyShown = _chatHistory.Count > 0
                && _chatHistory[^1].role == "user"
                && _chatHistory[^1].text == text;
            if (!alreadyShown)
                AppendChatBubble("user", text);
        }, DispatcherPriority.Normal);
    }

    private void OnResponseStreamChunk(string chunk)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_currentStreamingTextBlock == null)
            {
                _currentStreamingTextBlock = CreateAiBubble();
            }
            _currentStreamingTextBlock.Text += chunk;
            ChatScrollViewer.ScrollToEnd();
        }, DispatcherPriority.Normal);
    }

    private void OnResponseUpdated(string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_currentStreamingTextBlock != null)
            {
                _currentStreamingTextBlock.Text = text;
                _chatHistory.Add(("ai", text));
                _currentStreamingTextBlock = null;
            }
            else
            {
                AppendChatBubble("ai", text);
            }
            ChatScrollViewer.ScrollToEnd();
        }, DispatcherPriority.Normal);
    }

    private void OnStatusUpdated(string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = status;
            UpdateBadges();
        }, DispatcherPriority.Normal);
    }

    private void OnAudioLevel(float level)
    {
        // Visual waveform feedback via opacity of state orb
        Dispatcher.BeginInvoke(() =>
        {
            if (StateOrbColor != null && _manager?.CurrentState == FlowAIState.Listening)
            {
                StateOrb.Opacity = 0.4 + level * 0.6;
            }
        }, DispatcherPriority.Render);
    }

    // ── Chat bubble rendering ─────────────────────────────────────────────────
    private TextBlock CreateAiBubble()
    {
        var bubble = new Border
        {
            CornerRadius = new CornerRadius(12, 12, 12, 2),
            Background = new SolidColorBrush(Color.FromRgb(23, 25, 24)),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 2, 40, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 300
        };

        var tb = new TextBlock
        {
            Text = string.Empty,
            Foreground = new SolidColorBrush(Color.FromRgb(232, 245, 233)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };

        bubble.Child = tb;
        ChatPanel.Children.Add(bubble);
        ChatScrollViewer.ScrollToEnd();
        return tb;
    }

    private void AppendChatBubble(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _chatHistory.Add((role, text));

        // Trim old messages
        while (_chatHistory.Count > MaxChatMessages)
        {
            _chatHistory.RemoveAt(0);
            if (ChatPanel.Children.Count > 0)
                ChatPanel.Children.RemoveAt(0);
        }

        bool isUser = role == "user";

        var bubble = new Border
        {
            CornerRadius = isUser ? new CornerRadius(12, 12, 2, 12) : new CornerRadius(12, 12, 12, 2),
            Background = new SolidColorBrush(isUser
                ? Color.FromRgb(26, 46, 36)
                : Color.FromRgb(23, 25, 24)),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = isUser
                ? new Thickness(40, 2, 0, 2)
                : new Thickness(0, 2, 40, 2),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 300
        };

        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(232, 245, 233)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };

        bubble.Child = tb;
        ChatPanel.Children.Add(bubble);

        // Scroll to bottom
        ChatScrollViewer.ScrollToEnd();

        // Fade-in animation
        bubble.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        bubble.BeginAnimation(OpacityProperty, anim);
    }

    // ── State indicator ───────────────────────────────────────────────────────
    private void UpdateStateIndicator(FlowAIState state)
    {
        if (state == FlowAIState.Idle)
        {
            StateIndicatorBorder.Visibility = Visibility.Collapsed;
            return;
        }

        StateIndicatorBorder.Visibility = Visibility.Visible;

        var (label, color) = state switch
        {
            FlowAIState.Listening       => ("🎤 Listening…", Color.FromRgb(0, 255, 136)),
            FlowAIState.Transcribing    => ("📝 Transcribing…", Color.FromRgb(0, 230, 160)),
            FlowAIState.AnalysingScreen => ("🖥 Reading screen…", Color.FromRgb(0, 200, 180)),
            FlowAIState.Thinking        => ("💭 Thinking…", Color.FromRgb(0, 180, 220)),
            FlowAIState.Guiding         => ("🎯 Guiding…", Color.FromRgb(0, 255, 136)),
            FlowAIState.Speaking        => ("🔊 Speaking…", Color.FromRgb(0, 230, 100)),
            FlowAIState.Learning        => ("📚 Recording workflow…", Color.FromRgb(100, 255, 100)),
            FlowAIState.Executing       => ("⚡ Executing…", Color.FromRgb(255, 200, 0)),
            FlowAIState.Error           => ("⚠ Error", Color.FromRgb(255, 82, 82)),
            _                           => ("Processing…", Color.FromRgb(0, 255, 136))
        };

        StateText.Text = label;
        StateOrbColor.Color = color;

        // Pulse animation on the orb
        var pulse = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        StateOrb.BeginAnimation(OpacityProperty, pulse);
    }

    // ── Input handling ────────────────────────────────────────────────────────
    private void QueryInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
            && !string.IsNullOrWhiteSpace(QueryInput.Text))
        {
            SendQuery();
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendQuery();

    private void SendQuery()
    {
        string query = QueryInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(query) || _manager == null) return;

        _logger?.LogInformation("User text input: {Query}", query);

        // Clear input and immediately show user bubble — don't wait for manager callback
        QueryInput.Text = string.Empty;
        QueryInput.Focus();
        Keyboard.Focus(QueryInput);
        AppendChatBubble("user", query);

        _ = _manager.AskTextAsync(query);
    }

    // ── PTT button (mouse hold) ───────────────────────────────────────────────
    // These are backup buttons; real PTT is handled by the low-level keyboard hook.
    private void PttButton_Down(object sender, MouseButtonEventArgs e)
    {
        PttButton.Background = new SolidColorBrush(Color.FromArgb(80, 0, 255, 136));
    }

    private void PttButton_Up(object sender, MouseButtonEventArgs e)
    {
        PttButton.Background = Brushes.Transparent;
    }

    // ── Settings / Memory ─────────────────────────────────────────────────────
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void MemoryButton_Click(object sender, RoutedEventArgs e) => MemoryRequested?.Invoke();

    // ── Teaching session ──────────────────────────────────────────────────────
    private void TeachButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;

        if (!_isTeachingSession)
        {
            var dialog = new TeachingSessionDialog { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.WorkflowName))
            {
                _manager.StartTeachingSession(dialog.WorkflowName, dialog.Description, dialog.TargetApp);
                _isTeachingSession = true;
                TeachButton.Content = "✅ Save Workflow";
                TeachButton.Background = new SolidColorBrush(Color.FromArgb(60, 0, 255, 136));
            }
        }
        else
        {
            _ = FinishTeachingAsync();
        }
    }

    private async Task FinishTeachingAsync()
    {
        if (_manager == null) return;
        string name = await _manager.StopTeachingSessionAsync();
        _isTeachingSession = false;
        TeachButton.Content = "📚 Teach";
        TeachButton.Background = Brushes.Transparent;

        if (!string.IsNullOrWhiteSpace(name))
            AppendChatBubble("ai", $"Workflow '{name}' saved. I'll remember it for next time!");
    }

    // ── Public toggle ──────────────────────────────────────────────────────────
    public void ToggleVisibility()
    {
        if (IsVisible) Hide(); else Show();
    }
}
