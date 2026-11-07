using System.Windows.Automation;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using FlowAI.Vision.Native;
using Microsoft.Extensions.Logging;

namespace FlowAI.Vision.Discovery;

public sealed class UIAutomationDiscoveryService : IElementDiscoveryService
{
    private readonly ILogger<UIAutomationDiscoveryService>? logger;

    public UIAutomationDiscoveryService(ILogger<UIAutomationDiscoveryService>? logger = null)
    {
        this.logger = logger;
    }

    public Task<IReadOnlyList<DetectedTarget>> DiscoverElementsInForegroundAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var results = new List<DetectedTarget>();
            IntPtr fgHwnd = ScreenNativeMethods.GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return (IReadOnlyList<DetectedTarget>)results;

            string windowTitle = ScreenNativeMethods.GetActiveWindowTitle(fgHwnd);
            string processName = ScreenNativeMethods.GetActiveProcessName(fgHwnd);

            try
            {
                var rootElement = AutomationElement.FromHandle(fgHwnd);
                if (rootElement == null) return results;

                var walker = TreeWalker.ControlViewWalker;
                WalkElementTree(rootElement, walker, query, windowTitle, processName, results, 0, 8, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("UIAutomation traversal failed: {Message}", ex.Message);
            }

            // Sort by confidence / relevance
            results.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return (IReadOnlyList<DetectedTarget>)results;
        }, cancellationToken);
    }

    public async Task<DetectedTarget?> FindElementByQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var candidates = await DiscoverElementsInForegroundAsync(query, cancellationToken);
        return candidates.FirstOrDefault();
    }

    public bool RevalidateTarget(DetectedTarget target)
    {
        if (target.BoundingBox.IsEmpty) return false;

        // Check if the foreground window still overlaps target
        IntPtr fgHwnd = ScreenNativeMethods.GetForegroundWindow();
        if (fgHwnd != IntPtr.Zero && ScreenNativeMethods.GetWindowRect(fgHwnd, out var windowRect))
        {
            var winBox = windowRect.ToBoundingBox();
            return winBox.Contains(target.TargetPoint);
        }

        return true;
    }

    private void WalkElementTree(
        AutomationElement element,
        TreeWalker walker,
        string query,
        string windowTitle,
        string processName,
        List<DetectedTarget> results,
        int currentDepth,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        if (currentDepth > maxDepth || cancellationToken.IsCancellationRequested) return;

        try
        {
            string name = element.Current.Name ?? string.Empty;
            string autoId = element.Current.AutomationId ?? string.Empty;
            string controlType = element.Current.ControlType?.ProgrammaticName ?? string.Empty;
            var rect = element.Current.BoundingRectangle;

            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
            {
                double score = CalculateMatchScore(query, name, autoId, controlType);
                if (score > 0.3)
                {
                    var box = new BoundingBox(rect.X, rect.Y, rect.Width, rect.Height);
                    results.Add(new DetectedTarget(box.Center.X, box.Center.Y, name.Length > 0 ? name : autoId, 0, box)
                    {
                        Source = DetectionSource.UIAutomation,
                        Confidence = score,
                        AutomationId = autoId,
                        ControlType = controlType,
                        WindowTitle = windowTitle,
                        ProcessName = processName
                    });
                }
            }
        }
        catch { }

        try
        {
            var child = walker.GetFirstChild(element);
            while (child != null && !cancellationToken.IsCancellationRequested)
            {
                WalkElementTree(child, walker, query, windowTitle, processName, results, currentDepth + 1, maxDepth, cancellationToken);
                child = walker.GetNextSibling(child);
            }
        }
        catch { }
    }

    private static double CalculateMatchScore(string query, string name, string autoId, string controlType)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0.5;

        string cleanQuery = query.Trim().ToLowerInvariant();
        string cleanName = name.ToLowerInvariant();
        string cleanId = autoId.ToLowerInvariant();

        // Exact match
        if (cleanName == cleanQuery) return 1.0;
        if (cleanId == cleanQuery) return 0.95;

        // Substring / Contains
        if (cleanName.Contains(cleanQuery)) return 0.85;
        if (cleanId.Contains(cleanQuery)) return 0.75;

        // Query contains name
        if (cleanName.Length > 3 && cleanQuery.Contains(cleanName)) return 0.8;

        // Common keyword matching
        var queryWords = cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int matchedWords = 0;
        foreach (var word in queryWords)
        {
            if (cleanName.Contains(word) || cleanId.Contains(word))
            {
                matchedWords++;
            }
        }

        if (matchedWords > 0 && queryWords.Length > 0)
        {
            return 0.5 + (0.4 * matchedWords / queryWords.Length);
        }

        return 0.0;
    }
}
