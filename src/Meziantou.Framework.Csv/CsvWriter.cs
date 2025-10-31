namespace Meziantou.Framework.Csv;

/// <summary>
/// Provides methods for writing CSV data.
/// </summary>
public class CsvWriter
{
    private bool _isFirstRow = true;
    private bool _isFirstRowValue = true;

    /// <summary>
    /// Gets the underlying text writer.
    /// </summary>
    public TextWriter BaseWriter { get; }

    /// <summary>
    /// Gets or sets the end-of-line string to use between rows.
    /// </summary>
    public string EndOfLine { get; set; } = Environment.NewLine;

    /// <summary>
    /// Gets or sets the column separator character.
    /// </summary>
    public char Separator { get; set; } = CsvReader.DefaultSeparatorCharacter;

    /// <summary>
    /// Gets or sets the quote character used to escape values containing separators or newlines.
    /// </summary>
    public char? Quote { get; set; } = CsvReader.DefaultQuoteCharacter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvWriter"/> class.
    /// </summary>
    /// <param name="textReader">The text writer to write CSV data to.</param>
    public CsvWriter(TextWriter textReader)
    {
        BaseWriter = textReader ?? throw new ArgumentNullException(nameof(textReader));
    }

    /// <summary>
    /// Asynchronously begins a new row in the CSV output.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task BeginRowAsync()
    {
        var isFirstRow = _isFirstRow;
        _isFirstRow = false;
        _isFirstRowValue = true;
        if (!isFirstRow)
        {
            return BaseWriter.WriteAsync(EndOfLine);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously writes a value to the current row.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WriteValueAsync(string? value)
    {
        return WriteEscapedValueAsync(value);
    }

    /// <summary>
    /// Asynchronously writes multiple values to the current row.
    /// </summary>
    /// <param name="values">The values to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WriteValuesAsync(IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            await WriteValueAsync(value).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously writes multiple values to the current row.
    /// </summary>
    /// <param name="values">The values to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WriteValuesAsync(params string?[] values)
    {
        return WriteValuesAsync((IEnumerable<string?>)values);
    }

    /// <summary>
    /// Asynchronously writes a complete row with the specified values.
    /// </summary>
    /// <param name="values">The values to write as a row.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WriteRowAsync(params string?[] values)
    {
        return WriteRowAsync((IEnumerable<string?>)values);
    }

    /// <summary>
    /// Asynchronously writes a complete row with the specified values.
    /// </summary>
    /// <param name="values">The values to write as a row.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WriteRowAsync(IEnumerable<string?> values)
    {
        await BeginRowAsync().ConfigureAwait(false);
        await WriteValuesAsync(values).ConfigureAwait(false);
    }

    private async Task WriteEscapedValueAsync(string? value)
    {
        var writer = BaseWriter;
        if (!_isFirstRowValue)
        {
            await writer.WriteAsync(Separator).ConfigureAwait(false);
        }

        _isFirstRowValue = false;
        if (!string.IsNullOrEmpty(value))
        {
            if (Quote.HasValue)
            {
                var quote = Quote.Value;
                if (value[0] != quote && value.IndexOf(Separator, StringComparison.Ordinal) < 0)
                {
                    await writer.WriteAsync(value).ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteAsync(quote).ConfigureAwait(false);
                    for (var i = 0; i < value.Length; i++)
                    {
                        var c = value[i];
                        if (c == quote)
                        {
                            await writer.WriteAsync(quote).ConfigureAwait(false);
                            await writer.WriteAsync(quote).ConfigureAwait(false);
                        }
                        else
                        {
                            await writer.WriteAsync(c).ConfigureAwait(false);
                        }
                    }

                    await writer.WriteAsync(quote).ConfigureAwait(false);
                }
            }
            else
            {
                await writer.WriteAsync(value).ConfigureAwait(false);
            }
        }
    }
}
