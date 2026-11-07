using FlowAI.Core.Models;

namespace FlowAI.Core.Geometry;

/// <summary>
/// Dedicated coordinate transformation layer.
/// Accurately maps coordinates across multi-monitor setups, DPI scaling factors, and image resolutions.
/// </summary>
public static class CoordinateTransform
{
    /// <summary>
    /// Converts physical pixels to Device Independent Pixels (DIPs) given a DPI scale factor.
    /// </summary>
    public static CoordinatePoint PhysicalPixelsToDips(CoordinatePoint physicalPixels, double scaleFactor)
    {
        if (scaleFactor <= 0.0) scaleFactor = 1.0;
        return new CoordinatePoint(physicalPixels.X / scaleFactor, physicalPixels.Y / scaleFactor);
    }

    /// <summary>
    /// Converts DIPs to physical pixels given a DPI scale factor.
    /// </summary>
    public static CoordinatePoint DipsToPhysicalPixels(CoordinatePoint dips, double scaleFactor)
    {
        if (scaleFactor <= 0.0) scaleFactor = 1.0;
        return new CoordinatePoint(dips.X * scaleFactor, dips.Y * scaleFactor);
    }

    /// <summary>
    /// Maps normalized image coordinates (0.0 to 1.0 or 0 to 1000 as used by vision models)
    /// into physical screen pixel coordinates on a target monitor.
    /// </summary>
    public static CoordinatePoint NormalizedToScreenPixels(
        double normalizedX,
        double normalizedY,
        ScreenInfo screen,
        bool isThousandScale = false)
    {
        double factor = isThousandScale ? 1000.0 : 1.0;
        double normX = Math.Clamp(normalizedX / factor, 0.0, 1.0);
        double normY = Math.Clamp(normalizedY / factor, 0.0, 1.0);

        double screenX = screen.PhysicalBounds.X + (normX * screen.PhysicalBounds.Width);
        double screenY = screen.PhysicalBounds.Y + (normY * screen.PhysicalBounds.Height);

        return new CoordinatePoint(screenX, screenY);
    }

    /// <summary>
    /// Converts a normalized bounding box into a physical screen pixel bounding box.
    /// </summary>
    public static BoundingBox NormalizedToScreenBox(
        double normX,
        double normY,
        double normW,
        double normH,
        ScreenInfo screen,
        bool isThousandScale = false)
    {
        double factor = isThousandScale ? 1000.0 : 1.0;
        double nx = Math.Clamp(normX / factor, 0.0, 1.0);
        double ny = Math.Clamp(normY / factor, 0.0, 1.0);
        double nw = Math.Clamp(normW / factor, 0.0, 1.0);
        double nh = Math.Clamp(normH / factor, 0.0, 1.0);

        double screenX = screen.PhysicalBounds.X + (nx * screen.PhysicalBounds.Width);
        double screenY = screen.PhysicalBounds.Y + (ny * screen.PhysicalBounds.Height);
        double screenW = nw * screen.PhysicalBounds.Width;
        double screenH = nh * screen.PhysicalBounds.Height;

        return new BoundingBox(screenX, screenY, screenW, screenH);
    }

    /// <summary>
    /// Clamps a coordinate point to stay within screen bounds.
    /// </summary>
    public static CoordinatePoint ClampToScreen(CoordinatePoint point, ScreenInfo screen)
    {
        double clampedX = Math.Clamp(point.X, screen.PhysicalBounds.X, screen.PhysicalBounds.X + screen.PhysicalBounds.Width - 1);
        double clampedY = Math.Clamp(point.Y, screen.PhysicalBounds.Y, screen.PhysicalBounds.Y + screen.PhysicalBounds.Height - 1);
        return new CoordinatePoint(clampedX, clampedY);
    }
}
