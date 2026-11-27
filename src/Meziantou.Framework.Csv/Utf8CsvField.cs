using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>The value of a column of a <see cref="Utf8CsvRecord"/>. It is valid until the next record is read.</summary>
public readonly ref struct Utf8CsvField
{
    private readonly CsvParser<byte>? _parser;

    internal Utf8CsvField(CsvParser<byte> parser, int columnIndex, ReadOnlySpan<byte> value, ReadOnlySpan<byte> raw, bool isQuoted)
    {
        _parser = parser;
        ColumnIndex = columnIndex;
        Utf8Span = value;
        RawUtf8Span = raw;
        IsQuoted = isQuoted;
    }

    /// <summary>Gets the 0-based index of the column.</summary>
    public int ColumnIndex { get; }

    /// <summary>Gets the UTF-8 bytes of the value, without its quotes, with escaped quotes unescaped and trimmed as <see cref="CsvReaderOptions.Trim"/> says.</summary>
    public ReadOnlySpan<byte> Utf8Span { get; }

    /// <summary>Gets the UTF-8 bytes of the value, as they are in the data.</summary>
    public ReadOnlySpan<byte> RawUtf8Span { get; }

    /// <summary>Gets a value indicating whether the value is quoted in the data.</summary>
    public bool IsQuoted { get; }

    /// <summary>Gets a value indicating whether the value is empty.</summary>
    public bool IsEmpty => Utf8Span.IsEmpty;

    /// <summary>Gets the value as a string. Invalid UTF-8 sequences are replaced with U+FFFD.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => _parser is null ? "" : _parser.GetString(Utf8Span);

    /// <summary>Parses the UTF-8 bytes of the value with <see cref="CsvReaderOptions.FormatProvider"/>.</summary>
    /// <typeparam name="T">The type to parse the value to.</typeparam>
    /// <returns>The parsed value.</returns>
    /// <exception cref="CsvException">The value is not valid for <typeparamref name="T"/>.</exception>
    /// <remarks>Use <see cref="ParseUtf16{T}"/> for the types that cannot parse UTF-8, such as <see cref="DateTime"/>.</remarks>
    public T Parse<T>()
        where T : IUtf8SpanParsable<T>
    {
        if (TryParse<T>(out var result))
            return result;

        throw CreateParseException(typeof(T));
    }

    /// <summary>Tries to parse the UTF-8 bytes of the value with <see cref="CsvReaderOptions.FormatProvider"/>.</summary>
    /// <typeparam name="T">The type to parse the value to.</typeparam>
    /// <param name="result">The parsed value.</param>
    /// <returns><see langword="true"/> when the value is valid for <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryParse<T>([MaybeNullWhen(false)] out T result)
        where T : IUtf8SpanParsable<T>
    {
        var formatProvider = GetFormatProvider();
        if (InvariantFloatingPoint.IsSupported<T>() && InvariantNumbers.IsInvariant(formatProvider) && InvariantFloatingPoint.TryParse(Utf8Span, out result))
            return true;

        return T.TryParse(Utf8Span, formatProvider, out result);
    }

    /// <summary>Decodes the value to UTF-16 and parses it with <see cref="CsvReaderOptions.FormatProvider"/>.</summary>
    /// <typeparam name="T">The type to parse the value to.</typeparam>
    /// <returns>The parsed value.</returns>
    /// <exception cref="CsvException">The value is not valid for <typeparamref name="T"/>.</exception>
    public T ParseUtf16<T>()
        where T : ISpanParsable<T>
    {
        if (TryParseUtf16<T>(out var result))
            return result;

        throw CreateParseException(typeof(T));
    }

    /// <summary>Decodes the value to UTF-16 and tries to parse it with <see cref="CsvReaderOptions.FormatProvider"/>.</summary>
    /// <typeparam name="T">The type to parse the value to.</typeparam>
    /// <param name="result">The parsed value.</param>
    /// <returns><see langword="true"/> when the value is valid for <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryParseUtf16<T>([MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T>
    {
        Span<char> buffer = unsafe(stackalloc char[Utf8Transcoder.StackBufferSize]);
        var chars = Utf8Transcoder.Decode(Utf8Span, buffer, out var rented);
        try
        {
            return T.TryParse(chars, GetFormatProvider(), out result);
        }
        finally
        {
            Utf8Transcoder.Return(rented);
        }
    }

    /// <summary>Parses the value as the name or the number of a member of an enumeration.</summary>
    /// <typeparam name="TEnum">The type of the enumeration.</typeparam>
    /// <param name="ignoreCase"><see langword="true"/> to ignore the case of the name.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="CsvException">The value is not valid for <typeparamref name="TEnum"/>.</exception>
    public TEnum ParseEnum<TEnum>(bool ignoreCase = true)
        where TEnum : struct, Enum
    {
        Span<char> buffer = unsafe(stackalloc char[Utf8Transcoder.StackBufferSize]);
        var chars = Utf8Transcoder.Decode(Utf8Span, buffer, out var rented);
        try
        {
            if (Enum.TryParse<TEnum>(chars, ignoreCase, out var result))
                return result;
        }
        finally
        {
            Utf8Transcoder.Return(rented);
        }

        throw CreateParseException(typeof(TEnum));
    }

    private IFormatProvider GetFormatProvider() => _parser?.FormatProvider ?? CultureInfo.InvariantCulture;

    private CsvException CreateParseException(Type type)
    {
        var message = $"The value '{Encoding.UTF8.GetString(Utf8Span)}' is not a valid {type.Name}.";
        return _parser is null ? new CsvException(message) : _parser.CreateException(message, ColumnIndex);
    }
}
