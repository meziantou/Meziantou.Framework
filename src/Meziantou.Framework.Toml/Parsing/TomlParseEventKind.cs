namespace Meziantou.Framework.Toml.Parsing;

/// <summary>
/// Represents the semantic event kinds emitted by <see cref="TomlParser"/>.
/// </summary>
#pragma warning disable CA1720 // The members are named after the TOML value types
public enum TomlParseEventKind
{
    /// <summary>No event.</summary>
    None = 0,

    /// <summary>Document start.</summary>
    StartDocument = 1,

    /// <summary>Document end.</summary>
    EndDocument = 2,

    /// <summary>Table start.</summary>
    StartTable = 3,

    /// <summary>Table end.</summary>
    EndTable = 4,

    /// <summary>Property name.</summary>
    PropertyName = 5,

    /// <summary>Array start.</summary>
    StartArray = 6,

    /// <summary>Array end.</summary>
    EndArray = 7,

    /// <summary>String scalar value.</summary>
    String = 10,

    /// <summary>Integer scalar value.</summary>
    Integer = 11,

    /// <summary>Floating-point scalar value.</summary>
    Float = 12,

    /// <summary>Boolean scalar value.</summary>
    Boolean = 13,

    /// <summary>Date/time scalar value.</summary>
    DateTime = 14,
}
#pragma warning restore CA1720
