using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace FlowAI.App.Tray;

/// <summary>
/// Manages the system-tray icon and context menu.
/// Must be created and accessed on the STA thread that owns the Windows message pump
/// (use TrayThread in App.xaml.cs).
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly ILogger<TrayIconManager>? _logger;
    private NotifyIcon? _notifyIcon;
    private volatile bool _disposed;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action? ShowHideRequested;
    public event Action? SettingsRequested;
    public event Action? MemoryRequested;
    public event Action? QuitRequested;

    public TrayIconManager(ILogger<TrayIconManager>? logger = null)
    {
        _logger = logger;
    }

    // ── Initialise on message-pump thread ─────────────────────────────────────
    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateFlowAIIcon(),
            Text = "FlowAI — AI Cursor Companion",
            Visible = true
        };

        _notifyIcon.MouseClick += OnIconClick;

        var menu = new ContextMenuStrip();
        menu.Renderer = new DarkMenuRenderer();

        var headerItem = new ToolStripMenuItem("FlowAI")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        var showItem = new ToolStripMenuItem("Show / Hide Panel");
        showItem.Click += (_, _) => ShowHideRequested?.Invoke();

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var memoryItem = new ToolStripMenuItem("Memory & Learning…");
        memoryItem.Click += (_, _) => MemoryRequested?.Invoke();

        var quitItem = new ToolStripMenuItem("Quit FlowAI");
        quitItem.Click += (_, _) => QuitRequested?.Invoke();

        menu.Items.Add(headerItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(memoryItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon.ContextMenuStrip = menu;

        _logger?.LogInformation("System tray icon initialised");
    }

    public void ShowBalloon(string title, string message, int durationMs = 3000)
    {
        _notifyIcon?.ShowBalloonTip(durationMs, title, message, ToolTipIcon.None);
    }

    // ── Event handlers ────────────────────────────────────────────────────────
    private void OnIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowHideRequested?.Invoke();
    }

    // ── Icon creation (programmatic emerald orb) ──────────────────────────────
    private static Icon CreateFlowAIIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Dark circle background
        using var bgBrush = new SolidBrush(Color.FromArgb(220, 16, 18, 17));
        g.FillEllipse(bgBrush, 1, 1, size - 2, size - 2);

        // Emerald orb gradient
        var center = new PointF(size / 2f, size / 2f);
        using var glowBrush = new PathGradientBrush(CreateCirclePath(center, size / 2f - 3))
        {
            CenterColor = Color.FromArgb(255, 0, 255, 136),
            SurroundColors = new[] { Color.FromArgb(0, 0, 200, 100) }
        };
        g.FillEllipse(glowBrush, 3, 3, size - 6, size - 6);

        // Inner bright highlight
        using var innerBrush = new SolidBrush(Color.FromArgb(180, 255, 255, 255));
        g.FillEllipse(innerBrush, size / 2 - 4, size / 2 - 7, 8, 5);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateCirclePath(PointF center, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        return path;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    // ── Dark-themed menu renderer ─────────────────────────────────────────────
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color BgColor = Color.FromArgb(23, 25, 24);
        private static readonly Color FgColor = Color.FromArgb(220, 220, 220);
        private static readonly Color HoverColor = Color.FromArgb(0, 230, 100);
        private static readonly Color BorderColor = Color.FromArgb(40, 42, 40);

        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (item.Selected && item.Enabled)
            {
                using var brush = new SolidBrush(Color.FromArgb(40, HoverColor));
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, item.Size));
            }
            else
            {
                using var brush = new SolidBrush(BgColor);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, item.Size));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? FgColor : Color.FromArgb(100, FgColor);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(23, 25, 24);
        public override Color MenuItemSelected => Color.FromArgb(40, 0, 230, 100);
        public override Color MenuBorder => Color.FromArgb(40, 42, 40);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(40, 0, 230, 100);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(40, 0, 230, 100);
    }
}
