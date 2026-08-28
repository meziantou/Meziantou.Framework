namespace Meziantou.Framework.Csv.Tests;

public class CsvReaderTests
{
    [Fact]
    public async Task CsvReader_RowWithoutHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine("value1.1,value1.2,value1.3");
        sb.Append("value2.1,value2.2,value2.3");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr)
        {
            HasHeaderRow = false,
        };

        var row1 = await reader.ReadRowAsync();
        var row2 = await reader.ReadRowAsync();
        var row3 = await reader.ReadRowAsync();

        Assert.NotNull(row1);
        Assert.NotNull(row2);
        Assert.Equal("value1.1", row1[0]);
        Assert.Equal("value1.2", row1[1]);
        Assert.Equal("value1.3", row1[2]);
        Assert.Equal("value2.1", row2[0]);
        Assert.Equal("value2.2", row2[1]);
        Assert.Equal("value2.3", row2[2]);
        Assert.Null(row3);
    }

    [Fact]
    public async Task CsvReader_RowWithHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine("column1,column2,column3");
        sb.AppendLine("value1.1,value1.2,value1.3");
        sb.Append("value2.1,value2.2,value2.3");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr)
        {
            HasHeaderRow = true,
        };
        var row1 = await reader.ReadRowAsync();
        var row2 = await reader.ReadRowAsync();
        var row3 = await reader.ReadRowAsync();

        Assert.NotNull(row1);
        Assert.NotNull(row2);
        Assert.Equal("value1.1", row1["column1"]);
        Assert.Equal("value1.2", row1["column2"]);
        Assert.Equal("value1.3", row1["column3"]);
        Assert.Equal("value2.1", row2["column1"]);
        Assert.Equal("value2.2", row2["column2"]);
        Assert.Equal("value2.3", row2["column3"]);

        Assert.Null(row3);
    }

    [Fact]
    public async Task CsvReader_ColumnNumberCountsCharactersNotFields()
    {
        using var sr = new StringReader("A,B,C\nvalue");
        var reader = new CsvReader(sr);

        Assert.Equal(0, reader.ColumnNumber);

        await reader.ReadRowAsync();
        Assert.Equal(6, reader.ColumnNumber);

        await reader.ReadRowAsync();
        Assert.Equal(5, reader.ColumnNumber);
    }

    [Fact]
    public async Task CsvReader_LineNumberCountsConsumedLineTerminators()
    {
        using var sr = new StringReader("a\nb\nc");
        var reader = new CsvReader(sr);

        Assert.Equal(0, reader.LineNumber);

        await reader.ReadRowAsync();
        Assert.Equal(1, reader.LineNumber);

        await reader.ReadRowAsync();
        Assert.Equal(2, reader.LineNumber);

        await reader.ReadRowAsync();
        Assert.Equal(2, reader.LineNumber);
    }

    [Fact]
    public async Task CsvReader_LineNumberCountsLineTerminatorsInsideQuotedValues()
    {
        using var sr = new StringReader("\"a\nb\",c");
        var reader = new CsvReader(sr);

        await reader.ReadRowAsync();

        Assert.Equal(1, reader.LineNumber);
    }

    [Fact]
    public async Task CsvReader_MultiLineQuotedValue()
    {
        var sb = new StringBuilder();
        sb.AppendLine("column1,column2,column3");
        sb.AppendLine("value1.1,\"value1.2\r\nline2\",value1.3");
        sb.Append("value2.1,value2.2,value2.3");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr)
        {
            HasHeaderRow = true,
        };
        var row1 = await reader.ReadRowAsync();
        var row2 = await reader.ReadRowAsync();
        var row3 = await reader.ReadRowAsync();

        Assert.NotNull(row1);
        Assert.NotNull(row2);
        Assert.Equal("value1.1", row1["column1"]);
        Assert.Equal("value1.2\r\nline2", row1["column2"]);
        Assert.Equal("value1.3", row1["column3"]);
        Assert.Equal("value2.1", row2["column1"]);
        Assert.Equal("value2.2", row2["column2"]);
        Assert.Equal("value2.3", row2["column3"]);

        Assert.Null(row3);
    }

    [Fact]
    public async Task CsvReader_QuoteInTheMiddleOfAValue()
    {
        var sb = new StringBuilder();
        sb.Append("a\"c");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr);
        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal("a\"c", row1[0]);
    }

    [Fact]
    public async Task CsvReader_QuoteAtTheStartOfAValue()
    {
        var sb = new StringBuilder();
        sb.Append("\"\"\"bc\"");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr);
        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal("\"bc", row1[0]);
    }

    [Fact]
    public async Task CsvReader_QuoteAtTheEndOfAValue()
    {
        var sb = new StringBuilder();
        sb.Append("\"ab\"\"\"");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr);
        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal("ab\"", row1[0]);
    }

    [Fact]
    public async Task CsvReader_QuoteAndSeparator()
    {
        var sb = new StringBuilder();
        sb.Append("'ab'\t'cd'");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr)
        {
            Quote = '\'',
            Separator = '\t',
        };
        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal("ab", row1[0]);
        Assert.Equal("cd", row1[1]);
    }

    [Fact]
    public async Task CsvReader_QuotedValueEndingWithCarriageReturnAtEndOfStream_DoesNotThrow()
    {
        using var sr = new TruncatedCarriageReturnReader();
        var reader = new CsvReader(sr);

        var row = await reader.ReadRowAsync();

        Assert.NotNull(row);
        Assert.Equal("a\r", row[0]);
        Assert.Null(await reader.ReadRowAsync());
    }

    private sealed class TruncatedCarriageReturnReader : TextReader
    {
        private readonly char[] _content = ['"', 'a', '\r'];
        private int _position;

        public override int Peek()
        {
            if (_position < _content.Length)
                return _content[_position];

            // Simulate an inconsistent reader that reported a '\n' after '\r' in Peek(),
            // but reaches EOF when the next character is read.
            if (_position == _content.Length)
                return '\n';

            return -1;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (count <= 0)
                return 0;

            if (_position >= _content.Length)
                return 0;

            buffer[index] = _content[_position];
            _position++;
            return 1;
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            return Task.FromResult(Read(buffer, index, count));
        }
    }

    [Fact]
    public async Task CsvReader_CreateRowAndCreateColumnCanBeOverridden()
    {
        using var sr = new StringReader("A,B\n1,2");
        var reader = new CustomReader(sr) { HasHeaderRow = true };

        var row = await reader.ReadRowAsync();

        var customRow = Assert.IsType<CustomRow>(row);
        Assert.Equal("1", customRow["A"]);
        Assert.NotNull(customRow.Columns);
        Assert.IsType<CustomColumn>(customRow.Columns[0]);
    }

    private sealed class CustomColumn(string? name, int index) : CsvColumn(name, index);

    private sealed class CustomRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
        : CsvRow(columns, values);

    private sealed class CustomReader(TextReader reader) : CsvReader(reader)
    {
        protected override CsvColumn CreateColumn(string name, int index) => new CustomColumn(name, index);

        protected override CsvRow CreateRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
            => new CustomRow(columns, values);
    }
}
