using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using FlowAI.Core.Enums;
using FlowAI.Core.Models;
using FlowAI.Cursor.Geometry;
using FlowAI.Cursor.Physics;
using FlowAI.Cursor.Styles;
using FlowAI.Overlay.Native;

namespace FlowAI.Overlay;

public enum CompanionMode
{
    FollowingCursor,
    NavigatingToTarget,
    HighlightingTarget
}

public class OverlayWindow : Window
{
    private readonly Int32Rect screenBoundsPixels;
    private readonly int screenIndex;

    private readonly Canvas rootCanvas;
    private readonly System.Windows.Shapes.Path companionCursor;
    private readonly RotateTransform cursorRotation;
    private readonly ScaleTransform cursorScale;
    private readonly DropShadowEffect cursorGlow;

    // Visual indicators
    private readonly Canvas waveformCanvas;
    private readonly System.Windows.Shapes.Rectangle[] waveformBars;
    private readonly Canvas thinkingSpinnerCanvas;
    private readonly RotateTransform spinnerRotation;

    // Target highlight elements
    private readonly Border targetBoundingBox;
    private readonly Border targetLabelBadge;
    private readonly TextBlock targetLabelText;
    private readonly System.Windows.Shapes.Path arrivalPulseRing;
    private readonly ScaleTransform arrivalPulseScale;

    // Bubbles
    private readonly Border responseBubble;
    private readonly TextBlock responseBubbleText;
    private readonly DispatcherTimer responseHideTimer;

    // Physics & State
    private readonly SpringDampedMotion springMotion;
    private BezierFlightPath? activeFlight;
    private double flightElapsedSeconds;
    private CompanionMode mode = CompanionMode.FollowingCursor;
    private FlowAIState currentState = FlowAIState.Idle;

    private static readonly Vector CursorTrackingOffset = new(32, 22);
    private TimeSpan lastRenderTime = TimeSpan.Zero;
    private float currentAudioLevel = 0f;
    private double spinnerAngle = 0;
    private double arrivalPulseRadius = 1.0;
    private double arrivalPulseOpacity = 0.0;

    public OverlayWindow(Int32Rect bounds, int screenIndex)
    {
        this.screenBoundsPixels = bounds;
        this.screenIndex = screenIndex;

        Title = $"FlowAI Overlay Screen {screenIndex}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        rootCanvas = new Canvas { IsHitTestVisible = false };
        Content = rootCanvas;

        // ── 1. Target Bounding Box ───────────────────────────────────────
        targetBoundingBox = new Border
        {
            BorderBrush = FlowAIDesignTokens.Brushes.EmeraldNeon,
            BorderThickness = new Thickness(2.0),
            CornerRadius = new CornerRadius(6.0),
            Background = new SolidColorBrush(Color.FromArgb(28, 0, 255, 136)),
            Opacity = 0,
            Effect = new DropShadowEffect
            {
                Color = FlowAIDesignTokens.Colors.EmeraldNeon,
                ShadowDepth = 0,
                BlurRadius = 14,
                Opacity = 0.8
            }
        };
        rootCanvas.Children.Add(targetBoundingBox);

        // Target Label Badge
        targetLabelText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FlowAIDesignTokens.Brushes.TextOnAccent,
            FontFamily = new FontFamily("Segoe UI")
        };
        targetLabelBadge = new Border
        {
            Background = FlowAIDesignTokens.Brushes.EmeraldNeon,
            CornerRadius = new CornerRadius(4.0),
            Padding = new Thickness(6, 2, 6, 2),
            Opacity = 0,
            Child = targetLabelText,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0),
                ShadowDepth = 1,
                BlurRadius = 4,
                Opacity = 0.5
            }
        };
        rootCanvas.Children.Add(targetLabelBadge);

        // Arrival Pulse Ring
        arrivalPulseScale = new ScaleTransform(1, 1);
        arrivalPulseRing = new System.Windows.Shapes.Path
        {
            Data = EmeraldCursorGeometry.BuildPulseRingGeometry(24),
            Stroke = FlowAIDesignTokens.Brushes.EmeraldNeon,
            StrokeThickness = 2.0,
            Opacity = 0,
            RenderTransform = arrivalPulseScale
        };
        rootCanvas.Children.Add(arrivalPulseRing);

        // ── 2. Emerald Cursor Companion ──────────────────────────────────
        cursorRotation = new RotateTransform(-35, 9, 11);
        cursorScale = new ScaleTransform(1, 1, 9, 11);
        cursorGlow = new DropShadowEffect
        {
            Color = FlowAIDesignTokens.Colors.EmeraldNeon,
            ShadowDepth = 0,
            BlurRadius = 10,
            Opacity = 0.95
        };
        companionCursor = new System.Windows.Shapes.Path
        {
            Data = EmeraldCursorGeometry.BuildCompanionGeometry(18),
            Fill = FlowAIDesignTokens.Brushes.EmeraldPrimary,
            Stroke = FlowAIDesignTokens.Brushes.CursorOutline,
            StrokeThickness = 1.5,
            Effect = cursorGlow,
            RenderTransform = new TransformGroup { Children = { cursorScale, cursorRotation } }
        };
        rootCanvas.Children.Add(companionCursor);

        // ── 3. Audio-reactive Waveform (Listening state) ─────────────────
        waveformCanvas = new Canvas { Width = 22, Height = 18, Opacity = 0 };
        waveformBars = new System.Windows.Shapes.Rectangle[5];
        double[] barHeightWeights = { 0.4, 0.75, 1.0, 0.75, 0.4 };
        for (int i = 0; i < 5; i++)
        {
            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = 2.5,
                Height = 4,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = FlowAIDesignTokens.Brushes.EmeraldNeon,
                Tag = barHeightWeights[i]
            };
            Canvas.SetLeft(bar, i * 4.5);
            waveformBars[i] = bar;
            waveformCanvas.Children.Add(bar);
        }
        rootCanvas.Children.Add(waveformCanvas);

        // ── 4. Thinking / Scanner Radar Visual ───────────────────────────
        spinnerRotation = new RotateTransform(0, 10, 10);
        thinkingSpinnerCanvas = BuildSpinnerVisual();
        thinkingSpinnerCanvas.RenderTransform = spinnerRotation;
        thinkingSpinnerCanvas.Opacity = 0;
        rootCanvas.Children.Add(thinkingSpinnerCanvas);

        // ── 5. Response Speech Bubble ────────────────────────────────────
        (responseBubble, responseBubbleText) = BuildResponseBubble();
        rootCanvas.Children.Add(responseBubble);

        responseHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        responseHideTimer.Tick += (s, e) =>
        {
            responseHideTimer.Stop();
            AnimateOpacity(responseBubble, 0.0, 0.4);
        };

        springMotion = new SpringDampedMotion(new Point(200, 200));

        SourceInitialized += HandleSourceInitialized;
        Loaded += HandleLoaded;
        Closed += HandleClosed;
    }

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        IntPtr currentEx = OverlayNativeMethods.GetWindowLongPtr(hwnd, OverlayNativeMethods.GWL_EXSTYLE);
        OverlayNativeMethods.SetWindowLongPtr(
            hwnd,
            OverlayNativeMethods.GWL_EXSTYLE,
            new IntPtr(currentEx.ToInt64()
                | OverlayNativeMethods.WS_EX_TRANSPARENT
                | OverlayNativeMethods.WS_EX_NOACTIVATE
                | OverlayNativeMethods.WS_EX_TOOLWINDOW
                | OverlayNativeMethods.WS_EX_LAYERED));

        // Screen capture exclusion so FlowAI never sees itself in screenshots
        OverlayNativeMethods.SetWindowDisplayAffinity(hwnd, OverlayNativeMethods.WDA_EXCLUDEFROMCAPTURE);

        // Position window covering the screen exactly in physical coordinates
        OverlayNativeMethods.SetWindowPos(
            hwnd,
            OverlayNativeMethods.HWND_TOPMOST,
            screenBoundsPixels.X,
            screenBoundsPixels.Y,
            screenBoundsPixels.Width,
            screenBoundsPixels.Height,
            OverlayNativeMethods.SWP_NOACTIVATE | OverlayNativeMethods.SWP_SHOWWINDOW);
    }

    private void HandleLoaded(object? sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += HandleRendering;
    }

    private void HandleClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= HandleRendering;
    }

    public void SetAudioLevel(float level)
    {
        currentAudioLevel = Math.Clamp(level, 0f, 1f);
    }

    public void SetState(FlowAIState state)
    {
        currentState = state;
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case FlowAIState.Listening:
                    AnimateOpacity(waveformCanvas, 1.0, 0.2);
                    AnimateOpacity(thinkingSpinnerCanvas, 0.0, 0.2);
                    break;
                case FlowAIState.Thinking:
                case FlowAIState.AnalysingScreen:
                case FlowAIState.FindingElement:
                    AnimateOpacity(waveformCanvas, 0.0, 0.2);
                    AnimateOpacity(thinkingSpinnerCanvas, 1.0, 0.2);
                    break;
                default:
                    AnimateOpacity(waveformCanvas, 0.0, 0.2);
                    AnimateOpacity(thinkingSpinnerCanvas, 0.0, 0.2);
                    break;
            }
        });
    }

    public void FlyToTarget(DetectedTarget target)
    {
        Dispatcher.Invoke(() =>
        {
            var localDipsTarget = GlobalPixelsToLocalDips(new Point(target.TargetPoint.X, target.TargetPoint.Y));
            activeFlight = new BezierFlightPath(springMotion.Position, localDipsTarget);
            flightElapsedSeconds = 0;
            mode = CompanionMode.NavigatingToTarget;

            // Prepare bounding box
            if (!target.BoundingBox.IsEmpty)
            {
                var boxTopLeft = GlobalPixelsToLocalDips(new Point(target.BoundingBox.X, target.BoundingBox.Y));
                Canvas.SetLeft(targetBoundingBox, boxTopLeft.X);
                Canvas.SetTop(targetBoundingBox, boxTopLeft.Y);
                targetBoundingBox.Width = Math.Max(20, target.BoundingBox.Width);
                targetBoundingBox.Height = Math.Max(20, target.BoundingBox.Height);

                targetLabelText.Text = target.Label;
                Canvas.SetLeft(targetLabelBadge, boxTopLeft.X);
                Canvas.SetTop(targetLabelBadge, Math.Max(0, boxTopLeft.Y - 24));
            }
        });
    }

    public void ShowResponseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Dispatcher.Invoke(() =>
        {
            responseBubbleText.Text = text;
            Canvas.SetLeft(responseBubble, Math.Clamp(springMotion.Position.X + 25, 20, Math.Max(20, ActualWidth - 360)));
            Canvas.SetTop(responseBubble, Math.Clamp(springMotion.Position.Y + 25, 20, Math.Max(20, ActualHeight - 150)));

            AnimateOpacity(responseBubble, 1.0, 0.25);
            responseHideTimer.Stop();
            responseHideTimer.Start();
        });
    }

    public void DismissOverlayVisuals()
    {
        Dispatcher.Invoke(() =>
        {
            AnimateOpacity(targetBoundingBox, 0.0, 0.3);
            AnimateOpacity(targetLabelBadge, 0.0, 0.3);
            AnimateOpacity(responseBubble, 0.0, 0.3);
            mode = CompanionMode.FollowingCursor;
        });
    }

    private void HandleRendering(object? sender, EventArgs e)
    {
        var renderingArgs = (RenderingEventArgs)e;
        if (lastRenderTime == TimeSpan.Zero)
        {
            lastRenderTime = renderingArgs.RenderingTime;
            return;
        }

        double dt = (renderingArgs.RenderingTime - lastRenderTime).TotalSeconds;
        lastRenderTime = renderingArgs.RenderingTime;
        if (dt <= 0.0 || dt > 0.1) return;

        // 1. Navigation / Flight physics
        if (mode == CompanionMode.NavigatingToTarget && activeFlight != null)
        {
            flightElapsedSeconds += dt;
            var sample = activeFlight.Sample(flightElapsedSeconds);
            springMotion.Position = sample.Position;
            cursorRotation.Angle = sample.RotationDegrees;
            cursorScale.ScaleX = sample.Scale;
            cursorScale.ScaleY = sample.Scale;

            if (activeFlight.IsComplete(flightElapsedSeconds))
            {
                mode = CompanionMode.HighlightingTarget;
                activeFlight = null;
                // Trigger arrival pulse and reveal bounding box
                AnimateOpacity(targetBoundingBox, 1.0, 0.2);
                AnimateOpacity(targetLabelBadge, 1.0, 0.2);
                TriggerArrivalPulse(springMotion.Position);
            }
        }
        else if (mode == CompanionMode.FollowingCursor)
        {
            var cursorDips = GetCursorPositionInLocalDips();
            if (cursorDips.HasValue)
            {
                var target = cursorDips.Value + CursorTrackingOffset;
                springMotion.Update(target, dt);
                // Return rotation and scale to default
                cursorRotation.Angle += (-35.0 - cursorRotation.Angle) * Math.Min(1.0, dt * 8.0);
                cursorScale.ScaleX += (1.0 - cursorScale.ScaleX) * Math.Min(1.0, dt * 8.0);
                cursorScale.ScaleY += (1.0 - cursorScale.ScaleY) * Math.Min(1.0, dt * 8.0);
            }
        }

        // Apply companion position
        Canvas.SetLeft(companionCursor, springMotion.Position.X);
        Canvas.SetTop(companionCursor, springMotion.Position.Y);

        // Anchor waveform and spinner next to companion
        Canvas.SetLeft(waveformCanvas, springMotion.Position.X + 24);
        Canvas.SetTop(waveformCanvas, springMotion.Position.Y + 2);

        Canvas.SetLeft(thinkingSpinnerCanvas, springMotion.Position.X + 24);
        Canvas.SetTop(thinkingSpinnerCanvas, springMotion.Position.Y + 2);

        // Update waveform bars
        if (waveformCanvas.Opacity > 0.05)
        {
            for (int i = 0; i < 5; i++)
            {
                double weight = (double)waveformBars[i].Tag;
                double h = 3.0 + (currentAudioLevel * 14.0 * weight);
                waveformBars[i].Height = Math.Max(2.0, h);
            }
        }

        // Update thinking spinner rotation
        if (thinkingSpinnerCanvas.Opacity > 0.05)
        {
            spinnerAngle = (spinnerAngle + (dt * 360.0)) % 360.0;
            spinnerRotation.Angle = spinnerAngle;
        }

        // Update arrival pulse
        if (arrivalPulseOpacity > 0.01)
        {
            arrivalPulseRadius += dt * 45.0;
            arrivalPulseOpacity = Math.Max(0.0, arrivalPulseOpacity - dt * 2.5);
            arrivalPulseScale.ScaleX = arrivalPulseRadius / 24.0;
            arrivalPulseScale.ScaleY = arrivalPulseRadius / 24.0;
            arrivalPulseRing.Opacity = arrivalPulseOpacity;
        }
    }

    private void TriggerArrivalPulse(Point position)
    {
        Canvas.SetLeft(arrivalPulseRing, position.X);
        Canvas.SetTop(arrivalPulseRing, position.Y);
        arrivalPulseRadius = 1.0;
        arrivalPulseOpacity = 1.0;
    }

    private Point? GetCursorPositionInLocalDips()
    {
        OverlayNativeMethods.GetCursorPos(out var pt);
        if (screenBoundsPixels.X <= pt.X && pt.X < (screenBoundsPixels.X + screenBoundsPixels.Width) &&
            screenBoundsPixels.Y <= pt.Y && pt.Y < (screenBoundsPixels.Y + screenBoundsPixels.Height))
        {
            return GlobalPixelsToLocalDips(new Point(pt.X, pt.Y));
        }
        return null;
    }

    private Point GlobalPixelsToLocalDips(Point globalPixels)
    {
        double localX = globalPixels.X - screenBoundsPixels.X;
        double localY = globalPixels.Y - screenBoundsPixels.Y;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(new Point(localX, localY));
        }
        return new Point(localX, localY);
    }

    private static (Border bubble, TextBlock text) BuildResponseBubble()
    {
        var textBlock = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = FlowAIDesignTokens.Brushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            MaxWidth = 340,
            FontFamily = new FontFamily("Segoe UI")
        };

        var bubble = new Border
        {
            Background = FlowAIDesignTokens.Brushes.Surface2,
            BorderBrush = FlowAIDesignTokens.Brushes.EmeraldPrimary,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Opacity = 0,
            Child = textBlock,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0),
                ShadowDepth = 4,
                BlurRadius = 16,
                Opacity = 0.65
            }
        };

        return (bubble, textBlock);
    }

    private static Canvas BuildSpinnerVisual()
    {
        var canvas = new Canvas { Width = 20, Height = 20 };
        var outerRing = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 2 10 A 8 8 0 0 1 18 10"),
            Stroke = FlowAIDesignTokens.Brushes.EmeraldNeon,
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        canvas.Children.Add(outerRing);
        return canvas;
    }

    private static void AnimateOpacity(UIElement element, double toValue, double durationSeconds)
    {
        var anim = new DoubleAnimation(toValue, TimeSpan.FromSeconds(durationSeconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}
