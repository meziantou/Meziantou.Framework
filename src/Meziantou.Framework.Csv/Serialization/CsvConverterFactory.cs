namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Creates converters for a family of types, such as the enumerations or the generic instances of a type.</summary>
public abstract class CsvConverterFactory : CsvConverter
{
    /// <summary>Initializes a new instance of the <see cref="CsvConverterFactory"/> class.</summary>
    protected CsvConverterFactory()
    {
    }

    /// <inheritdoc/>
    public sealed override Type? TypeToConvert => null;

    /// <summary>Creates a converter for the specified type.</summary>
    /// <param name="typeToConvert">The type of the values, for which <see cref="CsvConverter.CanConvert(Type)"/> returned <see langword="true"/>.</param>
    /// <param name="options">The options of the serializer.</param>
    /// <returns>A <see cref="CsvConverter{T}"/> for <paramref name="typeToConvert"/>.</returns>
    public abstract CsvConverter? CreateConverter(Type typeToConvert, CsvSerializerOptions options);
}
