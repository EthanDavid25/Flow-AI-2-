namespace FlowAI.Core.Models;

public readonly record struct CoordinatePoint(double X, double Y)
{
    public static CoordinatePoint Zero => new(0, 0);

    public double DistanceTo(CoordinatePoint other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public readonly record struct BoundingBox(double X, double Y, double Width, double Height)
{
    public static BoundingBox Empty => new(0, 0, 0, 0);

    public CoordinatePoint Center => new(X + Width / 2.0, Y + Height / 2.0);

    public bool Contains(CoordinatePoint point)
    {
        return point.X >= X && point.X <= (X + Width) &&
               point.Y >= Y && point.Y <= (Y + Height);
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
