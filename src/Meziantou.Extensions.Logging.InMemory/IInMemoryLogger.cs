using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

/// <summary>Represents a logger that stores log entries in memory.</summary>
public interface IInMemoryLogger : ILogger
{
    /// <summary>Gets the collection of log entries captured by this logger.</summary>
    InMemoryLogCollection Logs { get; }
}
