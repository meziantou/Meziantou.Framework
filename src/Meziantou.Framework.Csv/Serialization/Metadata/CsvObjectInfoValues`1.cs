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

    /// <summary>Gets the method that sets the members from a record of UTF-16 data, or <see langword="null"/> to set them with <see cref="CsvPropertyInfoValues{TDeclaring, TValue}.Setter"/>.</summary>
    /// <remarks>
    /// The source generator writes it, so that the value of each member is converted and set without a virtual call, and its converter can be
    /// inlined. It is used when the columns of the members it sets are in the order of <see cref="Properties"/>, and there is no
    /// <see cref="CsvSerializerOptions.DeserializationErrorHandler"/>: the members are then set in the order of the columns, and an error throws.
    /// </remarks>
    public CsvMembersReader<T>? MembersReader { get; init; }

    /// <summary>Gets the method that sets the members from a record of UTF-8 data, or <see langword="null"/>. See <see cref="MembersReader"/>.</summary>
    public Utf8CsvMembersReader<T>? Utf8MembersReader { get; init; }
}
