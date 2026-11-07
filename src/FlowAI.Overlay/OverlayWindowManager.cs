using System.Windows;
using FlowAI.Core.Enums;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlowAI.Overlay;

public sealed class OverlayWindowManager : IDisposable
{
    private readonly IScreenCaptureService screenCaptureService;
    private readonly ILogger<OverlayWindowManager>? logger;
    private readonly List<OverlayWindow> activeOverlays = new();
    private readonly object syncLock = new();

    public OverlayWindowManager(
        IScreenCaptureService screenCaptureService,
        ILogger<OverlayWindowManager>? logger = null)
    {
        this.screenCaptureService = screenCaptureService;
        this.logger = logger;
    }

    public void InitializeOverlays()
    {
        lock (syncLock)
        {
            CloseAll();

            var displays = screenCaptureService.GetDisplays();
            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                var rect = new Int32Rect(
                    (int)d.WorkArea.X,
                    (int)d.WorkArea.Y,
                    (int)d.WorkArea.Width,
                    (int)d.WorkArea.Height);

                var overlay = new OverlayWindow(rect, i);
                activeOverlays.Add(overlay);
                overlay.Show();
            }

            logger?.LogInformation("Initialized {Count} FlowAI transparent overlay window(s)", activeOverlays.Count);
        }
    }

    public void UpdateAudioLevel(float level)
    {
        lock (syncLock)
        {
            foreach (var overlay in activeOverlays)
            {
                overlay.SetAudioLevel(level);
            }
        }
    }

    public void UpdateState(FlowAIState state)
    {
        lock (syncLock)
        {
            foreach (var overlay in activeOverlays)
            {
                overlay.SetState(state);
            }
        }
    }

    public void FlyToTarget(DetectedTarget target)
    {
        lock (syncLock)
        {
            int index = Math.Clamp(target.ScreenIndex, 0, Math.Max(0, activeOverlays.Count - 1));
            if (activeOverlays.Count > index)
            {
                activeOverlays[index].FlyToTarget(target);
            }
        }
    }

    public void ShowResponse(string responseText, int targetScreenIndex = 0)
    {
        lock (syncLock)
        {
            int index = Math.Clamp(targetScreenIndex, 0, Math.Max(0, activeOverlays.Count - 1));
            if (activeOverlays.Count > index)
            {
                activeOverlays[index].ShowResponseText(responseText);
            }
        }
    }

    public void DismissAll()
    {
        lock (syncLock)
        {
            foreach (var overlay in activeOverlays)
            {
                overlay.DismissOverlayVisuals();
            }
        }
    }

    public void CloseAll()
    {
        lock (syncLock)
        {
            foreach (var overlay in activeOverlays)
            {
                try { overlay.Close(); } catch { }
            }
            activeOverlays.Clear();
        }
    }

    public void Dispose()
    {
        CloseAll();
    }
}
