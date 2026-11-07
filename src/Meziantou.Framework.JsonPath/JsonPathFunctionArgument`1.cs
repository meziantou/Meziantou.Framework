namespace Meziantou.Framework.Json;

/// <summary>A typed function argument or result retaining native navigator values without JSON conversion.</summary>
/// <typeparam name="TValue">The navigator's node type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factories create a discriminated value of the navigator's node type.")]
public readonly struct JsonPathFunctionArgument<TValue>
{
    private readonly TValue? _node;
    private readonly object? _scalar;
    private readonly bool _logical;
    private readonly IReadOnlyList<TValue?>? _nodes;

    private JsonPathFunctionArgument(JsonPathFunctionType type, bool isNothing = false, bool isNode = false, TValue? node = default, object? scalar = null, bool logical = false, IReadOnlyList<TValue?>? nodes = null)
    {
        Type = type;
        IsNothing = isNothing;
        IsNode = isNode;
        _node = node;
        _scalar = scalar;
        _logical = logical;
        _nodes = nodes;
    }

    /// <summary>Gets the RFC function type.</summary>
    public JsonPathFunctionType Type { get; }

    /// <summary>Gets whether this value is Nothing, rather than JSON null.</summary>
    public bool IsNothing { get; }

    /// <summary>Gets whether this value is a native navigator node rather than a literal.</summary>
    public bool IsNode { get; }

    /// <summary>Gets the native node. Throws when this is not a native value.</summary>
    public TValue? Node => IsNode ? _node : throw new InvalidOperationException("The argument is not a native node.");

    /// <summary>Gets the logical result. Throws for other argument types.</summary>
    public bool Logical => Type is JsonPathFunctionType.Logical ? _logical : throw new InvalidOperationException("The argument is not logical.");

    /// <summary>Gets the ordered node list. Throws for other argument types.</summary>
    public IReadOnlyList<TValue?> Nodes => Type is JsonPathFunctionType.Nodes ? _nodes! : throw new InvalidOperationException("The argument is not a node list.");

    /// <summary>Gets the JSON kind of a value, or null for Nothing. Throws for other argument types.</summary>
    public JsonPathNodeKind? GetValueKind(JsonPathNavigator<TValue> navigator)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        if (Type is not JsonPathFunctionType.Value)
        {
            throw new InvalidOperationException("The argument is not a value.");
        }

        if (IsNothing)
        {
            return null;
        }

        if (IsNode)
        {
            return navigator.GetKind(_node);
        }

        return _scalar switch
        {
            string => JsonPathNodeKind.String,
            double => JsonPathNodeKind.Number,
            bool => JsonPathNodeKind.Boolean,
            _ => JsonPathNodeKind.Null,
        };
    }

    /// <summary>Reads a string from a literal or native value.</summary>
    public bool TryGetString(JsonPathNavigator<TValue> navigator, [NotNullWhen(true)] out string? value)
    {
        if (GetValueKind(navigator) is JsonPathNodeKind.String)
        {
            if (IsNode)
            {
                return navigator.TryGetString(_node, out value) && value is not null;
            }

            value = (string)_scalar!;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Reads a number from a literal or native value.</summary>
    public bool TryGetNumber(JsonPathNavigator<TValue> navigator, out double value)
    {
        if (GetValueKind(navigator) is JsonPathNodeKind.Number)
        {
            if (IsNode)
            {
                return navigator.TryGetNumber(_node, out value);
            }

            value = (double)_scalar!;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Reads a boolean from a literal or native value.</summary>
    public bool TryGetBoolean(JsonPathNavigator<TValue> navigator, out bool value)
    {
        if (GetValueKind(navigator) is JsonPathNodeKind.Boolean)
        {
            if (IsNode)
            {
                return navigator.TryGetBoolean(_node, out value);
            }

            value = (bool)_scalar!;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Creates Nothing, distinct from JSON null.</summary>
    public static JsonPathFunctionArgument<TValue> Nothing => new(JsonPathFunctionType.Value, isNothing: true);

    /// <summary>Creates JSON null. A default argument also represents JSON null.</summary>
    public static JsonPathFunctionArgument<TValue> Null => default;

    /// <summary>Creates a value from a native node, including a native JSON null.</summary>
    public static JsonPathFunctionArgument<TValue> FromNode(TValue? node) => new(JsonPathFunctionType.Value, isNode: true, node: node);

    /// <summary>Creates a string value.</summary>
    public static JsonPathFunctionArgument<TValue> FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(JsonPathFunctionType.Value, scalar: value);
    }

    /// <summary>Creates a number using the evaluator's double-precision representation.</summary>
    public static JsonPathFunctionArgument<TValue> FromNumber(double value) => new(JsonPathFunctionType.Value, scalar: value);

    /// <summary>Creates a JSON boolean value (not LogicalType).</summary>
    public static JsonPathFunctionArgument<TValue> FromBoolean(bool value) => new(JsonPathFunctionType.Value, scalar: value);

    /// <summary>Creates a logical result (not a JSON boolean).</summary>
    public static JsonPathFunctionArgument<TValue> FromLogical(bool value) => new(JsonPathFunctionType.Logical, logical: value);

    /// <summary>Creates a node list by copying its membership and preserving order and duplicates.</summary>
    public static JsonPathFunctionArgument<TValue> FromNodes(IEnumerable<TValue?> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return new(JsonPathFunctionType.Nodes, nodes: Array.AsReadOnly(nodes.ToArray()));
    }
}
