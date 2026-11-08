using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace FlowAI.Automation;

/// <summary>
/// Discovers installed applications and finds running instances.
/// Used by WindowsAutomationEngine to launch or switch to apps by name.
/// </summary>
public sealed class ApplicationDiscovery
{
    private readonly ILogger<ApplicationDiscovery>? _logger;

    // Well-known application paths (checked before registry scan)
    private static readonly Dictionary<string, string[]> WellKnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = [
            @"%PROGRAMFILES%\Google\Chrome\Application\chrome.exe",
            @"%PROGRAMFILES(X86)%\Google\Chrome\Application\chrome.exe",
            @"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
        ],
        ["google chrome"] = [
            @"%PROGRAMFILES%\Google\Chrome\Application\chrome.exe",
            @"%PROGRAMFILES(X86)%\Google\Chrome\Application\chrome.exe",
            @"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
        ],
        ["firefox"] = [
            @"%PROGRAMFILES%\Mozilla Firefox\firefox.exe",
            @"%PROGRAMFILES(X86)%\Mozilla Firefox\firefox.exe"
        ],
        ["edge"] = [
            @"%PROGRAMFILES%\Microsoft\Edge\Application\msedge.exe",
            @"%PROGRAMFILES(X86)%\Microsoft\Edge\Application\msedge.exe"
        ],
        ["microsoft edge"] = [
            @"%PROGRAMFILES%\Microsoft\Edge\Application\msedge.exe",
            @"%PROGRAMFILES(X86)%\Microsoft\Edge\Application\msedge.exe"
        ],
        ["notepad"] = [@"%WINDIR%\system32\notepad.exe"],
        ["notepad++"] = [
            @"%PROGRAMFILES%\Notepad++\notepad++.exe",
            @"%PROGRAMFILES(X86)%\Notepad++\notepad++.exe"
        ],
        ["calculator"] = [@"%WINDIR%\system32\calc.exe"],
        ["calc"] = [@"%WINDIR%\system32\calc.exe"],
        ["paint"] = [@"%WINDIR%\system32\mspaint.exe"],
        ["mspaint"] = [@"%WINDIR%\system32\mspaint.exe"],
        ["explorer"] = [@"%WINDIR%\explorer.exe"],
        ["file explorer"] = [@"%WINDIR%\explorer.exe"],
        ["wordpad"] = [@"%PROGRAMFILES%\Windows NT\Accessories\wordpad.exe"],
        ["cmd"] = [@"%WINDIR%\system32\cmd.exe"],
        ["command prompt"] = [@"%WINDIR%\system32\cmd.exe"],
        ["powershell"] = [@"%WINDIR%\system32\WindowsPowerShell\v1.0\powershell.exe"],
        ["terminal"] = [@"%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe"],
        ["windows terminal"] = [@"%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe"],
        ["task manager"] = [@"%WINDIR%\system32\taskmgr.exe"],
        ["taskmgr"] = [@"%WINDIR%\system32\taskmgr.exe"],
        ["control panel"] = [@"%WINDIR%\system32\control.exe"],
        ["settings"] = [@"%WINDIR%\ImmersiveControlPanel\SystemSettings.exe"],
        ["spotify"] = [
            @"%APPDATA%\Spotify\Spotify.exe",
            @"%LOCALAPPDATA%\Microsoft\WindowsApps\Spotify.exe"
        ],
        ["discord"] = [
            @"%LOCALAPPDATA%\Discord\Update.exe",
            @"%LOCALAPPDATA%\Microsoft\WindowsApps\Discord.exe"
        ],
        ["slack"] = [
            @"%LOCALAPPDATA%\slack\slack.exe",
            @"%LOCALAPPDATA%\Microsoft\WindowsApps\Slack.exe"
        ],
        ["teams"] = [
            @"%LOCALAPPDATA%\Microsoft\Teams\current\Teams.exe",
            @"%LOCALAPPDATA%\Microsoft\WindowsApps\MicrosoftTeams.exe"
        ],
        ["microsoft teams"] = [
            @"%LOCALAPPDATA%\Microsoft\Teams\current\Teams.exe",
            @"%LOCALAPPDATA%\Microsoft\WindowsApps\MicrosoftTeams.exe"
        ],
        ["vscode"] = [
            @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe",
            @"%PROGRAMFILES%\Microsoft VS Code\Code.exe"
        ],
        ["visual studio code"] = [
            @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe",
            @"%PROGRAMFILES%\Microsoft VS Code\Code.exe"
        ],
        ["word"] = [
            @"%PROGRAMFILES%\Microsoft Office\root\Office16\WINWORD.EXE",
            @"%PROGRAMFILES(X86)%\Microsoft Office\root\Office16\WINWORD.EXE"
        ],
        ["excel"] = [
            @"%PROGRAMFILES%\Microsoft Office\root\Office16\EXCEL.EXE",
            @"%PROGRAMFILES(X86)%\Microsoft Office\root\Office16\EXCEL.EXE"
        ],
        ["powerpoint"] = [
            @"%PROGRAMFILES%\Microsoft Office\root\Office16\POWERPNT.EXE",
            @"%PROGRAMFILES(X86)%\Microsoft Office\root\Office16\POWERPNT.EXE"
        ],
        ["outlook"] = [
            @"%PROGRAMFILES%\Microsoft Office\root\Office16\OUTLOOK.EXE",
            @"%PROGRAMFILES(X86)%\Microsoft Office\root\Office16\OUTLOOK.EXE"
        ],
    };

    public ApplicationDiscovery(ILogger<ApplicationDiscovery>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds the executable path for the named application.
    /// Returns null if not found.
    /// </summary>
    public string? FindApplication(string appName)
    {
        appName = appName.Trim();

        // 1. Check well-known apps first (fastest)
        if (WellKnownApps.TryGetValue(appName, out var candidates))
        {
            foreach (var template in candidates)
            {
                string expanded = Environment.ExpandEnvironmentVariables(template);
                if (File.Exists(expanded))
                {
                    _logger?.LogInformation("Found well-known app '{Name}' at: {Path}", appName, expanded);
                    return expanded;
                }
            }
        }

        // 2. Try Windows App Paths registry (HKLM and HKCU)
        string? regPath = FindInRegistry(appName);
        if (regPath != null)
        {
            _logger?.LogInformation("Found app '{Name}' via registry: {Path}", appName, regPath);
            return regPath;
        }

        // 3. Try Start Menu shortcuts
        string? startMenuPath = FindInStartMenu(appName);
        if (startMenuPath != null)
        {
            _logger?.LogInformation("Found app '{Name}' via Start Menu: {Path}", appName, startMenuPath);
            return startMenuPath;
        }

        _logger?.LogWarning("Application not found: '{Name}'", appName);
        return null;
    }

    /// <summary>
    /// Finds the first running process whose name matches appName.
    /// Returns the process, or null if not running.
    /// </summary>
    public static Process? FindRunningProcess(string appName)
    {
        string searchName = appName
            .Replace(" ", "")
            .Replace(".", "")
            .ToLowerInvariant();

        // Also check common process name mappings
        string processName = searchName switch
        {
            "googlechrome" or "chrome" => "chrome",
            "microsoftedge" or "edge" => "msedge",
            "microsoftteams" or "teams" => "Teams",
            "visualstudiocode" or "vscode" or "code" => "Code",
            "notepadplusplus" or "notepad++" => "notepad++",
            _ => searchName
        };

        try
        {
            var processes = Process.GetProcesses();
            var matches = processes.Where(p =>
                p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains(searchName, StringComparison.OrdinalIgnoreCase)).ToList();

            // Prefer a process that already has a visible window handle (avoids suspended background instances)
            return matches.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)
                ?? matches.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launches the application or switches to it if already running.
    /// Returns the launched/found process.
    /// </summary>
    public async Task<(bool Success, string Message)> LaunchOrActivateAsync(
        string appName,
        string? arguments = null,
        CancellationToken cancellationToken = default)
    {
        // Check if already running
        var runningProc = FindRunningProcess(appName);
        if (runningProc != null)
        {
            try
            {
                // Try to bring to foreground
                IntPtr hwnd = runningProc.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    AppDiscoveryNativeMethods.SetForegroundWindow(hwnd);
                    AppDiscoveryNativeMethods.ShowWindow(hwnd, 9); // SW_RESTORE
                    _logger?.LogInformation("Switched to running process '{Name}' (PID {Pid})", appName, runningProc.Id);
                    return (true, $"Switched to already-running {appName}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not bring '{Name}' to foreground", appName);
            }
        }

        // Find the executable
        string? exePath = FindApplication(appName);
        if (exePath == null)
        {
            // Last-resort: try shell-execute the app name directly (works for well-registered apps)
            try
            {
                var shellInfo = new ProcessStartInfo(appName) { UseShellExecute = true };
                Process.Start(shellInfo);
                _logger?.LogInformation("Shell-launched '{Name}' by name (no resolved path)", appName);
                await WaitForWindowReadyAsync(appName, 2500, cancellationToken);
                return (true, $"Launched {appName}");
            }
            catch
            {
                return (false, $"Could not find or launch application: {appName}");
            }
        }

        // Launch it
        try
        {
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Arguments = arguments ?? string.Empty
            };

            if (exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // For Discord, pass --processStart argument instead of launching Update.exe directly
                if (exePath.Contains("Discord") && exePath.Contains("Update.exe"))
                {
                    startInfo.Arguments = "--processStart Discord.exe";
                }
            }

            var proc = Process.Start(startInfo);
            _logger?.LogInformation("Launched '{Name}' from: {Path} (PID {Pid})", appName, exePath, proc?.Id);

            // Wait for application window to appear and become ready
            await WaitForWindowReadyAsync(appName, 2500, cancellationToken);
            return (true, $"Launched {appName}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch '{Name}' from path: {Path}", appName, exePath);
            return (false, $"Failed to launch {appName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits for an application's main window to be created and brings it to foreground.
    /// </summary>
    public async Task<bool> WaitForWindowReadyAsync(string appName, int timeoutMs = 2500, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
            var proc = FindRunningProcess(appName);
            if (proc != null)
            {
                try
                {
                    proc.Refresh();
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        AppDiscoveryNativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                        AppDiscoveryNativeMethods.ShowWindow(proc.MainWindowHandle, 9); // SW_RESTORE
                        return true;
                    }
                }
                catch { }
            }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private static string? FindInRegistry(string appName)
    {
        string[] registryKeys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
        ];

        foreach (var regKey in registryKeys)
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(regKey);
                    if (key == null) continue;

                    // Try exact match with .exe
                    string[] subKeys = key.GetSubKeyNames();
                    var match = subKeys.FirstOrDefault(k =>
                        k.Equals(appName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith(appName, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        using var appKey = key.OpenSubKey(match);
                        string? path = appKey?.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private static string? FindInStartMenu(string appName)
    {
        string[] startMenuDirs =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        ];

        foreach (var dir in startMenuDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var shortcuts = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                var match = shortcuts.FirstOrDefault(s =>
                    Path.GetFileNameWithoutExtension(s).Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(s).Contains(appName, StringComparison.OrdinalIgnoreCase));

                // We can't resolve .lnk without Shell32 — just return null for Start Menu hits for now
                // TODO: use IShellLink to resolve the target
            }
            catch { }
        }

        return null;
    }
}

/// <summary>Native window activation helpers used by ApplicationDiscovery.</summary>
internal static class AppDiscoveryNativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
