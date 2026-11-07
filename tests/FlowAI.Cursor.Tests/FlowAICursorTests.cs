using System.Windows;
using FlowAI.Cursor.Geometry;
using FlowAI.Cursor.Physics;
using Xunit;

namespace FlowAI.Cursor.Tests;

public class FlowAICursorTests
{
    [Fact]
    public void SpringDampedMotion_ConvergesTowardTarget()
    {
        var start = new Point(0, 0);
        var target = new Point(100, 100);
        var spring = new SpringDampedMotion(start);

        // Step through multiple frames (e.g. 1 second at 60fps)
        for (int i = 0; i < 60; i++)
        {
            spring.Update(target, 1.0 / 60.0);
        }

        // After 1 second, it should be within 0.5px of the target
        Assert.True(Math.Abs(spring.Position.X - target.X) < 0.5);
        Assert.True(Math.Abs(spring.Position.Y - target.Y) < 0.5);
    }

    [Fact]
    public void BezierFlightPath_StartsAndEndsAccurately()
    {
        var start = new Point(100, 100);
        var end = new Point(800, 600);
        var flight = new BezierFlightPath(start, end);

        // At t = 0
        var sampleStart = flight.Sample(0.0);
        Assert.Equal(start.X, sampleStart.Position.X, 2);
        Assert.Equal(start.Y, sampleStart.Position.Y, 2);

        // At t = duration
        var sampleEnd = flight.Sample(flight.DurationSeconds);
        Assert.Equal(end.X, sampleEnd.Position.X, 2);
        Assert.Equal(end.Y, sampleEnd.Position.Y, 2);

        // Mid-flight scale should be boosted (> 1.2)
        var sampleMid = flight.Sample(flight.DurationSeconds / 2.0);
        Assert.True(sampleMid.Scale > 1.25);
    }

    [Fact]
    public void EmeraldCursorGeometry_BuildsValidFigure()
    {
        var geom = EmeraldCursorGeometry.BuildCompanionGeometry(18.0);
        Assert.NotNull(geom);
        Assert.NotEmpty(geom.Figures);
        Assert.True(geom.Bounds.Width > 0);
        Assert.True(geom.Bounds.Height > 0);
    }
}
