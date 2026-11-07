using System.Windows;

namespace FlowAI.Cursor.Physics;

/// <summary>
/// Physics-based damped spring motion for smooth, jitter-free cursor tracking.
/// Matches SwiftUI's spring(response: 0.2, dampingFraction: 0.6).
/// </summary>
public sealed class SpringDampedMotion
{
    public double ResponseSeconds { get; set; } = 0.2;
    public double DampingFraction { get; set; } = 0.6;

    public Point Position { get; set; }
    public Point Velocity { get; set; }

    public SpringDampedMotion(Point initialPosition)
    {
        Position = initialPosition;
        Velocity = new Point(0, 0);
    }

    public void Update(Point targetPosition, double deltaSeconds)
    {
        if (deltaSeconds <= 0.0) return;
        // Clamp huge time steps (e.g. debugger pause or sleep) to prevent explosion
        deltaSeconds = Math.Min(deltaSeconds, 1.0 / 30.0);

        double angularFrequency = 2.0 * Math.PI / Math.Max(0.01, ResponseSeconds);

        double displacementX = Position.X - targetPosition.X;
        double displacementY = Position.Y - targetPosition.Y;

        double accelX = -angularFrequency * angularFrequency * displacementX
                        - 2.0 * DampingFraction * angularFrequency * Velocity.X;
        double accelY = -angularFrequency * angularFrequency * displacementY
                        - 2.0 * DampingFraction * angularFrequency * Velocity.Y;

        double newVx = Velocity.X + accelX * deltaSeconds;
        double newVy = Velocity.Y + accelY * deltaSeconds;

        Velocity = new Point(newVx, newVy);
        Position = new Point(Position.X + newVx * deltaSeconds, Position.Y + newVy * deltaSeconds);
    }

    public void SnapTo(Point newPosition)
    {
        Position = newPosition;
        Velocity = new Point(0, 0);
    }
}
