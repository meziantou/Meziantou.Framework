using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging;

/// <summary>A logger that writes to a file via the FileLoggerProvider.</summary>
internal sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
{
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var timestamp = provider.TimeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var level = GetLogLevelString(logLevel);
        var shortCategory = GetShortCategoryName(categoryName);

        var logMessage = exception is not null
            ? $"[{timestamp}] [{level}] [{shortCategory}] {message}{Environment.NewLine}{exception}"
            : $"[{timestamp}] [{level}] [{shortCategory}] {message}";

        provider.WriteLog(logMessage);
    }

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => logLevel.ToString().ToUpperInvariant()
    };

    private static string GetShortCategoryName(string categoryName)
    {
        var lastDotIndex = categoryName.LastIndexOf('.');
        return lastDotIndex >= 0 ? categoryName.Substring(lastDotIndex + 1) : categoryName;
    }
}
