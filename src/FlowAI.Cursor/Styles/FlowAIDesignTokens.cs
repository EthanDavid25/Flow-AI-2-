using System.Windows.Media;

namespace FlowAI.Cursor.Styles;

public static class FlowAIDesignTokens
{
    public static class Colors
    {
        // ── FlowAI Emerald Green Palette ─────────────────────────────────
        public static readonly Color EmeraldNeon = Color.FromRgb(0, 255, 136);       // #00FF88
        public static readonly Color EmeraldPrimary = Color.FromRgb(0, 230, 118);    // #00E676
        public static readonly Color EmeraldAccent = Color.FromRgb(16, 185, 129);     // #10B981
        public static readonly Color EmeraldDark = Color.FromRgb(5, 46, 22);         // #052E16
        public static readonly Color EmeraldGlow = Color.FromArgb(200, 0, 255, 136);

        // ── Dark Surfaces ────────────────────────────────────────────────
        public static readonly Color Background = Color.FromRgb(16, 18, 17);         // #101211
        public static readonly Color Surface1 = Color.FromRgb(23, 25, 24);           // #171918
        public static readonly Color Surface2 = Color.FromRgb(32, 34, 33);           // #202221
        public static readonly Color Surface3 = Color.FromRgb(39, 42, 41);           // #272A29
        public static readonly Color Surface4 = Color.FromRgb(46, 49, 48);           // #2E3130

        // ── Borders & Outlines ───────────────────────────────────────────
        public static readonly Color BorderSubtle = Color.FromRgb(55, 59, 57);
        public static readonly Color BorderStrong = Color.FromRgb(68, 73, 71);
        public static readonly Color CursorOutline = Color.FromArgb(220, 10, 15, 12); // Contrasting dark outline

        // ── Typography ───────────────────────────────────────────────────
        public static readonly Color TextPrimary = Color.FromRgb(236, 238, 237);
        public static readonly Color TextSecondary = Color.FromRgb(173, 181, 178);
        public static readonly Color TextMuted = Color.FromRgb(107, 115, 111);
        public static readonly Color TextOnAccent = Color.FromRgb(10, 15, 12);

        // ── Semantics ────────────────────────────────────────────────────
        public static readonly Color Error = Color.FromRgb(239, 68, 68);
        public static readonly Color Warning = Color.FromRgb(245, 158, 11);
        public static readonly Color Info = Color.FromRgb(59, 130, 246);
    }

    public static class Brushes
    {
        public static readonly SolidColorBrush EmeraldNeon = Freeze(new SolidColorBrush(Colors.EmeraldNeon));
        public static readonly SolidColorBrush EmeraldPrimary = Freeze(new SolidColorBrush(Colors.EmeraldPrimary));
        public static readonly SolidColorBrush EmeraldAccent = Freeze(new SolidColorBrush(Colors.EmeraldAccent));
        public static readonly SolidColorBrush EmeraldDark = Freeze(new SolidColorBrush(Colors.EmeraldDark));
        public static readonly SolidColorBrush Background = Freeze(new SolidColorBrush(Colors.Background));
        public static readonly SolidColorBrush Surface1 = Freeze(new SolidColorBrush(Colors.Surface1));
        public static readonly SolidColorBrush Surface2 = Freeze(new SolidColorBrush(Colors.Surface2));
        public static readonly SolidColorBrush Surface3 = Freeze(new SolidColorBrush(Colors.Surface3));
        public static readonly SolidColorBrush Surface4 = Freeze(new SolidColorBrush(Colors.Surface4));
        public static readonly SolidColorBrush BorderSubtle = Freeze(new SolidColorBrush(Colors.BorderSubtle));
        public static readonly SolidColorBrush BorderStrong = Freeze(new SolidColorBrush(Colors.BorderStrong));
        public static readonly SolidColorBrush CursorOutline = Freeze(new SolidColorBrush(Colors.CursorOutline));
        public static readonly SolidColorBrush TextPrimary = Freeze(new SolidColorBrush(Colors.TextPrimary));
        public static readonly SolidColorBrush TextSecondary = Freeze(new SolidColorBrush(Colors.TextSecondary));
        public static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Colors.TextMuted));
        public static readonly SolidColorBrush TextOnAccent = Freeze(new SolidColorBrush(Colors.TextOnAccent));
        public static readonly SolidColorBrush Error = Freeze(new SolidColorBrush(Colors.Error));
        public static readonly SolidColorBrush Warning = Freeze(new SolidColorBrush(Colors.Warning));

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
