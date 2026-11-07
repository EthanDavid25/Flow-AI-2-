using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlowAI.Core.Models;

namespace FlowAI.Vision.Native;

public static class ScreenNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public BoundingBox ToBoundingBox() => new(Left, Top, Width, Height);
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    public static string GetActiveWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        if (GetWindowText(hWnd, sb, sb.Capacity) > 0)
        {
            return sb.ToString();
        }
        return string.Empty;
    }

    public static string GetActiveProcessName(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId > 0)
            {
                using var proc = Process.GetProcessById((int)processId);
                return proc.ProcessName;
            }
        }
        catch { }
        return string.Empty;
    }
}
