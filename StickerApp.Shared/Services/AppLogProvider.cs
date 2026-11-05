using Microsoft.Extensions.Logging;
using StickerApp.Core;

namespace StickerApp.Shared.Services;

public sealed class AppLogProvider(AppLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(log, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(AppLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level is >= LogLevel.Warning and < LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) log.Write(level.ToString(), category, formatter(state, exception), exception);
        }
    }
}
