using System.Windows;

namespace FlowAI.Cursor.Physics;

/// <summary>
/// Quadratic Bézier flight math with raised arc, smoothstep easing,
/// curve tangent rotation, and mid-flight scale pulsing.
/// </summary>
public sealed class BezierFlightPath
{
    public Point StartPosition { get; }
    public Point ControlPoint { get; }
    public Point EndPosition { get; }
    public double DurationSeconds { get; }

    public BezierFlightPath(Point start, Point end)
    {
        StartPosition = start;
        EndPosition = end;

        double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        DurationSeconds = Math.Clamp(distance / 800.0, 0.6, 1.4);

        // Raise midpoint perpendicular to motion for an elegant arc
        double midX = (start.X + end.X) / 2.0;
        double midY = (start.Y + end.Y) / 2.0;
        double arcElevation = Math.Min(distance * 0.25, 90.0);

        // Displace control point upward
        ControlPoint = new Point(midX, midY - arcElevation);
    }

    /// <summary>
    /// Computes the companion position, tangent rotation angle, and scale factor at a given progress (0.0 to 1.0).
    /// </summary>
    public (Point Position, double RotationDegrees, double Scale) Sample(double elapsedSeconds)
    {
        double linearProgress = Math.Clamp(elapsedSeconds / DurationSeconds, 0.0, 1.0);

        // Smoothstep easeInOut: 3t² - 2t³
        double t = linearProgress * linearProgress * (3.0 - 2.0 * linearProgress);
        double oneMinusT = 1.0 - t;

        // Quadratic Bézier: B(t) = (1-t)²·P0 + 2(1-t)t·P1 + t²·P2
        double x = (oneMinusT * oneMinusT * StartPosition.X)
                   + (2.0 * oneMinusT * t * ControlPoint.X)
                   + (t * t * EndPosition.X);

        double y = (oneMinusT * oneMinusT * StartPosition.Y)
                   + (2.0 * oneMinusT * t * ControlPoint.Y)
                   + (t * t * EndPosition.Y);

        // Tangent vector: B'(t) = 2(1-t)(P1 - P0) + 2t(P2 - P1)
        double tangentX = 2.0 * oneMinusT * (ControlPoint.X - StartPosition.X)
                          + 2.0 * t * (EndPosition.X - ControlPoint.X);
        double tangentY = 2.0 * oneMinusT * (ControlPoint.Y - StartPosition.Y)
                          + 2.0 * t * (EndPosition.Y - ControlPoint.Y);

        double angle = Math.Atan2(tangentY, tangentX) * 180.0 / Math.PI;

        // Mid-flight scale pulse (up to 1.35x around progress = 0.5)
        double scale = 1.0 + 0.35 * Math.Sin(Math.PI * linearProgress);

        return (new Point(x, y), angle, scale);
    }

    public bool IsComplete(double elapsedSeconds) => elapsedSeconds >= DurationSeconds;
}
