namespace Meziantou.Framework.Csv;

/// <summary>Writes CSV data to a text writer.</summary>
/// <example>
/// <code>
/// using var writer = new StringWriter();
/// var csvWriter = new CsvWriter(writer);
/// 
/// await csvWriter.WriteRowAsync("Name", "Age");
/// await csvWriter.WriteRowAsync("John", "30");
/// await csvWriter.WriteRowAsync("Jane", "25");
/// 
/// Console.WriteLine(writer.ToString());
/// // Output:
/// // Name,Age
/// // John,30
/// // Jane,25
/// </code>
/// </example>
public class CsvWriter
{
    private bool _isFirstRow = true;
    private bool _isFirstRowValue = true;

    /// <summary>Gets the underlying <see cref="TextWriter"/> used to write the CSV data.</summary>
    public TextWriter BaseWriter { get; }

    /// <summary>Gets or sets the end-of-line string. Default is <see cref="Environment.NewLine"/>.</summary>
    public string EndOfLine { get; set; } = Environment.NewLine;

    /// <summary>Gets or sets the character used to separate values in a row. Default is a comma (,).</summary>
    public char Separator { get; set; } = CsvReader.DefaultSeparatorCharacter;

    /// <summary>Gets or sets the character used to quote values that contain special characters. Default is a double quote (").</summary>
    public char? Quote { get; set; } = CsvReader.DefaultQuoteCharacter;

    /// <summary>Initializes a new instance of the <see cref="CsvWriter"/> class with the specified <see cref="TextWriter"/>.</summary>
    /// <param name="textReader">The <see cref="TextWriter"/> to write CSV data to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="textReader"/> is <see langword="null"/>.</exception>
    public CsvWriter(TextWriter textReader)
    {
        BaseWriter = textReader ?? throw new ArgumentNullException(nameof(textReader));
    }

    /// <summary>Begins a new row. This method must be called before writing values using <see cref="WriteValueAsync"/> or <see cref="WriteValuesAsync(IEnumerable{string?})"/>.</summary>
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

    /// <summary>Writes a single value to the current row. Call <see cref="BeginRowAsync"/> before calling this method.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WriteValueAsync(string? value)
    {
        return WriteEscapedValueAsync(value);
    }

    /// <summary>Writes multiple values to the current row. Call <see cref="BeginRowAsync"/> before calling this method.</summary>
    /// <param name="values">The values to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WriteValuesAsync(IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            await WriteValueAsync(value).ConfigureAwait(false);
        }
    }

    /// <summary>Writes multiple values to the current row. Call <see cref="BeginRowAsync"/> before calling this method.</summary>
    /// <param name="values">The values to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WriteValuesAsync(params string?[] values)
    {
        return WriteValuesAsync((IEnumerable<string?>)values);
    }

    /// <summary>Writes a complete row with the specified values.</summary>
    /// <param name="values">The values to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task WriteRowAsync(params string?[] values)
    {
        return WriteRowAsync((IEnumerable<string?>)values);
    }

    /// <summary>Writes a complete row with the specified values.</summary>
    /// <param name="values">The values to write.</param>
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
