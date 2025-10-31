namespace Meziantou.Framework.Csv;

public class CsvWriter
{
    private bool _isFirstRow = true;
    private bool _isFirstRowValue = true;

    public TextWriter BaseWriter { get; }
    public string EndOfLine { get; set; } = Environment.NewLine;
    public char Separator { get; set; } = CsvReader.DefaultSeparatorCharacter;
    public char? Quote { get; set; } = CsvReader.DefaultQuoteCharacter;

    public CsvWriter(TextWriter textReader)
    {
        BaseWriter = textReader ?? throw new ArgumentNullException(nameof(textReader));
    }

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

    public Task WriteValueAsync(string? value)
    {
        return WriteEscapedValueAsync(value);
    }

    public async Task WriteValuesAsync(IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            await WriteValueAsync(value).ConfigureAwait(false);
        }
    }

    public Task WriteValuesAsync(params string?[] values)
    {
        return WriteValuesAsync((IEnumerable<string?>)values);
    }

    public Task WriteRowAsync(params string?[] values)
    {
        return WriteRowAsync((IEnumerable<string?>)values);
    }

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
