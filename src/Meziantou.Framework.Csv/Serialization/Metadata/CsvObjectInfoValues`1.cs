namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Describes a type to <see cref="CsvMetadataServices.CreateObjectInfo{T}"/>. Used by the source generator and by the reflection-based metadata.</summary>
/// <typeparam name="T">The type described.</typeparam>
public sealed class CsvObjectInfoValues<T>
{
    /// <summary>Gets the function that creates an instance with the parameterless constructor, or <see langword="null"/>.</summary>
    public Func<T>? ObjectCreator { get; init; }

    /// <summary>Gets the function that creates an instance with the values of the constructor parameters, or <see langword="null"/>.</summary>
    public Func<CsvConstructorArguments, T>? ObjectWithParameterizedConstructorCreator { get; init; }

    /// <summary>Gets the parameters of the constructor used by <see cref="ObjectWithParameterizedConstructorCreator"/>, ordered by position.</summary>
    public IReadOnlyList<CsvParameterInfoValues>? ConstructorParameters { get; init; }

    /// <summary>Gets the columns of the type, in declaration order, base types first.</summary>
    public required IReadOnlyList<CsvPropertyInfo> Properties { get; init; }
}
