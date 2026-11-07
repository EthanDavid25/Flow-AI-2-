namespace FlowAI.Core.Models;

public sealed class ScreenInfo
{
    public int ScreenIndex { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public BoundingBox PhysicalBounds { get; set; }
    public BoundingBox WorkArea { get; set; }
    public double ScaleFactor { get; set; } = 1.0;
    public bool IsPrimary { get; set; }
}

public sealed class CapturedScreen : IDisposable
{
    public int ScreenIndex { get; set; }
    public ScreenInfo ScreenInfo { get; set; } = new();
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public void Dispose()
    {
        ImageBytes = Array.Empty<byte>();
    }
}
