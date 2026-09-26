namespace Tomlyn.Serialization;

/// <summary>
/// Represents the current logical token when reading TOML.
/// </summary>
#pragma warning disable CA1720 // The members are named after the TOML value types
public enum TomlTokenType
{
    /// <summary>No token.</summary>
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
    /// <summary>String scalar.</summary>
    String = 10,
    /// <summary>Integer scalar.</summary>
    Integer = 11,
    /// <summary>Float scalar.</summary>
    Float = 12,
    /// <summary>Boolean scalar.</summary>
    Boolean = 13,
    /// <summary>Date/time scalar.</summary>
    DateTime = 14,
}
#pragma warning restore CA1720

