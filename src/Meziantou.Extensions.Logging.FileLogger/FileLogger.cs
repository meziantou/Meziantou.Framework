using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging;

/// <summary>A logger that writes to a file via the FileLoggerProvider.</summary>
internal sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
{
    private readonly string _shortCategoryName = GetShortCategoryName(categoryName);

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
        var logMessageBuilder = new StringBuilder(message.Length + _shortCategoryName.Length + 40);
        logMessageBuilder.Append('[');
        logMessageBuilder.Append(timestamp);
        logMessageBuilder.Append("] [");
        logMessageBuilder.Append(level);
        logMessageBuilder.Append("] [");
        logMessageBuilder.Append(_shortCategoryName);
        logMessageBuilder.Append("] ");
        logMessageBuilder.Append(message);
        if (exception is not null)
        {
            logMessageBuilder.AppendLine();
            logMessageBuilder.Append(exception);
        }

        provider.WriteLog(logMessageBuilder.ToString());
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
