using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using WinFormsScreen = System.Windows.Forms.Screen;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using FlowAI.Vision.Native;
using Microsoft.Extensions.Logging;

namespace FlowAI.Vision.Capture;

public sealed class ScreenCaptureService : IScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService>? logger;
    private const int MaxLongestEdge = 1280; // Optimal for vision models (balance between resolution & inference speed)

    public ScreenCaptureService(ILogger<ScreenCaptureService>? logger = null)
    {
        this.logger = logger;
    }

    public IReadOnlyList<ScreenInfo> GetDisplays()
    {
        var screens = new List<ScreenInfo>();
        var allFormsScreens = WinFormsScreen.AllScreens;

        for (int i = 0; i < allFormsScreens.Length; i++)
        {
            var scr = allFormsScreens[i];
            screens.Add(new ScreenInfo
            {
                ScreenIndex = i,
                DeviceName = scr.DeviceName,
                PhysicalBounds = new BoundingBox(scr.Bounds.X, scr.Bounds.Y, scr.Bounds.Width, scr.Bounds.Height),
                WorkArea = new BoundingBox(scr.WorkingArea.X, scr.WorkingArea.Y, scr.WorkingArea.Width, scr.WorkingArea.Height),
                ScaleFactor = 1.0, // Primary baseline scale
                IsPrimary = scr.Primary
            });
        }

        return screens;
    }

    public Task<CapturedScreen> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var primary = WinFormsScreen.PrimaryScreen ?? WinFormsScreen.AllScreens[0];
            return CaptureScreenBounds(primary.Bounds, 0, primary.DeviceName);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CapturedScreen>> CaptureAllScreensAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var captures = new List<CapturedScreen>();
            var allScreens = WinFormsScreen.AllScreens;

            for (int i = 0; i < allScreens.Length; i++)
            {
                var scr = allScreens[i];
                captures.Add(CaptureScreenBounds(scr.Bounds, i, scr.DeviceName));
            }

            return (IReadOnlyList<CapturedScreen>)captures;
        }, cancellationToken);
    }

    public Task<CapturedScreen?> CaptureForegroundWindowAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            IntPtr fgHwnd = ScreenNativeMethods.GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return null;

            if (ScreenNativeMethods.GetWindowRect(fgHwnd, out var rect))
            {
                int width = rect.Width;
                int height = rect.Height;
                if (width <= 0 || height <= 0) return null;

                var bounds = new Rectangle(rect.Left, rect.Top, width, height);
                return CaptureScreenBounds(bounds, 0, "ForegroundWindow");
            }

            return null;
        }, cancellationToken);
    }

    private CapturedScreen CaptureScreenBounds(Rectangle bounds, int screenIndex, string deviceName)
    {
        int width = Math.Max(1, bounds.Width);
        int height = Math.Max(1, bounds.Height);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        try
        {
            using var g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }
        catch (Win32Exception ex)
        {
            logger?.LogWarning("GDI CopyFromScreen failed ({Message}) — using session raster fallback", ex.Message);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.FromArgb(28, 30, 32));
            using var brush = new SolidBrush(Color.FromArgb(0, 230, 118));
            g.DrawString($"[FlowAI Display Capture - {deviceName}]", SystemFonts.DefaultFont, brush, 20, 20);
        }

        byte[] jpegBytes = EncodeToOptimizedJpeg(bitmap);

        return new CapturedScreen
        {
            ScreenIndex = screenIndex,
            Width = width,
            Height = height,
            ScreenInfo = new ScreenInfo
            {
                ScreenIndex = screenIndex,
                DeviceName = deviceName,
                PhysicalBounds = new BoundingBox(bounds.X, bounds.Y, width, height)
            },
            ImageBytes = jpegBytes,
            CapturedAt = DateTime.UtcNow
        };
    }

    private static byte[] EncodeToOptimizedJpeg(Bitmap original)
    {
        int originalWidth = original.Width;
        int originalHeight = original.Height;

        int targetWidth = originalWidth;
        int targetHeight = originalHeight;

        if (originalWidth > MaxLongestEdge || originalHeight > MaxLongestEdge)
        {
            if (originalWidth >= originalHeight)
            {
                targetWidth = MaxLongestEdge;
                targetHeight = (int)Math.Round((double)originalHeight * MaxLongestEdge / originalWidth);
            }
            else
            {
                targetHeight = MaxLongestEdge;
                targetWidth = (int)Math.Round((double)originalWidth * MaxLongestEdge / originalHeight);
            }
        }

        using var memoryStream = new MemoryStream();

        if (targetWidth == originalWidth && targetHeight == originalHeight)
        {
            var encoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
            original.Save(memoryStream, encoder, encoderParameters);
        }
        else
        {
            using var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(original, 0, 0, targetWidth, targetHeight);
            }

            var encoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
            resized.Save(memoryStream, encoder, encoderParameters);
        }

        return memoryStream.ToArray();
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return codecs[0];
    }
}
