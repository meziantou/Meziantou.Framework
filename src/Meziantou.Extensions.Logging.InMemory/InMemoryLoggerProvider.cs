using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName);
    }

    public void Dispose()
    {
    }
}
