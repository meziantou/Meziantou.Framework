using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory;

/// <summary>Represents a log entry captured by an in-memory logger.</summary>
/// <example>
/// <code>
/// var logger = InMemoryLogger.CreateLogger("sample");
/// logger.LogInformation("User {UserId} logged in", 123);
/// 
/// var log = logger.Logs.Informations.Single();
/// Console.WriteLine(log.Message); // "User 123 logged in"
/// log.TryGetParameterValue("UserId", out var userId); // userId = 123
/// </code>
/// </example>
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

    /// <summary>Gets the timestamp when the log entry was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the logger category name.</summary>
    public string? Category { get; }

    /// <summary>Gets the log level.</summary>
    public LogLevel LogLevel { get; }

    /// <summary>Gets the event identifier.</summary>
    public EventId EventId { get; }

    /// <summary>Gets the list of scopes active when the log entry was created.</summary>
    public IReadOnlyList<object?> Scopes { get; }

    /// <summary>Gets the state object associated with the log entry.</summary>
    public object? State { get; }

    /// <summary>Gets the exception associated with the log entry.</summary>
    public Exception? Exception { get; }

    /// <summary>Gets the formatted log message.</summary>
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
            sb.Append("\n  => ").Append(Serialize(State));
        }

        foreach (var scope in Scopes)
        {
            sb.Append("\n  => ").Append(Serialize(scope));
        }

        return sb.ToString();
    }

    // ToString is the diagnostic surface of this type: it ends up in assertion messages and in the
    // debugger, so it must never throw. JsonSerializer rejects plenty of values that are perfectly
    // ordinary to log (Type, delegates, reference cycles), hence the fallback to the value's own
    // representation instead of letting the exception escape.
    private static string Serialize(object? value)
    {
        if (value is null)
            return "null";

        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (Exception)
        {
            try
            {
                return value.ToString() ?? value.GetType().ToString();
            }
            catch (Exception)
            {
                return value.GetType().ToString();
            }
        }
    }

    /// <summary>Tries to retrieve the first parameter value with the specified name from the state or scopes.</summary>
    /// <param name="name">The parameter name to search for.</param>
    /// <param name="value">When this method returns, contains the parameter value if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a parameter with the specified name was found; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>Gets all parameter values with the specified name from the state and scopes.</summary>
    /// <param name="name">The parameter name to search for.</param>
    /// <returns>An enumerable of all matching parameter values.</returns>
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

        // Most states and scopes (such as FormattedLogValues) are indexable, so avoid allocating an enumerator
        if (owner is IReadOnlyList<KeyValuePair<string, object?>> stateList)
        {
            for (var i = 0; i < stateList.Count; i++)
            {
                var item = stateList[i];
                if (string.Equals(name, item.Key, StringComparison.Ordinal))
                {
                    result = item.Value;
                    return true;
                }
            }
        }
        else if (owner is IEnumerable<KeyValuePair<string, object?>> stateDictionary)
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

        var property = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property is not null)
        {
            result = property.GetValue(owner);
            return true;
        }

        result = null;
        return false;
    }
}
