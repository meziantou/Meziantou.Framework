using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly InMemoryLogCollection _logs;
    private readonly TimeProvider _timeProvider;

    public InMemoryLogCollection Logs => _logs;

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
        _logs = logs ?? [];
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
        _logs = logs ?? [];
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName, _logs, _scopeProvider, _timeProvider);
    }

    public ILogger<T> CreateLogger<T>()
    {
        return new InMemoryLogger<T>(_logs, _scopeProvider, _timeProvider);
    }

    public void Dispose()
    {
    }
}
