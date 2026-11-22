namespace Meziantou.Framework.Csv.Serialization;

/// <summary>The context of the conversion of the values of a column.</summary>
public sealed class CsvConverterContext
{
    /// <summary>Initializes a new instance of the <see cref="CsvConverterContext"/> class.</summary>
    /// <param name="options">The options of the serializer.</param>
    /// <param name="format">The format string of the column, or <see langword="null"/> for the default format.</param>
    /// <param name="memberName">The name of the member of the column, or <see langword="null"/>.</param>
    public CsvConverterContext(CsvSerializerOptions options, string? format = null, string? memberName = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Format = format;
        MemberName = memberName;
        FormatProvider = options.FormatProvider ?? CultureInfo.InvariantCulture;
    }

    /// <summary>Gets the options of the serializer.</summary>
    public CsvSerializerOptions Options { get; }

    /// <summary>Gets the format string set with <see cref="CsvFormatAttribute"/>, or <see langword="null"/> for the default format.</summary>
    public string? Format { get; }

    /// <summary>Gets the format provider: <see cref="CsvSerializerOptions.FormatProvider"/>, or <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public IFormatProvider FormatProvider { get; }

    /// <summary>Gets the name of the member of the column, or <see langword="null"/>.</summary>
    public string? MemberName { get; }
}
