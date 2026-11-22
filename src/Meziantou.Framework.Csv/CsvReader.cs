using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>Reads CSV data as UTF-16 text, one record at a time.</summary>
/// <remarks>
/// <para>
/// The values of the current record are slices of the internal buffer of the reader: the <see cref="CsvRecord"/> returned by <see cref="Current"/> and
/// the spans of its values are valid until the next call to <see cref="Read"/> or <see cref="ReadAsync"/>. Call <see cref="CsvField.ToString"/> to keep a value.
/// </para>
/// <para>To read UTF-8 data from a <see cref="Stream"/> or from bytes without decoding it first, use <see cref="Utf8CsvReader"/>.</para>
/// <para>This type is not thread-safe.</para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs" ref:region="CsvReader_Read" ref:dedent="true">
/// using var reader = CsvReader.CreateFromString("""
///     Name,Age
///     Alice,30
///     Bob,25
///     """);
///
/// var lines = new List&lt;string&gt;();
/// while (reader.Read())
/// {
///     var record = reader.Current;
///     lines.Add($"{record["Name"].Span} is {record["Age"].Parse&lt;int&gt;()} years old");
/// }
///
/// Assert.Equal(["Alice is 30 years old", "Bob is 25 years old"], lines);
/// </code>
/// </example>
public sealed class CsvReader : IDisposable, IAsyncDisposable
{
    private readonly CsvParser<char> _parser;
    private bool _hasCurrent;

    private CsvReader(CsvParser<char> parser)
    {
        _parser = parser;
    }

    /// <summary>Creates a reader that reads CSV data from a <see cref="TextReader"/>.</summary>
    /// <param name="reader">The reader to read the data from.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="reader"/> open when the <see cref="CsvReader"/> is disposed.</param>
    /// <returns>The new reader.</returns>
    public static CsvReader Create(TextReader reader, CsvReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new CsvReader(new CsvParser<char>(options ?? CsvReaderOptions.Default, new TextReaderCsvSource(reader, leaveOpen), default));
    }

    /// <summary>Creates a reader that reads CSV data from a <see cref="Stream"/>.</summary>
    /// <param name="stream">The stream to read the data from.</param>
    /// <param name="encoding">The encoding of the data, or <see langword="null"/> for UTF-8. A byte order mark at the start of the stream overrides it.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the <see cref="CsvReader"/> is disposed.</param>
    /// <returns>The new reader.</returns>
    /// <remarks>To read UTF-8 data without decoding it to UTF-16, use <see cref="Utf8CsvReader.Create(Stream, CsvReaderOptions?, bool)"/>.</remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The reader owns the StreamReader, which leaves the stream open as the caller asked")]
    public static CsvReader Create(Stream stream, Encoding? encoding = null, CsvReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: leaveOpen);
        return Create(reader, options, leaveOpen: false);
    }

    /// <summary>Creates a reader that reads CSV data from memory.</summary>
    /// <param name="csv">The CSV data.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <returns>The new reader.</returns>
    /// <remarks>The data is not copied: the values are slices of <paramref name="csv"/>.</remarks>
    public static CsvReader Create(ReadOnlyMemory<char> csv, CsvReaderOptions? options = null)
    {
        return new CsvReader(new CsvParser<char>(options ?? CsvReaderOptions.Default, source: null, csv));
    }

    /// <summary>Creates a reader that reads CSV data from a string.</summary>
    /// <param name="csv">The CSV data.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <returns>The new reader.</returns>
    public static CsvReader CreateFromString(string csv, CsvReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csv);
        return Create(csv.AsMemory(), options);
    }

    /// <summary>Creates a reader that reads CSV data from a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="encoding">The encoding of the file, or <see langword="null"/> for UTF-8. A byte order mark at the start of the file overrides it.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <returns>The new reader. Dispose it to close the file.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The reader owns the stream, and the method disposes it when it throws")]
    public static CsvReader CreateFromFile(string path, Encoding? encoding = null, CsvReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan | FileOptions.Asynchronous);
        try
        {
            return Create(stream, encoding, options, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Gets the names of the columns, read from the header row, or <see langword="null"/> when the data has no header row or when no record has been read yet.</summary>
    public CsvHeader? Header => _parser.Header;

    /// <summary>Gets the separator character, which is the detected one when <see cref="CsvReaderOptions.Separator"/> is <see langword="null"/>.</summary>
    /// <remarks>The separator is detected when the first record is read.</remarks>
    public char Separator => _parser.Separator;

    /// <summary>Gets the current record.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Read"/> or <see cref="ReadAsync"/> has not returned <see langword="true"/>.</exception>
    public CsvRecord Current
    {
        get
        {
            if (!_hasCurrent)
                throw new InvalidOperationException("There is no current record: call Read or ReadAsync first.");

            return new CsvRecord(_parser);
        }
    }

    /// <summary>Gets the 0-based index of the current record. The header row and the comment lines are not counted.</summary>
    public long RowIndex => _parser.RecordIndex;

    /// <summary>Gets the 1-based line number where the current record starts. Every line terminator counts, including those inside quoted values.</summary>
    public long LineNumber => _parser.LineNumber;

    /// <summary>Gets the 0-based position, in characters from the start of the data, where the current record starts.</summary>
    public long Position => _parser.Position;

    /// <summary>Reads the next record.</summary>
    /// <returns><see langword="true"/> when a record was read; <see langword="false"/> at the end of the data.</returns>
    /// <exception cref="CsvException">The data is invalid.</exception>
    public bool Read()
    {
        return _hasCurrent = _parser.Read();
    }

    /// <summary>Reads the next record asynchronously.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when a record was read; <see langword="false"/> at the end of the data.</returns>
    /// <exception cref="CsvException">The data is invalid.</exception>
    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        var task = _parser.ReadAsync(cancellationToken);
        if (task.IsCompletedSuccessfully)
        {
            var result = task.Result;
            _hasCurrent = result;
            return new ValueTask<bool>(result);
        }

        return ReadAsyncCore(task);
    }

    private async ValueTask<bool> ReadAsyncCore(ValueTask<bool> task)
    {
        _hasCurrent = false;
        return _hasCurrent = await task.ConfigureAwait(false);
    }

    /// <summary>Gets an enumerator that reads the records one by one, for use in a <see langword="foreach"/> loop.</summary>
    /// <returns>An enumerator over the records.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <inheritdoc/>
    public void Dispose()
    {
        _hasCurrent = false;
        _parser.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Enumerates the records of a <see cref="CsvReader"/>.</summary>
    public ref struct Enumerator
    {
        private readonly CsvReader _reader;

        internal Enumerator(CsvReader reader)
        {
            _reader = reader;
        }

        /// <summary>Gets the current record.</summary>
        public readonly CsvRecord Current => _reader.Current;

        /// <summary>Reads the next record.</summary>
        /// <returns><see langword="true"/> when a record was read; <see langword="false"/> at the end of the data.</returns>
        public readonly bool MoveNext() => _reader.Read();
    }
}
