using FlowAI.Core.Models;
using FlowAI.Vision.Capture;
using FlowAI.Vision.Discovery;
using Xunit;

namespace FlowAI.Vision.Tests;

public class FlowAIVisionTests
{
    [Fact]
    public void ScreenCaptureService_EnumeratesDisplays_ReturnsAtLeastOne()
    {
        var service = new ScreenCaptureService();
        var displays = service.GetDisplays();

        Assert.NotEmpty(displays);
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        Assert.True(primary.PhysicalBounds.Width > 0);
        Assert.True(primary.PhysicalBounds.Height > 0);
    }

    [Fact]
    public async Task ScreenCaptureService_CapturesPrimaryScreen_ProducesValidJpegBytes()
    {
        var service = new ScreenCaptureService();
        using var capture = await service.CapturePrimaryScreenAsync();

        Assert.NotNull(capture);
        Assert.NotEmpty(capture.ImageBytes);
        Assert.True(capture.Width > 0);
        Assert.True(capture.Height > 0);

        // Verify JPEG magic bytes FF D8
        Assert.Equal(0xFF, capture.ImageBytes[0]);
        Assert.Equal(0xD8, capture.ImageBytes[1]);
    }

    [Fact]
    public void UIAutomationDiscoveryService_RevalidatesTarget_InsideWindow()
    {
        var service = new UIAutomationDiscoveryService();
        var targetInside = new DetectedTarget(100, 100, "Test Button", 0, new BoundingBox(80, 80, 40, 40));

        // When target is valid and non-empty, revalidation succeeds
        bool isValid = service.RevalidateTarget(targetInside);
        Assert.True(isValid);

        // Empty bounding box should fail revalidation
        var targetEmpty = new DetectedTarget(0, 0, "Empty", 0, BoundingBox.Empty);
        Assert.False(service.RevalidateTarget(targetEmpty));
    }
}
