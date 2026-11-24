namespace Meziantou.Framework.Csv;

/// <summary>Options that control how <see cref="CsvReader"/> and <see cref="Utf8CsvReader"/> read CSV data.</summary>
/// <remarks>The options are immutable. Use a <see langword="with"/> expression to create a copy with other values.</remarks>
public sealed record CsvReaderOptions
{
    /// <summary>The default separator character (comma).</summary>
    public const char DefaultSeparator = ',';

    /// <summary>The default quote character (double quote).</summary>
    public const char DefaultQuote = '"';

    internal const int DefaultBufferSize = 16 * 1024;
    internal const int DefaultMaxRowLength = 16 * 1024 * 1024;

    /// <summary>Gets the default options: comma separator, double quote, a header row.</summary>
    public static CsvReaderOptions Default { get; } = new();

    /// <summary>Gets the character that separates the values of a row. Default is <c>,</c>.</summary>
    /// <remarks>
    /// When <see langword="null"/>, the separator is detected from the first line of the data: the most frequent character among <c>,</c>, <c>;</c>,
    /// tab and <c>|</c> outside quoted values, or <c>,</c> when the line contains none of them.
    /// <see cref="Utf8CsvReader"/> requires an ASCII separator.
    /// </remarks>
    public char? Separator { get; init; } = DefaultSeparator;

    /// <summary>Gets the character that quotes values. Default is <c>"</c>. When <see langword="null"/>, quote characters are part of the values.</summary>
    /// <remarks><see cref="Utf8CsvReader"/> requires an ASCII quote character.</remarks>
    public char? Quote { get; init; } = DefaultQuote;

    /// <summary>Gets a value indicating whether the first row holds the names of the columns. Default is <see langword="true"/>.</summary>
    /// <remarks>The header row is not returned as a record; the names are available from the <c>Header</c> property of the reader once the first record is read.</remarks>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>Gets which spaces are removed from the values. Default is <see cref="CsvTrimOptions.None"/>.</summary>
    public CsvTrimOptions Trim { get; init; }

    /// <summary>Gets the character that starts a comment line, or <see langword="null"/> when the data has no comments. Default is <see langword="null"/>.</summary>
    /// <remarks>A line is a comment when its first character is this character. Comment lines are skipped, including before the header row.</remarks>
    public char? CommentCharacter { get; init; }

    /// <summary>Gets a value indicating whether empty lines are skipped. Default is <see langword="false"/>: an empty line is a record without columns.</summary>
    public bool SkipEmptyLines { get; init; }

    /// <summary>Gets a value indicating whether every record must have the same number of columns as the header row, or as the first record when there is no header row.</summary>
    /// <remarks>A record with a different number of columns throws a <see cref="CsvException"/>, unless <see cref="BadDataHandler"/> decides otherwise.</remarks>
    public bool ValidateColumnCount { get; init; }

    /// <summary>Gets the comparer used to find a column by name. Default is <see cref="StringComparer.Ordinal"/>.</summary>
    public IEqualityComparer<string> ColumnNameComparer { get; init; } = StringComparer.Ordinal;

    /// <summary>Gets the format provider used to parse values. Default is <see langword="null"/>, which means <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public IFormatProvider? FormatProvider { get; init; }

    /// <summary>Gets a value indicating whether invalid data throws a <see cref="CsvException"/>. Default is <see langword="false"/>.</summary>
    /// <remarks>
    /// In strict mode, a quote character inside a value that is not quoted, a character other than a separator or a line terminator after the closing quote
    /// of a value, and a quoted value that does not end before the end of the data are errors.
    /// Otherwise, a quote character inside a value that is not quoted is part of the value, the characters after a closing quote are part of the value, and an
    /// unterminated quoted value ends at the end of the data.
    /// When <see cref="BadDataHandler"/> is set, it decides what happens to these errors, and this property is not used.
    /// </remarks>
    public bool Strict { get; init; }

    /// <summary>Gets the method called for malformed data, which decides whether the reader throws, accepts the data, or skips the record. Default is <see langword="null"/>.</summary>
    /// <remarks>
    /// <para>
    /// The handler is called for the errors that <see cref="Strict"/> reports (see <see cref="CsvBadDataKind"/>), whatever the value of <see cref="Strict"/>,
    /// and for a record with an unexpected number of columns when <see cref="ValidateColumnCount"/> is <see langword="true"/>.
    /// When it is <see langword="null"/>, <see cref="Strict"/> decides for the quoting errors: the reader throws in strict mode and accepts the data otherwise.
    /// A record with an unexpected number of columns throws whenever <see cref="ValidateColumnCount"/> is <see langword="true"/>, whatever the value of
    /// <see cref="Strict"/>.
    /// </para>
    /// <para>
    /// The handler is called in the order of the data, once every record before the malformed one has been read, and before the reader returns the malformed
    /// record. A record can have several errors, reported one by one until the handler returns <see cref="CsvBadDataAction.Skip"/>.
    /// The handler must not call the methods of the reader. It may throw its own exception, which ends the reading.
    /// </para>
    /// <para>
    /// The handler does not change how the data is read when it is valid, and it is not called for a row longer than <see cref="MaxRowLength"/>, which always
    /// throws. The quoting errors of such a row can be reported before the reader finds that the row is too long.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs" ref:region="CsvReader_BadDataHandler" ref:dedent="true">
    /// var errors = new List&lt;string&gt;();
    /// var options = new CsvReaderOptions
    /// {
    ///     BadDataHandler = context =&gt;
    ///     {
    ///         errors.Add($"line {context.LineNumber}, column {context.ColumnName}: {context.Message}");
    ///         return CsvBadDataAction.Skip; // or Accept to read the value as is, or Throw
    ///     },
    /// };
    ///
    /// using var reader = CsvReader.CreateFromString("Name,Comment\nAlice,ok\nBob,\"quoted\" text\nCharlie,ok", options);
    /// var names = new List&lt;string&gt;();
    /// while (reader.Read())
    /// {
    ///     names.Add(reader.Current["Name"].ToString());
    /// }
    ///
    /// Assert.Equal(["Alice", "Charlie"], names);
    /// Assert.Equal(["line 3, column Comment: A quoted value is followed by characters that are not a separator."], errors);
    /// </code>
    /// </example>
    public CsvBadDataHandler? BadDataHandler { get; init; }

    /// <summary>Gets a value indicating whether the strings of short values are reused instead of allocated again. Default is <see langword="false"/>.</summary>
    /// <remarks>Each reader keeps its own pool of strings of at most 32 characters. This reduces the allocations when the data repeats the same values, such as categories or flags.</remarks>
    public bool PoolStrings { get; init; }

    /// <summary>Gets the initial size, in characters or in bytes, of the buffer of the reader. Default is 16384.</summary>
    /// <remarks>The buffer grows when a row does not fit, up to <see cref="MaxRowLength"/>.</remarks>
    public int BufferSize { get; init; } = DefaultBufferSize;

    /// <summary>Gets the maximum length, in characters or in bytes, of a row read from a <see cref="TextReader"/> or a <see cref="Stream"/>. Default is 16777216.</summary>
    /// <remarks>A longer row throws a <see cref="CsvException"/>. This limits the memory used by malformed data, such as a quote that is never closed.</remarks>
    public int MaxRowLength { get; init; } = DefaultMaxRowLength;

    // Number of elements processed per SIMD block: 0 for the scalar path, 16, 32 or 64. -1 selects the widest accelerated path. Used by tests to cover every path.
    internal int VectorSize { get; init; } = -1;

    internal static void Validate(CsvReaderOptions options, bool utf8)
    {
        if (options.Separator is { } separator)
        {
            if (separator is '\r' or '\n')
                throw new ArgumentException("The separator cannot be a line terminator.", nameof(options));

            if (separator == options.Quote)
                throw new ArgumentException("The separator and the quote character must be different.", nameof(options));

            if (utf8 && separator > 0x7F)
                throw new ArgumentException("The separator must be an ASCII character to read UTF-8 data.", nameof(options));
        }

        if (options.Quote is { } quote)
        {
            if (quote is '\r' or '\n')
                throw new ArgumentException("The quote character cannot be a line terminator.", nameof(options));

            if (utf8 && quote > 0x7F)
                throw new ArgumentException("The quote character must be an ASCII character to read UTF-8 data.", nameof(options));
        }

        if (options.CommentCharacter is { } comment)
        {
            if (comment is '\r' or '\n')
                throw new ArgumentException("The comment character cannot be a line terminator.", nameof(options));

            if (utf8 && comment > 0x7F)
                throw new ArgumentException("The comment character must be an ASCII character to read UTF-8 data.", nameof(options));
        }

        if (options.BufferSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.BufferSize, "The buffer size must be positive.");

        if (options.MaxRowLength < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxRowLength, "The maximum row length must be positive.");

        if (options.ColumnNameComparer is null)
            throw new ArgumentException("The column name comparer cannot be null.", nameof(options));
    }
}
