using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

internal sealed class InMemoryLogger : ILogger
{
    private readonly string? _category;
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly InMemoryLogCollection _logs;

    public InMemoryLogger(string category, InMemoryLogCollection logs, IExternalScopeProvider scopeProvider)
    {
        _category = category;
        _logs = logs;
        _scopeProvider = scopeProvider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var scopes = new List<object?>();
        _scopeProvider.ForEachScope((current, scopes) => scopes.Add(current), scopes);
        _logs.Add(new InMemoryLogEntry(_category, logLevel, eventId, scopes, state, exception, formatter(state, exception)));
    }
}
