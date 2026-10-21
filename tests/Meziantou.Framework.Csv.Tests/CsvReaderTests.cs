namespace Meziantou.Framework.Csv.Tests;

public class CsvReaderTests
{
    [Fact]
    public async Task ReadmeExample_ReadWithoutHeader()
    {
        #region CsvReader_ReadWithoutHeader
        using var reader = new StringReader("""
            Alice,30,New York
            Bob,25,London
            """);
        var csvReader = new CsvReader(reader)
        {
            HasHeaderRow = false,
        };

        var lines = new List<string>();
        while (await csvReader.ReadRowAsync() is { } row)
        {
            lines.Add($"{row[0]}, {row[1]}, {row[2]}");
        }

        Assert.Equal(["Alice, 30, New York", "Bob, 25, London"], lines);
        #endregion
    }

    [Fact]
    public async Task ReadmeExample_ReadWithHeader()
    {
        #region CsvReader_ReadWithHeader
        using var reader = new StringReader("""
            Name,Age
            Alice,30
            """);
        var csvReader = new CsvReader(reader)
        {
            HasHeaderRow = true,
        };

        var row = await csvReader.ReadRowAsync();
        Assert.NotNull(row);

        // Access by column name
        Assert.Equal("Alice", row["Name"]);
        Assert.Equal("30", row["Age"]);

        // Access by index
        Assert.Equal("Alice", row[0]);

        // Access by CsvColumn
        var nameColumn = row.Columns?.FirstOrDefault(c => c.Name == "Name");
        Assert.NotNull(nameColumn);
        Assert.Equal("Alice", row[nameColumn]);

        // ReadRowAsync returns null at the end of the data
        Assert.Null(await csvReader.ReadRowAsync());
        #endregion
    }

    [Fact]
    public async Task ReadmeExample_CustomSeparatorAndQuote()
    {
        #region CsvReader_CustomSeparatorAndQuote
        using var reader = new StringReader("Name\tComment\nAlice\t'Tab\tand ''quote'''");
        var csvReader = new CsvReader(reader)
        {
            Separator = '\t',      // Tab-separated
            Quote = '\'',          // Single quote instead of double quote
            HasHeaderRow = true,
        };

        var row = await csvReader.ReadRowAsync();
        Assert.NotNull(row);
        Assert.Equal("Alice", row["Name"]);
        Assert.Equal("Tab\tand 'quote'", row["Comment"]);
        #endregion
    }

    [Fact]
    public async Task ReadmeExample_QuotedValues()
    {
        #region CsvReader_QuotedValues
        using var reader = new StringReader(""""
            "Value with, comma","Normal value","Value with ""quotes""","Multi-line
            value"
            """");
        var csvReader = new CsvReader(reader);

        var row = await csvReader.ReadRowAsync();
        Assert.NotNull(row);
        Assert.Equal("Value with, comma", row[0]);
        Assert.Equal("Normal value", row[1]);
        Assert.Equal("Value with \"quotes\"", row[2]);
        Assert.Equal("Multi-line\nvalue", row[3], ignoreLineEndingDifferences: true);
        #endregion
    }

    [Fact]
    public async Task CsvReader_DocumentationExample()
    {
        #region CsvReader_ReadRowAsync
        using var reader = new StringReader("Name,Age\nJohn,30\nJane,25");
        var csvReader = new CsvReader(reader) { HasHeaderRow = true };

        var lines = new List<string>();
        while (await csvReader.ReadRowAsync() is { } row)
        {
            lines.Add($"{row["Name"]} is {row["Age"]} years old");
        }

        Assert.Equal(["John is 30 years old", "Jane is 25 years old"], lines);
        #endregion
    }

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
    public async Task CsvReader_ColumnNumberCountsBothCharactersOfAnEscapedQuote()
    {
        using var sr = new StringReader("\"ab\"\"\"\n\"a\"\"b\"");
        var reader = new CsvReader(sr);

        await reader.ReadRowAsync();
        Assert.Equal(7, reader.ColumnNumber);

        await reader.ReadRowAsync();
        Assert.Equal(6, reader.ColumnNumber);
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

    [Fact]
    public async Task CsvReader_RowNumberIsTheZeroBasedIndexOfTheRowJustReturned()
    {
        using var sr = new StringReader("a\nb\nc");
        var reader = new CsvReader(sr);

        await reader.ReadRowAsync();
        Assert.Equal(0, reader.RowNumber);

        await reader.ReadRowAsync();
        Assert.Equal(1, reader.RowNumber);

        await reader.ReadRowAsync();
        Assert.Equal(2, reader.RowNumber);
    }

    [Fact]
    public async Task CsvReader_RowNumberDoesNotAdvancePastTheLastRow()
    {
        using var sr = new StringReader("a\nb");
        var reader = new CsvReader(sr);

        await reader.ReadRowAsync();
        await reader.ReadRowAsync();
        Assert.Equal(1, reader.RowNumber);

        Assert.Null(await reader.ReadRowAsync());
        Assert.Equal(1, reader.RowNumber);
    }

    [Fact]
    public async Task CsvReader_RowNumberCountsTheHeaderRow()
    {
        using var sr = new StringReader("A,B\n1,2\n3,4");
        var reader = new CsvReader(sr) { HasHeaderRow = true };

        await reader.ReadRowAsync();
        Assert.Equal(1, reader.RowNumber);

        await reader.ReadRowAsync();
        Assert.Equal(2, reader.RowNumber);
    }

    [Fact]
    public async Task CsvReader_RowNumberIsIndependentOfTheLineTerminator()
    {
        using var sr = new StringReader("a\r\nb\rc");
        var reader = new CsvReader(sr);

        await reader.ReadRowAsync();
        Assert.Equal(0, reader.RowNumber);

        await reader.ReadRowAsync();
        Assert.Equal(1, reader.RowNumber);

        await reader.ReadRowAsync();
        Assert.Equal(2, reader.RowNumber);
    }

    [Theory]
    [InlineData("a,b,", new[] { "a", "b", "" })]
    [InlineData("a,b,\n", new[] { "a", "b", "" })]
    [InlineData("a,b,\r\n", new[] { "a", "b", "" })]
    [InlineData("a,b,\"\"", new[] { "a", "b", "" })]
    [InlineData(",", new[] { "", "" })]
    [InlineData(",,", new[] { "", "", "" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("a,\"\",b", new[] { "a", "", "b" })]
    [InlineData("\"a,b\",", new[] { "a,b", "" })]
    [InlineData("a,b", new[] { "a", "b" })]
    public async Task CsvReader_EmptyTrailingValueIsPreserved(string data, string[] expected)
    {
        using var sr = new StringReader(data);
        var reader = new CsvReader(sr);

        var row = await reader.ReadRowAsync();

        Assert.NotNull(row);
        Assert.Equal(expected, row.Values);
    }

    [Fact]
    public async Task CsvReader_EmptyInputReturnsNoRow()
    {
        using var sr = new StringReader("");
        var reader = new CsvReader(sr);

        Assert.Null(await reader.ReadRowAsync());
    }

    [Fact]
    public async Task CsvReader_TrailingNewLineDoesNotProduceAnExtraRow()
    {
        using var sr = new StringReader("a\nb\n");
        var reader = new CsvReader(sr);

        Assert.NotNull(await reader.ReadRowAsync());
        Assert.NotNull(await reader.ReadRowAsync());
        Assert.Null(await reader.ReadRowAsync());
    }

    [Fact]
    public async Task CsvReader_BlankLineStillYieldsARowWithoutValues()
    {
        using var sr = new StringReader("a\n\nb");
        var reader = new CsvReader(sr);

        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal(["a"], row1.Values);

        var row2 = await reader.ReadRowAsync();
        Assert.NotNull(row2);
        Assert.Empty(row2.Values);

        var row3 = await reader.ReadRowAsync();
        Assert.NotNull(row3);
        Assert.Equal(["b"], row3.Values);
    }

    [Fact]
    public async Task CsvReader_EmptyTrailingValueIsAddressableByColumnName()
    {
        using var sr = new StringReader("A,B,C\n1,2,");
        var reader = new CsvReader(sr) { HasHeaderRow = true };

        var row = await reader.ReadRowAsync();

        Assert.NotNull(row);
        Assert.Equal(["1", "2", ""], row.Values);
        Assert.Equal("", row["C"]);
    }

    [Fact]
    public async Task CsvWriter_TrailingEmptyValue_RoundTripsThroughCsvReader()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw) { EndOfLine = "\n" };
        await writer.WriteRowAsync("a", "b", "");
        await writer.WriteRowAsync("", "", "");

        using var sr = new StringReader(sw.ToString());
        var reader = new CsvReader(sr);

        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal(["a", "b", ""], row1.Values);

        var row2 = await reader.ReadRowAsync();
        Assert.NotNull(row2);
        Assert.Equal(["", "", ""], row2.Values);

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
