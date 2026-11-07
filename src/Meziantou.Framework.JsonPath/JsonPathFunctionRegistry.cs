namespace Meziantou.Framework.Json;

/// <summary>An immutable collection of opt-in filter functions, in addition to the five built-ins.</summary>
public sealed class JsonPathFunctionRegistry
{
    private readonly Dictionary<string, JsonPathFunction> _functions;

    private JsonPathFunctionRegistry(Dictionary<string, JsonPathFunction> functions) => _functions = functions;

    /// <summary>Gets the registry with only RFC 9535 built-ins.</summary>
    public static JsonPathFunctionRegistry Default { get; } = new(new(StringComparer.Ordinal));

    /// <summary>Returns a new registry containing the function. Duplicate names and built-in overrides are rejected.</summary>
    public JsonPathFunctionRegistry WithFunction(JsonPathFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (function.Name is "length" or "count" or "match" or "search" or "value" || _functions.ContainsKey(function.Name))
        {
            throw new ArgumentException($"Function '{function.Name}' is already registered.", nameof(function));
        }

        var functions = new Dictionary<string, JsonPathFunction>(_functions, StringComparer.Ordinal)
        {
            [function.Name] = function,
        };
        return new JsonPathFunctionRegistry(functions);
    }

    internal JsonPathFunction? GetFunction(string name) => _functions.GetValueOrDefault(name);
}
