using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly IExternalScopeProvider _scopeProvider;
    private readonly InMemoryLogCollection _logs;

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
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName, _logs, _scopeProvider);
    }

    public void Dispose()
    {
    }
}
