using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public interface IInMemoryLogger<T> : ILogger<T>
{
    InMemoryLogCollection Logs { get; }
}
