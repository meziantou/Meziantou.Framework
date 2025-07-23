using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public interface IInMemoryLogger : ILogger
{
    InMemoryLogCollection Logs { get; }
}
