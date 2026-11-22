using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>Reads UTF-8 encoded CSV data, one record at a time, without decoding it to UTF-16.</summary>
/// <remarks>
/// <para>
/// The values of the current record are slices of the internal buffer of the reader: the <see cref="Utf8CsvRecord"/> returned by <see cref="Current"/>
/// and the spans of its values are valid until the next call to <see cref="Read"/> or <see cref="ReadAsync"/>. Call <see cref="Utf8CsvField.ToString"/> to keep a value.
/// </para>
/// <para>The separator, the quote and the comment characters must be ASCII characters. A UTF-8 byte order mark at the start of the data is skipped.</para>
/// <para>This type is not thread-safe.</para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs" ref:region="Utf8CsvReader_Read" ref:dedent="true">
/// using var stream = new MemoryStream("Name,Age\nAlice,30\nBob,25"u8.ToArray());
/// using var reader = Utf8CsvReader.Create(stream);
///
/// var totalAge = 0;
/// var names = new List&lt;string&gt;();
/// foreach (var record in reader)
/// {
///     names.Add(record["Name"].ToString());
///     totalAge += record["Age"].Parse&lt;int&gt;(); // Parses the UTF-8 bytes, without decoding them
/// }
///
/// Assert.Equal(["Alice", "Bob"], names);
/// Assert.Equal(55, totalAge);
/// </code>
/// </example>
public sealed class Utf8CsvReader : IDisposable, IAsyncDisposable
{
    private readonly CsvParser<byte> _parser;
    private bool _hasCurrent;

    private Utf8CsvReader(CsvParser<byte> parser)
    {
        _parser = parser;
    }

    /// <summary>Creates a reader that reads UTF-8 CSV data from a <see cref="Stream"/>.</summary>
    /// <param name="stream">The stream to read the data from.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the <see cref="Utf8CsvReader"/> is disposed.</param>
    /// <returns>The new reader.</returns>
    public static Utf8CsvReader Create(Stream stream, CsvReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new Utf8CsvReader(new CsvParser<byte>(options ?? CsvReaderOptions.Default, new StreamCsvSource(stream, leaveOpen), default));
    }

    /// <summary>Creates a reader that reads UTF-8 CSV data from memory.</summary>
    /// <param name="utf8Csv">The UTF-8 encoded CSV data.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <returns>The new reader.</returns>
    /// <remarks>The data is not copied: the values are slices of <paramref name="utf8Csv"/>.</remarks>
    public static Utf8CsvReader Create(ReadOnlyMemory<byte> utf8Csv, CsvReaderOptions? options = null)
    {
        return new Utf8CsvReader(new CsvParser<byte>(options ?? CsvReaderOptions.Default, source: null, utf8Csv));
    }

    /// <summary>Creates a reader that reads UTF-8 CSV data from a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvReaderOptions.Default"/>.</param>
    /// <returns>The new reader. Dispose it to close the file.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The reader owns the stream, and the method disposes it when it throws")]
    public static Utf8CsvReader CreateFromFile(string path, CsvReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan | FileOptions.Asynchronous);
        try
        {
            return Create(stream, options, leaveOpen: false);
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
    public Utf8CsvRecord Current
    {
        get
        {
            if (!_hasCurrent)
                throw new InvalidOperationException("There is no current record: call Read or ReadAsync first.");

            return new Utf8CsvRecord(_parser);
        }
    }

    /// <summary>Gets the 0-based index of the current record. The header row and the comment lines are not counted.</summary>
    public long RowIndex => _parser.RecordIndex;

    /// <summary>Gets the 1-based line number where the current record starts. Every line terminator counts, including those inside quoted values.</summary>
    public long LineNumber => _parser.LineNumber;

    /// <summary>Gets the 0-based position, in bytes from the start of the data, where the current record starts.</summary>
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

    /// <summary>Enumerates the records of a <see cref="Utf8CsvReader"/>.</summary>
    public ref struct Enumerator
    {
        private readonly Utf8CsvReader _reader;

        internal Enumerator(Utf8CsvReader reader)
        {
            _reader = reader;
        }

        /// <summary>Gets the current record.</summary>
        public readonly Utf8CsvRecord Current => _reader.Current;

        /// <summary>Reads the next record.</summary>
        /// <returns><see langword="true"/> when a record was read; <see langword="false"/> at the end of the data.</returns>
        public readonly bool MoveNext() => _reader.Read();
    }
}
