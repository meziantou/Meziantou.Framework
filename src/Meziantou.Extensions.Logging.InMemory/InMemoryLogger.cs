using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

internal sealed class InMemoryLogger : ILogger
{
    private readonly string? _category;
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly InMemoryLogCollection _logs;
#if NET8_0_OR_GREATER
    private readonly TimeProvider _timeProvider;
#endif

    public InMemoryLogger(string category, InMemoryLogCollection logs, IExternalScopeProvider scopeProvider
#if NET8_0_OR_GREATER
        , TimeProvider timeProvider
#endif
        )
    {
        _category = category;
        _logs = logs;
        _scopeProvider = scopeProvider;
#if NET8_0_OR_GREATER
        _timeProvider = timeProvider;
#endif
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
#if NET8_0_OR_GREATER
        var now = _timeProvider.GetUtcNow();
#else
        var now = DateTimeOffset.UtcNow;
#endif
        var scopes = new List<object?>();
        _scopeProvider.ForEachScope((current, scopes) => scopes.Add(current), scopes);
        _logs.Add(new InMemoryLogEntry(now, _category, logLevel, eventId, scopes, state, exception, formatter(state, exception)));
    }
}
