using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

/// <summary>Represents a generic logger that stores log entries in memory.</summary>
/// <typeparam name="T">The type whose name is used for the logger category name.</typeparam>
public interface IInMemoryLogger<T> : ILogger<T>
{
    /// <summary>Gets the collection of log entries captured by this logger.</summary>
    InMemoryLogCollection Logs { get; }
}
