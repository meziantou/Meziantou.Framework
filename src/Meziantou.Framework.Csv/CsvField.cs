using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>The value of a column of a <see cref="CsvRecord"/>. It is valid until the next record is read.</summary>
public readonly ref struct CsvField
{
    private readonly CsvParser<char>? _parser;

    internal CsvField(CsvParser<char> parser, int columnIndex, ReadOnlySpan<char> value, ReadOnlySpan<char> raw, bool isQuoted)
    {
        _parser = parser;
        ColumnIndex = columnIndex;
        Span = value;
        RawSpan = raw;
        IsQuoted = isQuoted;
    }

    /// <summary>Gets the 0-based index of the column.</summary>
    public int ColumnIndex { get; }

    /// <summary>Gets the value, without its quotes, with escaped quotes unescaped and trimmed as <see cref="CsvReaderOptions.Trim"/> says.</summary>
    public ReadOnlySpan<char> Span { get; }

    /// <summary>Gets the text of the value, as it is in the data.</summary>
    public ReadOnlySpan<char> RawSpan { get; }

    /// <summary>Gets a value indicating whether the value is quoted in the data.</summary>
    public bool IsQuoted { get; }

    /// <summary>Gets a value indicating whether the value is empty.</summary>
    public bool IsEmpty => Span.IsEmpty;

    /// <summary>Gets the value as a string.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => _parser is null ? "" : _parser.GetString(Span);

    /// <summary>Parses the value with <see cref="CsvReaderOptions.FormatProvider"/>.</summary>
    /// <typeparam name="T">The type to parse the value to.</typeparam>
    /// <returns>The parsed value.</returns>
    /// <exception cref="CsvException">The value is not valid for <typeparamref name="T"/>.</exception>
    public T Parse<T>()
        where T : ISpanParsable<T>
    {
        if (TryParse<T>(out var result))
            return result;

        throw CreateParseException(typeof(T));
    }

    /// <summary>Tries to parse the value with <see cref="CsvReaderOptions.FormatProvider"/>.</summary>
    /// <typeparam name="T">The type to parse the value to.</typeparam>
    /// <param name="result">The parsed value.</param>
    /// <returns><see langword="true"/> when the value is valid for <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryParse<T>([MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T>
    {
        var formatProvider = GetFormatProvider();
        if (InvariantFloatingPoint.IsSupported<T>() && InvariantNumbers.IsInvariant(formatProvider) && InvariantFloatingPoint.TryParse(Span, out result))
            return true;

        return T.TryParse(Span, formatProvider, out result);
    }

    /// <summary>Parses the value as the name or the number of a member of an enumeration.</summary>
    /// <typeparam name="TEnum">The type of the enumeration.</typeparam>
    /// <param name="ignoreCase"><see langword="true"/> to ignore the case of the name.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="CsvException">The value is not valid for <typeparamref name="TEnum"/>.</exception>
    public TEnum ParseEnum<TEnum>(bool ignoreCase = true)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(Span, ignoreCase, out var result))
            return result;

        throw CreateParseException(typeof(TEnum));
    }

    private IFormatProvider GetFormatProvider() => _parser?.FormatProvider ?? CultureInfo.InvariantCulture;

    private CsvException CreateParseException(Type type)
    {
        var message = $"The value '{Span}' is not a valid {type.Name}.";
        return _parser is null ? new CsvException(message) : _parser.CreateException(message, ColumnIndex);
    }
}
