namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Converts a value of a column to or from text. Derive from <see cref="CsvConverter{T}"/> or <see cref="CsvConverterFactory"/>.</summary>
public abstract class CsvConverter
{
    private protected CsvConverter()
    {
    }

    /// <summary>Gets the type the converter converts, or <see langword="null"/> for a <see cref="CsvConverterFactory"/>.</summary>
    public abstract Type? TypeToConvert { get; }

    /// <summary>Determines whether the converter converts values of the specified type.</summary>
    /// <param name="typeToConvert">The type of the values.</param>
    /// <returns><see langword="true"/> when the converter converts values of <paramref name="typeToConvert"/>; otherwise, <see langword="false"/>.</returns>
    public abstract bool CanConvert(Type typeToConvert);
}
