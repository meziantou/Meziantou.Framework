using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLogEntry
{
    internal InMemoryLogEntry(string? category, LogLevel logLevel, EventId eventId, IReadOnlyList<object?> scopes, object? state, Exception? exception, string message)
    {
        Category = category;
        LogLevel = logLevel;
        EventId = eventId;
        Scopes = scopes;
        State = state;
        Exception = exception;
        Message = message;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset CreatedAt { get; }
    public string? Category { get; }
    public LogLevel LogLevel { get; }
    public EventId EventId { get; }
    public IReadOnlyList<object?> Scopes { get; }
    public object? State { get; }
    public Exception? Exception { get; }
    public string Message { get; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Category != null)
        {
            sb.Append('[').Append(Category).Append("] ");
        }

        sb.Append(LogLevel);
        if (EventId != default)
        {
            sb.Append(" (");
            sb.Append(EventId.Id);
            sb.Append(' ');
            sb.Append(EventId.Name);
            sb.Append(')');
        }

        sb.Append(": ");
        sb.Append(Message);
        if (Exception != null)
        {
            sb.Append('\n').Append(Exception);
        }

        if (State != null)
        {
            sb.Append("\n  => ").Append(JsonSerializer.Serialize(State));
        }

        foreach (var scope in Scopes)
        {
            sb.Append("\n  => ").Append(JsonSerializer.Serialize(scope));
        }

        return sb.ToString();
    }
}
