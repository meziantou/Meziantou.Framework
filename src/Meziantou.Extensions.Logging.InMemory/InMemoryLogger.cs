using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public class InMemoryLogger : IInMemoryLogger
{
    private readonly string? _category;
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly TimeProvider _timeProvider;

    public InMemoryLogCollection Logs { get; }

    public static IInMemoryLogger CreateLogger(string category, InMemoryLogCollection? logs = null, IExternalScopeProvider? scopeProvider = null, TimeProvider? timeProvider = null)
    {
        logs ??= [];
        scopeProvider ??= new LoggerExternalScopeProvider();
        timeProvider ??= TimeProvider.System;
        return new InMemoryLogger(category, logs, scopeProvider, timeProvider);
    }

    public static IInMemoryLogger<T> CreateLogger<T>(InMemoryLogCollection? logs = null, IExternalScopeProvider? scopeProvider = null, TimeProvider? timeProvider = null)
    {
        logs ??= [];
        scopeProvider ??= new LoggerExternalScopeProvider();
        timeProvider ??= TimeProvider.System;
        return new InMemoryLogger<T>(logs, scopeProvider, timeProvider);
    }

    internal InMemoryLogger(string category, InMemoryLogCollection logs, IExternalScopeProvider scopeProvider, TimeProvider timeProvider)
    {
        _category = category;
        Logs = logs;
        _scopeProvider = scopeProvider;
        _timeProvider = timeProvider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var now = _timeProvider.GetUtcNow();
        var scopes = new List<object?>();
        _scopeProvider.ForEachScope((current, scopes) => scopes.Add(current), scopes);
        Logs.Add(new InMemoryLogEntry(now, _category, logLevel, eventId, scopes, state, exception, formatter(state, exception)));
    }
}
