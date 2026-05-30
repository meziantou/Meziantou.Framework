namespace Meziantou.AspNetCore.Components;

/// <summary>Represents a single log entry in the log viewer.</summary>
public class LogEntry
{
    /// <summary>Gets or sets the timestamp when the log entry was created.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Gets or sets the severity level of the log entry.</summary>
    public LogLevel LogLevel { get; set; }

    /// <summary>Gets or sets the log message payload. This can be a plain text string or a structured object.</summary>
    public object? Message { get; set; }

    /// <summary>Gets or sets the child log entries nested under this entry.</summary>
    public IReadOnlyList<LogEntry>? Children { get; set; }

    /// <summary>Gets or sets whether this entry starts expanded (children shown). Default is <c>false</c> (collapsed).</summary>
    public bool Expanded { get; set; }
}
