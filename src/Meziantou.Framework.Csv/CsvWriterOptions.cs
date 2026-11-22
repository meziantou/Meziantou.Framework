namespace Meziantou.Framework.Csv;

/// <summary>Options that control how <see cref="CsvWriter"/> and <see cref="Utf8CsvWriter"/> write CSV data.</summary>
/// <remarks>The options are immutable. Use a <see langword="with"/> expression to create a copy with other values.</remarks>
public sealed record CsvWriterOptions
{
    internal const int DefaultBufferSize = 16 * 1024;

    /// <summary>Gets the default options: comma separator, double quote, <c>\r\n</c> line terminator, values quoted as needed.</summary>
    public static CsvWriterOptions Default { get; } = new();

    /// <summary>Gets the character that separates the values of a row. Default is <c>,</c>.</summary>
    /// <remarks><see cref="Utf8CsvWriter"/> requires an ASCII separator.</remarks>
    public char Separator { get; init; } = CsvReaderOptions.DefaultSeparator;

    /// <summary>Gets the character that quotes values. Default is <c>"</c>. When <see langword="null"/>, values are never quoted.</summary>
    /// <remarks><see cref="Utf8CsvWriter"/> requires an ASCII quote character.</remarks>
    public char? Quote { get; init; } = CsvReaderOptions.DefaultQuote;

    /// <summary>Gets the string written at the end of each row. Default is <c>\r\n</c>, as in RFC 4180.</summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>Gets when values are quoted. Default is <see cref="CsvQuoteMode.AsNeeded"/>.</summary>
    public CsvQuoteMode QuoteMode { get; init; }

    /// <summary>Gets a value indicating whether text values that a spreadsheet would run as a formula are escaped. Default is <see langword="false"/>.</summary>
    /// <remarks>
    /// When <see langword="true"/>, a text value that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return is prefixed with
    /// a single quote, which prevents CSV injection when the file is opened in a spreadsheet. Values written from numbers and other formattable values
    /// are not escaped.
    /// </remarks>
    public bool EscapeFormulas { get; init; }

    /// <summary>Gets the format provider used to format values. Default is <see langword="null"/>, which means <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public IFormatProvider? FormatProvider { get; init; }

    /// <summary>Gets the size, in characters or in bytes, of the buffer of the writer. Default is 16384.</summary>
    /// <remarks>The writer sends its buffer to the underlying writer, stream or buffer writer when it is mostly full at the end of a row, and when it is flushed.</remarks>
    public int BufferSize { get; init; } = DefaultBufferSize;

    internal static void Validate(CsvWriterOptions options, bool utf8)
    {
        var separator = options.Separator;
        if (separator is '\r' or '\n')
            throw new ArgumentException("The separator cannot be a line terminator.", nameof(options));

        if (separator == options.Quote)
            throw new ArgumentException("The separator and the quote character must be different.", nameof(options));

        if (utf8 && separator > 0x7F)
            throw new ArgumentException("The separator must be an ASCII character to write UTF-8 data.", nameof(options));

        if (options.Quote is { } quote)
        {
            if (quote is '\r' or '\n')
                throw new ArgumentException("The quote character cannot be a line terminator.", nameof(options));

            if (utf8 && quote > 0x7F)
                throw new ArgumentException("The quote character must be an ASCII character to write UTF-8 data.", nameof(options));
        }

        var newLine = options.NewLine;
        if (string.IsNullOrEmpty(newLine))
            throw new ArgumentException("The new line string cannot be empty.", nameof(options));

        if (newLine.Contains(separator, StringComparison.Ordinal) || (options.Quote is { } q && newLine.Contains(q, StringComparison.Ordinal)))
            throw new ArgumentException("The new line string cannot contain the separator or the quote character.", nameof(options));

        if (!Enum.IsDefined(options.QuoteMode))
            throw new ArgumentOutOfRangeException(nameof(options), options.QuoteMode, "The quote mode is not valid.");

        if (options.BufferSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.BufferSize, "The buffer size must be positive.");
    }
}
