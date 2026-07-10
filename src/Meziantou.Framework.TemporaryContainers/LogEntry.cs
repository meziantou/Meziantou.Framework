namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents a single log line emitted by a container.</summary>
/// <param name="Stream">The stream the line was written to.</param>
/// <param name="Message">The log line, without the timestamp prefix.</param>
/// <param name="Timestamp">The timestamp reported by the runtime, when available.</param>
public sealed record LogEntry(LogStream Stream, string Message, DateTimeOffset? Timestamp);
