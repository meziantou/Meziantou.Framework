using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly TimeProvider _timeProvider;

    public InMemoryLogCollection Logs { get; }

    public InMemoryLoggerProvider()
        : this(logs: null, scopeProvider: null)
    {
    }

    public InMemoryLoggerProvider(InMemoryLogCollection? logs)
        : this(logs, scopeProvider: null)
    {
    }

    public InMemoryLoggerProvider(IExternalScopeProvider? scopeProvider)
        : this(logs: null, scopeProvider)
    {
    }

    public InMemoryLoggerProvider(InMemoryLogCollection? logs, IExternalScopeProvider? scopeProvider)
    {
        Logs = logs ?? [];
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
        _timeProvider = TimeProvider.System;
    }

    public InMemoryLoggerProvider(TimeProvider? timeProvider)
    : this(timeProvider, logs: null, scopeProvider: null)
    {
    }

    public InMemoryLoggerProvider(TimeProvider? timeProvider, InMemoryLogCollection? logs)
        : this(timeProvider, logs, scopeProvider: null)
    {
    }

    public InMemoryLoggerProvider(TimeProvider? timeProvider, IExternalScopeProvider? scopeProvider)
        : this(timeProvider, logs: null, scopeProvider)
    {
    }

    public InMemoryLoggerProvider(TimeProvider? timeProvider, InMemoryLogCollection? logs, IExternalScopeProvider? scopeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Logs = logs ?? [];
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName, Logs, _scopeProvider, _timeProvider);
    }

    public ILogger<T> CreateLogger<T>()
    {
        return new InMemoryLogger<T>(Logs, _scopeProvider, _timeProvider);
    }

    public void Dispose()
    {
    }
}
