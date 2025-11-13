namespace Meziantou.AspNetCore.Components;

/// <summary>Represents a single log entry in the log viewer.</summary>
public class LogEntry
{
    /// <summary>Gets or sets the timestamp when the log entry was created.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Gets or sets the severity level of the log entry.</summary>
    public LogLevel LogLevel { get; set; }

    /// <summary>Gets or sets the log message text.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets additional data associated with the log entry.</summary>
    public object? Data { get; set; }
}
