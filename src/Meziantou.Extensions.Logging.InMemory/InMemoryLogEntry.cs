using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

public sealed class InMemoryLogEntry
{
    internal InMemoryLogEntry(DateTimeOffset createdAt, string? category, LogLevel logLevel, EventId eventId, IReadOnlyList<object?> scopes, object? state, Exception? exception, string message)
    {
        Category = category;
        LogLevel = logLevel;
        EventId = eventId;
        Scopes = scopes;
        State = state;
        Exception = exception;
        Message = message;
        CreatedAt = createdAt;
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
        if (Category is not null)
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
        if (Exception is not null)
        {
            sb.Append('\n').Append(Exception);
        }

        if (State is not null)
        {
            sb.Append("\n  => ").Append(JsonSerializer.Serialize(State));
        }

        foreach (var scope in Scopes)
        {
            sb.Append("\n  => ").Append(JsonSerializer.Serialize(scope));
        }

        return sb.ToString();
    }

    public bool TryGetParameterValue(string name, out object? value)
    {
        if (TryGetValue(State, name, out value))
            return true;

        foreach (var scope in Scopes)
        {
            if (TryGetValue(scope, name, out value))
                return true;
        }

        value = null;
        return false;
    }

    public IEnumerable<object?> GetAllParameterValues(string name)
    {
        if (TryGetValue(State, name, out var value))
            yield return value;

        foreach (var scope in Scopes)
        {
            if (TryGetValue(scope, name, out value))
                yield return value;
        }
    }

    private static bool TryGetValue(object? owner, string name, out object? result)
    {
        if (owner is null)
        {
            result = null;
            return false;
        }

        if (owner is IEnumerable<KeyValuePair<string, object?>> stateDictionary)
        {
            foreach (var item in stateDictionary)
            {
                if (string.Equals(name, item.Key, StringComparison.Ordinal))
                {
                    result = item.Value;
                    return true;
                }
            }
        }

        var property = owner.GetType().GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (property is not null)
        {
            result = property.GetValue(owner);
            return true;
        }

        result = null;
        return false;
    }
}
