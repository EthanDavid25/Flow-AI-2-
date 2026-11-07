using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using FlowAI.Vision.Native;
using Microsoft.Extensions.Logging;

namespace FlowAI.Vision.Discovery;

public sealed class HybridElementLocator
{
    private readonly UIAutomationDiscoveryService uiaDiscovery;
    private readonly IScreenCaptureService screenCaptureService;
    private readonly IAIProvider aiProvider;
    private readonly ILogger<HybridElementLocator>? logger;

    public HybridElementLocator(
        UIAutomationDiscoveryService uiaDiscovery,
        IScreenCaptureService screenCaptureService,
        IAIProvider aiProvider,
        ILogger<HybridElementLocator>? logger = null)
    {
        this.uiaDiscovery = uiaDiscovery;
        this.screenCaptureService = screenCaptureService;
        this.aiProvider = aiProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Locates the requested UI element using the hybrid pipeline:
    /// UI Automation -> AI Vision -> Target validation.
    /// </summary>
    public async Task<DetectedTarget?> LocateTargetAsync(string userQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuery)) return null;

        // Stage 1: Try Windows UI Automation first (zero cost, deterministic, high accuracy)
        try
        {
            var uiaTarget = await uiaDiscovery.FindElementByQueryAsync(userQuery, cancellationToken);
            if (uiaTarget != null && uiaTarget.Confidence >= 0.75)
            {
                logger?.LogInformation("UI Automation found target '{Label}' with confidence {Confidence:F2}",
                    uiaTarget.Label, uiaTarget.Confidence);
                return uiaTarget;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning("UIA discovery stage threw: {Message}", ex.Message);
        }

        // Stage 2: Capture screen and invoke Vision AI model
        try
        {
            var capture = await screenCaptureService.CapturePrimaryScreenAsync(cancellationToken);
            var aiRequest = new AIRequest
            {
                Prompt = $"Find the UI element: '{userQuery}'. Respond with [POINT:x,y:{userQuery}:screen0] or [BOX:x,y,w,h:{userQuery}:screen0].",
                ScreenCaptures = new List<CapturedScreen> { capture }
            };

            var aiResponse = await aiProvider.SendChatAsync(aiRequest, cancellationToken);
            if (aiResponse.IsSuccess && aiResponse.PointTargets.Count > 0)
            {
                var target = aiResponse.PointTargets[0];
                target.Source = DetectionSource.VisionAI;
                logger?.LogInformation("Vision AI localized target at ({X}, {Y}) with label '{Label}'",
                    target.TargetPoint.X, target.TargetPoint.Y, target.Label);
                return target;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Vision AI target localization failed");
        }

        return null;
    }
}
