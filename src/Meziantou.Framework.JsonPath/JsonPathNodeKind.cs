namespace Meziantou.Framework.Json;

/// <summary>Represents the JSON node kind exposed by a <see cref="JsonPathNavigator{TValue}"/>.</summary>
#pragma warning disable CA1720
public enum JsonPathNodeKind
{
    /// <summary>A JSON null value.</summary>
    Null,

    /// <summary>A JSON boolean value.</summary>
    Boolean,

    /// <summary>A JSON number value.</summary>
    Number,

    /// <summary>A JSON string value.</summary>
    String,

    /// <summary>A JSON array value.</summary>
    Array,

    /// <summary>A JSON object value.</summary>
    Object,
}
#pragma warning restore CA1720
