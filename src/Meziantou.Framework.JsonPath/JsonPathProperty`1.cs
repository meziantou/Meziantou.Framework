namespace Meziantou.Framework.Json;

/// <summary>Represents an object property exposed by a <see cref="JsonPathNavigator{TValue}"/>.</summary>
/// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
public readonly struct JsonPathProperty<TValue>
{
    /// <summary>Initializes a new instance of the <see cref="JsonPathProperty{TValue}"/> struct.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The property value.</param>
    public JsonPathProperty(string name, TValue? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        Value = value;
    }

    /// <summary>Gets the property name.</summary>
    public string Name { get; }

    /// <summary>Gets the property value. May be <see langword="null"/> when the navigator represents JSON <c>null</c> as <see langword="null"/>.</summary>
    public TValue? Value { get; }
}
