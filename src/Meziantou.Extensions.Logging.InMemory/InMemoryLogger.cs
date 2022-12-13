using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLogger : ILogger
{
    private readonly string? _category;
    private readonly IExternalScopeProvider _scopeProvider;

    public InMemoryLogCollection Logs { get; } = new InMemoryLogCollection();

    public InMemoryLogger(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public InMemoryLogger(string category, IExternalScopeProvider scopeProvider)
    {
        _category = category;
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
        Logs.Add(new InMemoryLogEntry(_category, logLevel, eventId, scopes, state, exception, formatter(state, exception)));
    }
}
