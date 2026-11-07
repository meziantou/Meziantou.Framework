using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.PostgreSql.Fuzz;

/// <summary>Records what the connection processor logs: it logs the exceptions it turns into error responses.</summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly Lock _lock = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var message = formatter(state, exception);
        lock (_lock)
        {
            _entries.Add(new LogEntry(logLevel, message, exception));
        }
    }

    internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
