namespace Meziantou.Framework.Csv;

/// <summary>Reads CSV data from a text reader and parses it into rows.</summary>
/// <example>
/// <code>
/// using var reader = new StringReader("Name,Age\nJohn,30\nJane,25");
/// var csvReader = new CsvReader(reader) { HasHeaderRow = true };
/// 
/// while (await csvReader.ReadRowAsync() is { } row)
/// {
///     Console.WriteLine($"{row["Name"]} is {row["Age"]} years old");
/// }
/// </code>
/// </example>
public class CsvReader
{
    /// <summary>The default separator character used in CSV files (comma).</summary>
    public const char DefaultSeparatorCharacter = ',';

    /// <summary>The default quote character used in CSV files (double quote).</summary>
    public const char DefaultQuoteCharacter = '"';

    private CsvColumn[]? _columns;
    private readonly char[] _readBuffer = new char[4096];
    private int _readBufferOffset;
    private int _readBufferLength;
    private bool _hasBufferedChar;
    private char _bufferedChar;
    private bool _endOfStreamReached;

    /// <summary>Gets or sets the character used to separate values in a row. Default is a comma (,).</summary>
    public char Separator { get; set; } = DefaultSeparatorCharacter;

    /// <summary>Gets or sets the character used to quote values that contain special characters. Default is a double quote (").</summary>
    public char? Quote { get; set; } = DefaultQuoteCharacter;

    /// <summary>Gets or sets a value indicating whether the first row contains column headers.</summary>
    public bool HasHeaderRow { get; set; }

    /// <summary>Gets the current column number being read (1-based).</summary>
    public int ColumnNumber { get; private set; }

    /// <summary>Gets the current line number being read (1-based).</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets or sets the current row number (0-based).</summary>
    public int RowNumber { get; set; }

    /// <summary>Gets the underlying <see cref="TextReader"/> used to read the CSV data.</summary>
    public TextReader BaseReader { get; }

    /// <summary>Gets a value indicating whether the end of the stream has been reached.</summary>
    public bool EndOfStream
    {
        get
        {
            if (_hasBufferedChar)
                return false;

            if (_readBufferOffset < _readBufferLength)
                return false;

            if (_endOfStreamReached)
                return true;

            if (BaseReader is StreamReader streamReader)
                return streamReader.EndOfStream;

            return BaseReader.Peek() < 0;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="CsvReader"/> class with the specified <see cref="TextReader"/>.</summary>
    /// <param name="textReader">The <see cref="TextReader"/> to read CSV data from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="textReader"/> is <see langword="null"/>.</exception>
    public CsvReader(TextReader textReader)
    {
        BaseReader = textReader ?? throw new ArgumentNullException(nameof(textReader));
    }

    private async ValueTask<char?> ReadCharAsync()
    {
        if (_hasBufferedChar)
        {
            _hasBufferedChar = false;
            return _bufferedChar;
        }

        return await ReadCharCoreAsync().ConfigureAwait(false);
    }

    private async ValueTask<char?> PeekCharAsync()
    {
        if (_hasBufferedChar)
            return _bufferedChar;

        var nextChar = await ReadCharCoreAsync().ConfigureAwait(false);
        if (!nextChar.HasValue)
            return null;

        _bufferedChar = nextChar.Value;
        _hasBufferedChar = true;
        return nextChar;
    }

    private async ValueTask<char?> ReadCharCoreAsync()
    {
        if (_endOfStreamReached)
            return null;

        if (_readBufferOffset >= _readBufferLength)
        {
            _readBufferLength = await BaseReader.ReadAsync(_readBuffer.AsMemory()).ConfigureAwait(false);
            _readBufferOffset = 0;
            if (_readBufferLength <= 0)
            {
                _endOfStreamReached = true;
                return null;
            }
        }

        return _readBuffer[_readBufferOffset++];
    }

    /// <summary>Reads the next row from the CSV data.</summary>
    /// <returns>A <see cref="CsvRow"/> representing the next row, or <see langword="null"/> if the end of the stream has been reached.</returns>
    public async Task<CsvRow?> ReadRowAsync()
    {
        var endOfStream = false;
        var rowValues = new List<string>();
        if (BaseReader is not null)
        {
            var inQuote = false;
            var value = new StringBuilder();
            var hasCell = false;
            ColumnNumber = 0;
            while (true)
            {
                var c = await ReadCharAsync().ConfigureAwait(false);
                if (!c.HasValue)
                {
                    endOfStream = true;
                    if (hasCell)
                    {
                        rowValues.Add(value.ToString());
                    }

                    RowNumber++;
                    break;
                }

                ColumnNumber++;
                var next = await PeekCharAsync().ConfigureAwait(false);
                if (inQuote)
                {
                    if (c == '\n')
                    {
                        LineNumber++;
                    }
                    else if (c == '\r')
                    {
                        if (next == '\n')
                        {
                            hasCell = true;
                            value.Append(c.Value);
                            c = await ReadCharAsync().ConfigureAwait(false);
                            ColumnNumber++;
                            if (!c.HasValue)
                            {
                                endOfStream = true;
                                if (hasCell)
                                {
                                    rowValues.Add(value.ToString());
                                }

                                RowNumber++;
                                break;
                            }
                        }

                        LineNumber++;
                    }

                    if (Quote.HasValue && c == Quote)
                    {
                        if (next == Quote)
                        {
                            await ReadCharAsync().ConfigureAwait(false);
                            hasCell = true;
                            value.Append(Quote.Value);
                        }
                        else
                        {
                            inQuote = false;
                        }
                    }
                    else
                    {
                        hasCell = true;
                        value.Append(c.Value);
                    }
                }
                else if (c == '\n')
                {
                    if (hasCell)
                    {
                        rowValues.Add(value.ToString());
                    }

                    LineNumber++;
                    break;
                }
                else if (c == '\r')
                {
                    if (next == '\n')
                    {
                        await ReadCharAsync().ConfigureAwait(false);
                        ColumnNumber++;
                    }

                    if (hasCell)
                    {
                        rowValues.Add(value.ToString());
                    }

                    LineNumber++;
                    break;
                }
                else if (Quote.HasValue && c == Quote)
                {
                    if (value.Length == 0)
                    {
                        inQuote = true;
                    }
                    else
                    {
                        hasCell = true;
                        value.Append(c.Value);
                    }
                }
                else if (c == Separator)
                {
                    rowValues.Add(value.ToString());
                    value.Clear();
                    hasCell = false;
                }
                else
                {
                    hasCell = true;
                    value.Append(c.Value);
                }
            }
        }

        if (rowValues.Count == 0 && endOfStream)
            return null;

        var columns = _columns;
        if (HasHeaderRow && columns is null)
        {
            columns = rowValues.Select(CreateColumn).ToArray();
            _columns = columns;
            return await ReadRowAsync().ConfigureAwait(false); // Read the first row with data
        }

        return CreateRow(columns, rowValues);
    }

    /// <summary>Creates a <see cref="CsvColumn"/> instance. This method can be overridden to create custom column types.</summary>
    /// <param name="name">The name of the column.</param>
    /// <param name="index">The zero-based index of the column.</param>
    /// <returns>A new <see cref="CsvColumn"/> instance.</returns>
    protected virtual CsvColumn CreateColumn(string name, int index)
    {
        return new CsvColumn(name, index);
    }

    /// <summary>Creates a <see cref="CsvRow"/> instance. This method can be overridden to create custom row types.</summary>
    /// <param name="columns">The columns of the row, or <see langword="null"/> if the CSV file has no header row.</param>
    /// <param name="values">The values of the row.</param>
    /// <returns>A new <see cref="CsvRow"/> instance.</returns>
    protected virtual CsvRow CreateRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
    {
        return new CsvRow(columns, values);
    }
}
