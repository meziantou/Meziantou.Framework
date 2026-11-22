namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Describes a column to <see cref="CsvMetadataServices.CreatePropertyInfo{TDeclaring, TValue}"/>. Used by the source generator and by the reflection-based metadata.</summary>
/// <typeparam name="TDeclaring">The type that declares the member.</typeparam>
/// <typeparam name="TValue">The type of the member.</typeparam>
public sealed class CsvPropertyInfoValues<TDeclaring, TValue>
{
    /// <summary>Gets the name of the member.</summary>
    public required string MemberName { get; init; }

    /// <summary>Gets the name of the column set with <see cref="CsvPropertyNameAttribute"/>, or <see langword="null"/> to apply <see cref="CsvSerializerOptions.PropertyNamingPolicy"/> to <see cref="MemberName"/>.</summary>
    public string? ColumnName { get; init; }

    /// <summary>Gets the order set with <see cref="CsvPropertyOrderAttribute"/>.</summary>
    public int Order { get; init; }

    /// <summary>Gets the index set with <see cref="CsvColumnIndexAttribute"/>, or <see langword="null"/>.</summary>
    public int? ColumnIndex { get; init; }

    /// <summary>Gets a value indicating whether the column is required.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Gets the function that gets the value of the member, or <see langword="null"/> when the column is not written.</summary>
    public Func<TDeclaring, TValue>? Getter { get; init; }

    /// <summary>Gets the function that sets the value of the member, or <see langword="null"/> when the member is set by the constructor or is not read.</summary>
    public CsvPropertySetter<TDeclaring, TValue>? Setter { get; init; }

    /// <summary>Gets the converter set with <see cref="CsvConverterAttribute"/>, or <see langword="null"/>.</summary>
    public CsvConverter? Converter { get; init; }

    /// <summary>Gets the converter to use when neither <see cref="Converter"/> nor <see cref="CsvSerializerOptions.Converters"/> converts <typeparamref name="TValue"/>, or <see langword="null"/> for the built-in converters.</summary>
    public CsvConverter<TValue>? DefaultConverter { get; init; }

    /// <summary>Gets the format string set with <see cref="CsvFormatAttribute"/>, or <see langword="null"/>.</summary>
    public string? Format { get; init; }

    /// <summary>Gets the position of the constructor parameter that receives the value of the column, or -1.</summary>
    public int ParameterIndex { get; init; } = -1;
}
