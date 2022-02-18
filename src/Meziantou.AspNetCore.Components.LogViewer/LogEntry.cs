namespace Meziantou.AspNetCore.Components;

public class LogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public LogLevel LogLevel { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
}
