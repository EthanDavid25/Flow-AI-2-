using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace FlowAI.App.Hotkey;

/// <summary>
/// Installs a WH_KEYBOARD_LL low-level keyboard hook on a dedicated STA thread
/// with its own message pump. Never installed on the UI thread.
///
/// Monitors Ctrl + Alt + Space for push-to-talk (hold = recording, release = process).
/// Monitors Escape to cancel any active operation.
/// </summary>
public sealed class GlobalHotkeyMonitor : IDisposable
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;    // Alt
    private const int VK_SPACE = 0x20;
    private const int VK_ESCAPE = 0x1B;
    private const short KEY_DOWN_BIT = unchecked((short)0x8000);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ── State ────────────────────────────────────────────────────────────────
    private readonly ILogger<GlobalHotkeyMonitor>? _logger;
    private Thread? _hookThread;
    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;  // keep delegate alive (GC safety)
    private volatile bool _disposed;
    private volatile bool _hotkeyHeld;

    // ── Events ───────────────────────────────────────────────────────────────
    /// <summary>Raised on the hook thread when Ctrl+Alt+Space is pressed (start recording).</summary>
    public event Action? HotkeyPressed;

    /// <summary>Raised on the hook thread when Ctrl+Alt+Space is released (stop recording, begin processing).</summary>
    public event Action? HotkeyReleased;

    /// <summary>Raised on the hook thread when Escape is pressed.</summary>
    public event Action? EscapePressed;

    public GlobalHotkeyMonitor(ILogger<GlobalHotkeyMonitor>? logger = null)
    {
        _logger = logger;
    }

    // ── Public API ───────────────────────────────────────────────────────────
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlobalHotkeyMonitor));
        if (_hookThread != null) return;

        _hookThread = new Thread(HookThreadProc)
        {
            Name = "FlowAI-HookThread",
            IsBackground = true
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        _logger?.LogInformation("GlobalHotkeyMonitor started (Ctrl+Alt+Space / Escape)");
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        // The GetMessage loop will exit when we post WM_QUIT
        // The thread is background so it will not block process exit
    }

    // ── Hook thread ─────────────────────────────────────────────────────────
    private void HookThreadProc()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        IntPtr hMod = GetModuleHandle(module.ModuleName);

        _hookProc = KeyboardHookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            _logger?.LogError("Failed to install low-level keyboard hook (error {Error})", Marshal.GetLastWin32Error());
            return;
        }

        _logger?.LogDebug("Low-level keyboard hook installed (handle {Handle})", _hookHandle);

        // Message pump — required for WH_KEYBOARD_LL to receive callbacks
        while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)kb.vkCode;
            bool isKeyDown = (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN);
            bool isKeyUp = (wParam == WM_KEYUP || wParam == WM_SYSKEYUP);

            if (isKeyDown && vk == VK_ESCAPE)
            {
                SafeRaise(EscapePressed, nameof(EscapePressed));
            }
            else if (vk == VK_SPACE)
            {
                bool ctrl = (GetAsyncKeyState(VK_CONTROL) & KEY_DOWN_BIT) != 0;
                bool alt = (GetAsyncKeyState(VK_MENU) & KEY_DOWN_BIT) != 0;

                if (ctrl && alt)
                {
                    if (isKeyDown && !_hotkeyHeld)
                    {
                        _hotkeyHeld = true;
                        SafeRaise(HotkeyPressed, nameof(HotkeyPressed));
                    }
                    else if (isKeyUp && _hotkeyHeld)
                    {
                        _hotkeyHeld = false;
                        SafeRaise(HotkeyReleased, nameof(HotkeyReleased));
                    }
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void SafeRaise(Action? handler, string name)
    {
        try { handler?.Invoke(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Exception in hotkey event {Event}", name); }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
