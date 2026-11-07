namespace FlowAI.Core.Models;

public enum DetectionSource
{
    PointProtocol,
    UIAutomation,
    OCR,
    VisionAI,
    LearnedWorkflow
}

public sealed class DetectedTarget
{
    public string Label { get; set; } = string.Empty;
    public CoordinatePoint TargetPoint { get; set; }
    public BoundingBox BoundingBox { get; set; }
    public int ScreenIndex { get; set; } = 0;
    public double Confidence { get; set; } = 1.0;
    public DetectionSource Source { get; set; } = DetectionSource.PointProtocol;
    public string? AutomationId { get; set; }
    public string? ControlType { get; set; }
    public string? WindowTitle { get; set; }
    public string? ProcessName { get; set; }

    public DetectedTarget() { }

    public DetectedTarget(double x, double y, string label, int screenIndex = 0, BoundingBox? box = null)
    {
        TargetPoint = new CoordinatePoint(x, y);
        Label = label;
        ScreenIndex = screenIndex;
        BoundingBox = box ?? new BoundingBox(x - 20, y - 15, 40, 30);
    }
}
