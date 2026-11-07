using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FlowAI.AI;
using FlowAI.AI.Providers;
using FlowAI.AI.Security;
using FlowAI.App.Hotkey;
using FlowAI.App.Tray;
using FlowAI.App.UI;
using FlowAI.Audio.Capture;
using FlowAI.Audio.Transcription;
using FlowAI.Audio.Tts;
using FlowAI.Automation.Engine;
using FlowAI.Core.Configuration;
using FlowAI.Core.Interfaces;
using FlowAI.Core.StateMachine;
using FlowAI.Learning;
using FlowAI.Memory;
using FlowAI.Overlay;
using FlowAI.Vision.Capture;
using FlowAI.Vision.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowAI.App;

/// <summary>
/// FlowAI application entry point.
///
/// Architecture:
///   - WPF UI thread owns the CompanionPanel window and WPF dispatcher.
///   - A dedicated STA thread hosts the low-level keyboard hook message pump.
///   - A Windows Forms message pump thread hosts the NotifyIcon (requires STA + Forms loop).
///   - All service state is shared through CompanionManager (thread-safe).
///   - ShutdownMode = OnExplicitShutdown — the app stays alive while the tray icon is visible.
/// </summary>
public partial class App : Application
{
    // ── Single-instance mutex ─────────────────────────────────────────────────
    private static Mutex? _singleInstanceMutex;
    private const string MutexName = "FlowAI_SingleInstance_2026";

    // ── Services ──────────────────────────────────────────────────────────────
    private ServiceProvider? _services;
    private CompanionManager? _manager;
    private CompanionPanelWindow? _panel;
    private TrayIconManager? _tray;
    private Thread? _trayThread;

    // ── Startup ───────────────────────────────────────────────────────────────
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard
        _singleInstanceMutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "FlowAI is already running.\nLook for the green icon in the system tray.",
                "FlowAI", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Global crash handler
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;

        try
        {
            // Build services
            _services = BuildServices();

            // Create UI
            _panel = new CompanionPanelWindow(
                _services.GetService<ILogger<CompanionPanelWindow>>());

            // Create and wire companion manager
            _manager = _services.GetRequiredService<CompanionManager>();
            _panel.BindManager(_manager);

            _panel.SettingsRequested += ShowSettings;
            _panel.MemoryRequested += ShowMemory;
            _panel.QuitRequested += RequestShutdown;

            // Start tray icon on its own STA thread (NotifyIcon requires STA + its own message pump)
            StartTrayThread();

            // Initialise overlays (must be on WPF UI thread)
            var overlayManager = _services.GetRequiredService<OverlayWindowManager>();
            overlayManager.InitializeOverlays();

            // Initialise companion manager (async: AI health check, memory, transcription warm-up)
            await _manager.InitialiseAsync();

            // Show companion panel
            _panel.Show();

            // Show first-run welcome
            if (!FlowAISettings.Current.FirstRunCompleted)
            {
                ShowFirstRun();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"FlowAI failed to start:\n\n{ex.Message}\n\nSee logs for details.",
                "FlowAI Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // ── DI composition root ───────────────────────────────────────────────────
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        // Settings (singleton from static Current)
        services.AddSingleton(_ => FlowAISettings.Current);

        // State machine
        services.AddSingleton<FlowAIStateManager>();

        // AI providers
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<KimiProvider>();
        services.AddSingleton<AIProviderFactory>();
        services.AddSingleton<IAIProvider>(sp =>
            sp.GetRequiredService<AIProviderFactory>().GetActiveProvider());

        // Vision
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.AddSingleton<UIAutomationDiscoveryService>();
        services.AddSingleton<IElementDiscoveryService>(sp =>
            sp.GetRequiredService<UIAutomationDiscoveryService>());
        services.AddSingleton<HybridElementLocator>();

        // Audio — select implementation based on settings
        services.AddSingleton<IAudioRecordingService, WasapiAudioRecorder>();
        services.AddSingleton<WhisperTranscriptionService>();
        services.AddSingleton<WindowsSpeechTranscriptionService>();
        services.AddSingleton<ITranscriptionService>(sp =>
        {
            var settings = sp.GetRequiredService<FlowAISettings>();
            if (settings.SttProvider == "WindowsSpeech")
                return sp.GetRequiredService<WindowsSpeechTranscriptionService>();
            return sp.GetRequiredService<WhisperTranscriptionService>();
        });

        services.AddSingleton<EdgeTtsService>();
        services.AddSingleton<WindowsSpeechTtsService>();
        services.AddSingleton<ITextToSpeechService>(sp =>
        {
            var settings = sp.GetRequiredService<FlowAISettings>();
            if (settings.TtsProvider == "WindowsSpeech")
                return sp.GetRequiredService<WindowsSpeechTtsService>();
            return sp.GetRequiredService<EdgeTtsService>();
        });

        // Overlay
        services.AddSingleton<OverlayWindowManager>();

        // Memory & learning
        services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
        services.AddSingleton<ILearningEngine, LearningEngine>();

        // Automation
        services.AddSingleton<IComputerAutomationService, WindowsAutomationEngine>();

        // Hotkey
        services.AddSingleton<GlobalHotkeyMonitor>();

        // Companion manager (orchestrator)
        services.AddSingleton<CompanionManager>();

        return services.BuildServiceProvider();
    }

    // ── Tray icon (dedicated STA thread with message pump) ────────────────────
    private void StartTrayThread()
    {
        _trayThread = new Thread(() =>
        {
            System.Windows.Forms.Application.EnableVisualStyles();

            _tray = new TrayIconManager(
                _services?.GetService<ILogger<TrayIconManager>>());

            _tray.ShowHideRequested += () =>
                Dispatcher.BeginInvoke(() => _panel?.ToggleVisibility());

            _tray.SettingsRequested += () =>
                Dispatcher.BeginInvoke(ShowSettings);

            _tray.MemoryRequested += () =>
                Dispatcher.BeginInvoke(ShowMemory);

            _tray.QuitRequested += () =>
                Dispatcher.BeginInvoke(RequestShutdown);

            _tray.Initialize();
            _tray.ShowBalloon("FlowAI started",
                "Press Ctrl+Alt+Space to talk. Right-click tray icon for options.", 4000);

            System.Windows.Forms.Application.Run();  // message pump
        })
        {
            Name = "FlowAI-TrayThread",
            IsBackground = true
        };
        _trayThread.SetApartmentState(ApartmentState.STA);
        _trayThread.Start();
    }

    // ── Window management ─────────────────────────────────────────────────────
    private void ShowSettings()
    {
        var settings = new SettingsWindow(
            FlowAISettings.Current,
            _services?.GetService<ILogger<SettingsWindow>>())
        {
            Owner = _panel
        };

        if (settings.ShowDialog() == true)
        {
            // Settings saved — notify user; full reload requires restart for some changes
            _tray?.ShowBalloon("Settings saved",
                "Some changes (AI provider, voice) take effect on next query.", 3000);
        }
    }

    private void ShowMemory()
    {
        var memoryStore = _services?.GetRequiredService<IMemoryStore>();
        if (memoryStore == null) return;

        var memoryWindow = new MemoryViewerWindow(
            memoryStore,
            _services?.GetService<ILogger<MemoryViewerWindow>>())
        {
            Owner = _panel
        };
        memoryWindow.Show();
    }

    private void ShowFirstRun()
    {
        var result = MessageBox.Show(
            "Welcome to FlowAI!\n\n" +
            "• Press Ctrl+Alt+Space to start voice interaction\n" +
            "• Ask questions about anything on your screen\n" +
            "• FlowAI will guide your cursor to what you need\n\n" +
            $"AI Model: {FlowAISettings.Current.OllamaModel} (local via Ollama)\n" +
            $"STT: {FlowAISettings.Current.SttProvider}\n" +
            $"TTS: {FlowAISettings.Current.TtsProvider}\n\n" +
            "Make sure Ollama is running with: ollama serve\n\n" +
            "Open Settings to configure voice, AI provider and appearance.",
            "Welcome to FlowAI",
            MessageBoxButton.OK, MessageBoxImage.Information);

        FlowAISettings.Current.FirstRunCompleted = true;
        FlowAISettings.Current.Save();
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────
    private void RequestShutdown()
    {
        _manager?.Dispose();
        _tray?.Dispose();

        // Stop the Windows Forms message pump on the tray thread
        System.Windows.Forms.Application.Exit();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        _services?.Dispose();
        Shutdown();
    }

    // ── Error handlers ────────────────────────────────────────────────────────
    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("UI thread unhandled exception", e.Exception);
        e.Handled = true;  // prevent crash — show notification instead
        _tray?.ShowBalloon("FlowAI error", e.Exception.Message, 5000);
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash("AppDomain unhandled exception", ex);
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string context, Exception ex)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                FlowAISettings.AppDataDirectory, "crash.log");
            System.IO.Directory.CreateDirectory(FlowAISettings.AppDataDirectory);
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\n\n");
        }
        catch { /* log failure must not crash */ }
    }
}
