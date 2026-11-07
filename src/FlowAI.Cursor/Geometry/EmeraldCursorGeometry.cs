using System.Windows;
using System.Windows.Media;

namespace FlowAI.Cursor.Geometry;

public static class EmeraldCursorGeometry
{
    /// <summary>
    /// Builds an original, geometric, polished emerald companion cursor.
    /// Uses an arrowhead with a rounded inward notch, optimized for visibility at small sizes.
    /// </summary>
    public static PathGeometry BuildCompanionGeometry(double size = 18.0)
    {
        // Dimensions normalized to size
        // (0, 0) is top-left tip
        double width = size;
        double height = size * 1.25;

        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        // Down right edge to right wing
        figure.Segments.Add(new LineSegment(new Point(width, height * 0.75), true));
        // Inward notch
        figure.Segments.Add(new LineSegment(new Point(width * 0.55, height * 0.70), true));
        // Tail
        figure.Segments.Add(new LineSegment(new Point(width * 0.35, height), true));
        // Back to tip
        figure.Segments.Add(new LineSegment(new Point(0, 0), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Builds a subtle target ring / arrival pulse circle geometry.
    /// </summary>
    public static EllipseGeometry BuildPulseRingGeometry(double radius)
    {
        var geom = new EllipseGeometry(new Point(0, 0), radius, radius);
        geom.Freeze();
        return geom;
    }
}
