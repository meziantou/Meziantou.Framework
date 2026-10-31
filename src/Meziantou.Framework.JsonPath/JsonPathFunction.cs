namespace Meziantou.Framework.Json;

/// <summary>Defines a custom filter function that works with any JSONPath navigator.</summary>
/// <remarks>
/// Implementations must be thread-safe and should be pure. Exceptions propagate to the caller.
/// Callbacks are not cached; their invocation count is not guaranteed. Do not mutate input nodes.
/// </remarks>
public abstract class JsonPathFunction
{
    /// <summary>Initializes a function with an immutable RFC 9535 signature.</summary>
    protected JsonPathFunction(string name, JsonPathFunctionType resultType, params JsonPathFunctionType[] parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(parameterTypes);
        if (name.Length is 0 || name[0] is not (>= 'a' and <= 'z') ||
            name.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))
        {
            throw new ArgumentException("Function names must match [a-z][a-z0-9_]*.", nameof(name));
        }

        ValidateType(resultType);
        foreach (var type in parameterTypes)
        {
            ValidateType(type);
        }

        Name = name;
        ResultType = resultType;
        ParameterTypes = Array.AsReadOnly((JsonPathFunctionType[])parameterTypes.Clone());
    }

    /// <summary>Gets the function name.</summary>
    public string Name { get; }

    /// <summary>Gets the result type.</summary>
    public JsonPathFunctionType ResultType { get; }

    /// <summary>Gets the immutable parameter types.</summary>
    public IReadOnlyList<JsonPathFunctionType> ParameterTypes { get; }

    /// <summary>Invokes the function with arguments converted according to its declared signature.</summary>
    /// <remarks>Return a result of <see cref="ResultType"/>. Nodes retain their original object model and lifetime.</remarks>
    public abstract JsonPathFunctionArgument<TValue> Invoke<TValue>(IReadOnlyList<JsonPathFunctionArgument<TValue>> arguments, JsonPathNavigator<TValue> navigator);

    private static void ValidateType(JsonPathFunctionType type)
    {
        if (type is not (JsonPathFunctionType.Value or JsonPathFunctionType.Logical or JsonPathFunctionType.Nodes))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
}
