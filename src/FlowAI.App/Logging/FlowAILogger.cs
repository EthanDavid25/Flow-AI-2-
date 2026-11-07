using System.IO;
using System.Text;
using FlowAI.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowAI.App.Logging;

/// <summary>
/// Thread-safe, non-blocking persistent file logger for FlowAI.
/// Writes timestamped diagnostic logs to %APPDATA%\FlowAI\Logs\flowai_{yyyyMMdd}.log.
/// Captures component startup stages, exceptions with inner exceptions, and shutdown reasons.
/// </summary>
public sealed class FlowAILoggerProvider : ILoggerProvider
{
    private static readonly object WriteLock = new();
    private readonly LogLevel minLogLevel;

    public static string LogsDirectory =>
        Path.Combine(FlowAISettings.AppDataDirectory, "Logs");

    public static string CurrentLogFilePath =>
        Path.Combine(LogsDirectory, $"flowai_{DateTime.Now:yyyyMMdd}.log");

    public FlowAILoggerProvider(LogLevel minLogLevel = LogLevel.Information)
    {
        this.minLogLevel = minLogLevel;
        try
        {
            Directory.CreateDirectory(LogsDirectory);
        }
        catch { }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FlowAILogger(categoryName, minLogLevel);
    }

    public void Dispose() { }

    internal static void WriteEntry(string category, LogLevel level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant(),-5}] [{category}] {message}");

            if (exception != null)
            {
                sb.AppendLine();
                sb.Append("  EXCEPTION: ");
                sb.Append(exception.GetType().FullName);
                sb.Append(": ");
                sb.AppendLine(exception.Message);

                if (!string.IsNullOrWhiteSpace(exception.StackTrace))
                {
                    sb.AppendLine("  STACK TRACE:");
                    sb.AppendLine(exception.StackTrace);
                }

                // Inner exceptions
                var inner = exception.InnerException;
                int depth = 1;
                while (inner != null && depth <= 5)
                {
                    sb.AppendLine($"  INNER EXCEPTION ({depth}): {inner.GetType().FullName}: {inner.Message}");
                    if (!string.IsNullOrWhiteSpace(inner.StackTrace))
                    {
                        sb.AppendLine(inner.StackTrace);
                    }
                    inner = inner.InnerException;
                    depth++;
                }
            }

            sb.AppendLine();

            lock (WriteLock)
            {
                File.AppendAllText(CurrentLogFilePath, sb.ToString());
            }
        }
        catch
        {
            // Logging failure must never crash the host application
        }
    }

    private sealed class FlowAILogger : ILogger
    {
        private readonly string categoryName;
        private readonly LogLevel minLevel;

        public FlowAILogger(string categoryName, LogLevel minLevel)
        {
            this.categoryName = categoryName;
            this.minLevel = minLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            FlowAILoggerProvider.WriteEntry(categoryName, logLevel, message, exception);
        }
    }
}
