namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Describes a constructor parameter. Used by the source generator and by the reflection-based metadata.</summary>
public sealed class CsvParameterInfoValues
{
    /// <summary>Gets the name of the parameter.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the type of the parameter.</summary>
    public required Type ParameterType { get; init; }

    /// <summary>Gets the 0-based position of the parameter.</summary>
    public required int Position { get; init; }

    /// <summary>Gets a value indicating whether the parameter has a default value, used when its column is missing.</summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>Gets the default value of the parameter.</summary>
    public object? DefaultValue { get; init; }
}
