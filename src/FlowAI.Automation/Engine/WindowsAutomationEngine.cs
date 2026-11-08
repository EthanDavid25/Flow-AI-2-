using System.Diagnostics;
using System.Windows.Automation;
using FlowAI.Automation;
using FlowAI.Automation.Native;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using FlowAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlowAI.Automation.Engine;

public sealed class WindowsAutomationEngine : IComputerAutomationService
{
    private readonly IElementDiscoveryService discoveryService;
    private readonly ILogger<WindowsAutomationEngine>? logger;
    private readonly ApplicationDiscovery appDiscovery;

    public WindowsAutomationEngine(
        IElementDiscoveryService discoveryService,
        ApplicationDiscovery? appDiscovery = null,
        ILogger<WindowsAutomationEngine>? logger = null)
    {
        this.discoveryService = discoveryService;
        this.appDiscovery = appDiscovery ?? new ApplicationDiscovery(null);
        this.logger = logger;
    }

    public async Task<AutomationResult> ExecuteActionAsync(
        AutomationAction action,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Risk check & permission confirmation
        if (action.RiskLevel == ActionRiskLevel.High &&
            FlowAISettings.Current.RequireConfirmationForConsequential &&
            !action.UserConfirmed)
        {
            return new AutomationResult
            {
                Success = false,
                Message = $"Action '{action.Description}' cancelled: requires user confirmation.",
                Verified = false,
                Duration = stopwatch.Elapsed
            };
        }

        try
        {
            switch (action.ActionType.ToLowerInvariant())
            {
                case "movecursor":
                    ExecuteMoveCursor(action);
                    break;
                case "click":
                case "leftclick":
                    await ExecuteClickAsync(action, cancellationToken);
                    break;
                case "typetext":
                    ExecuteTypeText(action);
                    break;
                case "scroll":
                    ExecuteScroll(action);
                    break;
                case "shortcut":
                    ExecuteShortcut(action);
                    break;
                case "invokeuia":
                    ExecuteInvokeUia(action);
                    break;
                case "launchapplication":
                case "launch":
                case "openapp":
                case "open":
                    await ExecuteLaunchApplicationAsync(action, cancellationToken);
                    break;
                case "switchwindow":
                case "activatewindow":
                    ExecuteSwitchWindow(action);
                    break;
                case "closewindow":
                    ExecuteCloseWindow(action);
                    break;
                case "maximizewindow":
                    ExecuteMaximizeWindow(action);
                    break;
                case "minimizewindow":
                    ExecuteMinimizeWindow(action);
                    break;
                case "rightclick":
                    await ExecuteRightClickAsync(action, cancellationToken);
                    break;
                case "doubleclick":
                    await ExecuteDoubleClickAsync(action, cancellationToken);
                    break;
                default:
                    return new AutomationResult
                    {
                        Success = false,
                        Message = $"Unsupported action type: {action.ActionType}",
                        Duration = stopwatch.Elapsed
                    };
            }

            // 2. Result Verification
            bool verified = true;
            if (FlowAISettings.Current.VerifyActionOutcomes)
            {
                verified = await VerifyResultAsync(action, cancellationToken);
            }

            stopwatch.Stop();
            return new AutomationResult
            {
                Success = true,
                Message = $"Successfully executed: {action.Description}",
                Verified = verified,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogError(ex, "Execution failed for action '{Desc}'", action.Description);
            return new AutomationResult
            {
                Success = false,
                Message = $"Execution error: {ex.Message}",
                Verified = false,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public Task<bool> VerifyResultAsync(AutomationAction action, CancellationToken cancellationToken = default)
    {
        // Give the UI 150ms to settle
        Thread.Sleep(150);

        if (action.Target != null)
        {
            // Revalidate that target was affected or still responsive
            bool stillValid = discoveryService.RevalidateTarget(action.Target);
            return Task.FromResult(stillValid);
        }

        return Task.FromResult(true);
    }

    private static void ExecuteMoveCursor(AutomationAction action)
    {
        if (action.Target != null)
        {
            int targetX = (int)Math.Round(action.Target.TargetPoint.X);
            int targetY = (int)Math.Round(action.Target.TargetPoint.Y);
            AutomationNativeMethods.SetCursorPos(targetX, targetY);
        }
    }

    private static async Task ExecuteClickAsync(AutomationAction action, CancellationToken ct)
    {
        if (action.Target != null)
        {
            int x = (int)Math.Round(action.Target.TargetPoint.X);
            int y = (int)Math.Round(action.Target.TargetPoint.Y);
            AutomationNativeMethods.SetCursorPos(x, y);
            await Task.Delay(40, ct);

            // Left click down and up
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_LEFTDOWN, x, y, 0, UIntPtr.Zero);
            await Task.Delay(30, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_LEFTUP, x, y, 0, UIntPtr.Zero);
        }
    }

    private static void ExecuteTypeText(AutomationAction action)
    {
        string text = action.Parameter ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text)
        {
            // Send unicode keystroke
            AutomationNativeMethods.keybd_event(0, (byte)c, AutomationNativeMethods.KEYEVENTF_UNICODE, UIntPtr.Zero);
            AutomationNativeMethods.keybd_event(0, (byte)c, AutomationNativeMethods.KEYEVENTF_UNICODE | AutomationNativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    private static void ExecuteScroll(AutomationAction action)
    {
        int amount = int.TryParse(action.Parameter, out int parsed) ? parsed : -120;
        AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_WHEEL, 0, 0, (uint)amount, UIntPtr.Zero);
    }

    private static void ExecuteShortcut(AutomationAction action)
    {
        string shortcut = action.Parameter ?? string.Empty;
        if (shortcut.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            AutomationNativeMethods.keybd_event(0x0D, 0, 0, UIntPtr.Zero);
            AutomationNativeMethods.keybd_event(0x0D, 0, AutomationNativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        else if (shortcut.Equals("Escape", StringComparison.OrdinalIgnoreCase))
        {
            AutomationNativeMethods.keybd_event(0x1B, 0, 0, UIntPtr.Zero);
            AutomationNativeMethods.keybd_event(0x1B, 0, AutomationNativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    private static void ExecuteInvokeUia(AutomationAction action)
    {
        if (action.Target != null && !string.IsNullOrEmpty(action.Target.AutomationId))
        {
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, action.Target.AutomationId);
            var element = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition);
            if (element != null)
            {
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) && pattern is InvokePattern invokePattern)
                {
                    invokePattern.Invoke();
                }
            }
        }
    }

    private async Task ExecuteLaunchApplicationAsync(AutomationAction action, CancellationToken ct)
    {
        string appName = action.Parameter ?? action.Description;
        appName = appName
            .Replace("open ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("launch ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("start ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        logger?.LogInformation("Launching application: '{Name}'", appName);
        var (success, message) = await appDiscovery.LaunchOrActivateAsync(appName, cancellationToken: ct);
        if (!success)
            throw new InvalidOperationException(message);
    }

    private static void ExecuteSwitchWindow(AutomationAction action)
    {
        string processName = action.Parameter ?? string.Empty;
        var proc = ApplicationDiscovery.FindRunningProcess(processName);
        if (proc != null)
        {
            var hwnd = proc.MainWindowHandle;
            if (hwnd != IntPtr.Zero)
            {
                AutomationNativeMethods.SetForegroundWindow(hwnd);
                NativeWindowOps.ShowWindow(hwnd, 9); // SW_RESTORE
            }
        }
    }

    private static void ExecuteCloseWindow(AutomationAction action)
    {
        string processName = action.Parameter ?? string.Empty;
        var proc = ApplicationDiscovery.FindRunningProcess(processName);
        proc?.CloseMainWindow();
    }

    private static void ExecuteMaximizeWindow(AutomationAction action)
    {
        string processName = action.Parameter ?? string.Empty;
        var proc = ApplicationDiscovery.FindRunningProcess(processName);
        if (proc != null && proc.MainWindowHandle != IntPtr.Zero)
            NativeWindowOps.ShowWindow(proc.MainWindowHandle, 3); // SW_MAXIMIZE
    }

    private static void ExecuteMinimizeWindow(AutomationAction action)
    {
        string processName = action.Parameter ?? string.Empty;
        var proc = ApplicationDiscovery.FindRunningProcess(processName);
        if (proc != null && proc.MainWindowHandle != IntPtr.Zero)
            NativeWindowOps.ShowWindow(proc.MainWindowHandle, 6); // SW_MINIMIZE
    }

    private static async Task ExecuteRightClickAsync(AutomationAction action, CancellationToken ct)
    {
        if (action.Target != null)
        {
            int x = (int)Math.Round(action.Target.TargetPoint.X);
            int y = (int)Math.Round(action.Target.TargetPoint.Y);
            AutomationNativeMethods.SetCursorPos(x, y);
            await Task.Delay(40, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_RIGHTDOWN, x, y, 0, UIntPtr.Zero);
            await Task.Delay(30, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_RIGHTUP, x, y, 0, UIntPtr.Zero);
        }
    }

    private static async Task ExecuteDoubleClickAsync(AutomationAction action, CancellationToken ct)
    {
        if (action.Target != null)
        {
            int x = (int)Math.Round(action.Target.TargetPoint.X);
            int y = (int)Math.Round(action.Target.TargetPoint.Y);
            AutomationNativeMethods.SetCursorPos(x, y);
            await Task.Delay(40, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_LEFTDOWN, x, y, 0, UIntPtr.Zero);
            await Task.Delay(30, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_LEFTUP, x, y, 0, UIntPtr.Zero);
            await Task.Delay(80, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_LEFTDOWN, x, y, 0, UIntPtr.Zero);
            await Task.Delay(30, ct);
            AutomationNativeMethods.mouse_event(AutomationNativeMethods.MOUSEEVENTF_LEFTUP, x, y, 0, UIntPtr.Zero);
        }
    }
}

/// <summary>Extra P/Invoke for window show/hide — avoids duplicating SetForegroundWindow already in AutomationNativeMethods.</summary>
internal static class NativeWindowOps
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
