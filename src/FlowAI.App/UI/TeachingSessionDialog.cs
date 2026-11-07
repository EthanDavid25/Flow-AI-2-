using System.Windows;
using System.Windows.Controls;

namespace FlowAI.App.UI;

/// <summary>Simple dialog to capture workflow name and description before starting a teaching session.</summary>
public sealed class TeachingSessionDialog : Window
{
    public string WorkflowName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string TargetApp { get; private set; } = string.Empty;

    private readonly TextBox _nameBox;
    private readonly TextBox _descBox;
    private readonly TextBox _appBox;

    public TeachingSessionDialog()
    {
        Title = "Teach FlowAI a Workflow";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(23, 25, 24));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var fg = System.Windows.Media.Brushes.White;
        var inputBg = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(32, 34, 33));

        _nameBox = CreateInput(inputBg, fg, "e.g. Send daily report email");
        _descBox = CreateInput(inputBg, fg, "Describe what this workflow does");
        _appBox  = CreateInput(inputBg, fg, "e.g. Outlook (optional)");

        var saveBtn = new Button
        {
            Content = "Start Recording",
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0, 230, 100)),
            Foreground = System.Windows.Media.Brushes.Black,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        saveBtn.Click += (_, _) =>
        {
            WorkflowName = _nameBox.Text.Trim();
            Description = _descBox.Text.Trim();
            TargetApp = _appBox.Text.Trim();
            DialogResult = !string.IsNullOrWhiteSpace(WorkflowName);
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Margin = new Thickness(8, 12, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(120, 144, 156)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(42, 44, 43)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancelBtn.Click += (_, _) => DialogResult = false;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Label("Workflow Name", fg));
        panel.Children.Add(_nameBox);
        panel.Children.Add(Label("Description", fg));
        panel.Children.Add(_descBox);
        panel.Children.Add(Label("Target Application (optional)", fg));
        panel.Children.Add(_appBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(cancelBtn);
        panel.Children.Add(btnRow);

        Content = panel;
    }

    private static TextBlock Label(string text, System.Windows.Media.Brush fg) =>
        new() { Text = text, Foreground = fg, FontSize = 11,
                Margin = new Thickness(0, 10, 0, 3) };

    private static TextBox CreateInput(System.Windows.Media.Brush bg,
                                       System.Windows.Media.Brush fg, string placeholder)
    {
        var tb = new TextBox
        {
            Background = bg,
            Foreground = fg,
            CaretBrush = fg,
            BorderThickness = new Thickness(1),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(42, 44, 43)),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            Tag = placeholder
        };
        return tb;
    }
}
