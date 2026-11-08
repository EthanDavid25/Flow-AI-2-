using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FlowAI.Core.Enums;

namespace FlowAI.App.UI;

/// <summary>
/// Mini Companion Mode: A compact, dark translucent floating glass widget.
/// Stays topmost and displays the emerald orb, status, push-to-talk mic,
/// and quick restore to the main companion panel.
/// </summary>
public partial class MiniCompanionWindow : Window
{
    private readonly CompanionPanelWindow _parentPanel;
    private readonly CompanionManager _manager;
    private static Point? s_lastPosition;

    public MiniCompanionWindow(CompanionPanelWindow parent, CompanionManager manager)
    {
        _parentPanel = parent ?? throw new ArgumentNullException(nameof(parent));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        InitializeComponent();

        _manager.StateChanged += OnStateChanged;
        LocationChanged += OnLocationChanged;
    }

    /// <summary>
    /// Shows the mini companion window, restoring last position or docking to bottom-right.
    /// </summary>
    public void ShowMini()
    {
        var workArea = SystemParameters.WorkArea;

        if (s_lastPosition.HasValue)
        {
            Left = Math.Max(workArea.Left, Math.Min(s_lastPosition.Value.X, workArea.Right - Width));
            Top = Math.Max(workArea.Top, Math.Min(s_lastPosition.Value.Y, workArea.Bottom - Height));
        }
        else
        {
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 24;
        }

        UpdateStatusForState(_manager.CurrentState);
        Show();
    }

    /// <summary>
    /// Restores the main companion panel and hides the mini widget.
    /// </summary>
    public void RestoreToPanel()
    {
        Hide();
        _parentPanel.RestoreFromMini();
    }

    private void MiniWidget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        double clampedLeft = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
        double clampedTop = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));

        if (Math.Abs(clampedLeft - Left) > 0.5 || Math.Abs(clampedTop - Top) > 0.5)
        {
            Left = clampedLeft;
            Top = clampedTop;
        }

        s_lastPosition = new Point(Left, Top);
    }

    private void Orb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => RestoreToPanel();

    private void Status_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => RestoreToPanel();

    private void MiniRestoreButton_Click(object sender, RoutedEventArgs e) => RestoreToPanel();

    private void MiniCloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void MiniMicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manager.CurrentState == FlowAIState.Listening)
        {
            _manager.StopListening();
        }
        else
        {
            _manager.StartListening();
        }
    }

    private void OnStateChanged(FlowAIState state)
    {
        Dispatcher.BeginInvoke(() => UpdateStatusForState(state));
    }

    private void UpdateStatusForState(FlowAIState state)
    {
        switch (state)
        {
            case FlowAIState.Listening:
                MiniStatusText.Text = "Listening…";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 136));
                MiniOrbPulse.BeginAnimation(UIElement.OpacityProperty, CreatePulseAnimation());
                break;

            case FlowAIState.Transcribing:
                MiniStatusText.Text = "Transcribing…";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                break;

            case FlowAIState.Thinking:
            case FlowAIState.AnalysingScreen:
                MiniStatusText.Text = "Thinking…";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 64));
                MiniOrbPulse.BeginAnimation(UIElement.OpacityProperty, CreatePulseAnimation());
                break;

            case FlowAIState.Executing:
            case FlowAIState.Guiding:
                MiniStatusText.Text = "Executing…";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 136));
                break;

            case FlowAIState.Speaking:
                MiniStatusText.Text = "Speaking…";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(128, 222, 234));
                break;

            case FlowAIState.Error:
                MiniStatusText.Text = "Error";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                MiniOrbPulse.BeginAnimation(UIElement.OpacityProperty, null);
                MiniOrbPulse.Opacity = 0.8;
                break;

            default:
                MiniStatusText.Text = "FlowAI";
                MiniStatusText.Foreground = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                MiniOrbPulse.BeginAnimation(UIElement.OpacityProperty, null);
                MiniOrbPulse.Opacity = 0.3;
                break;
        }
    }

    private static DoubleAnimation CreatePulseAnimation()
    {
        return new DoubleAnimation
        {
            From = 0.2,
            To = 0.85,
            Duration = TimeSpan.FromMilliseconds(750),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
    }
}
