namespace Meziantou.Framework.Csv.Tests;

public class CsvWriterTests
{
    [Fact]
    public async Task CsvWriterAsync_NoEscape()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("A", "B");
        await writer.WriteRowAsync("C", "D");
        Assert.Equal($"A,B{Environment.NewLine}C,D", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueWithSeparator()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("A", "B,");
        await writer.WriteRowAsync("C", "D");
        Assert.Equal($@"A,""B,""{Environment.NewLine}C,D", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueWithStartingQuote()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("A", "\"B");
        Assert.Equal("A,\"\"\"B\"", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_WriteValues()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw)
        {
            EndOfLine = "\n",
        };
        await writer.BeginRowAsync();
        await writer.WriteValuesAsync("A", "B");
        await writer.WriteValuesAsync("C", "D");
        await writer.BeginRowAsync();
        await writer.WriteValuesAsync("E");
        Assert.Equal("A,B,C,D\nE", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_NoQuoteCharacter()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw)
        {
            Quote = null,
        };

        await writer.WriteRowAsync("A\"", "B");
        Assert.Equal("A\",B", sw.ToString());
    }

    [Theory]
    [InlineData("A;B:D;E")]
    [InlineData("A,;B:D;E")]
    [InlineData(",A;B:D;E")]
    [InlineData("A;\"B:D;E")]
    [InlineData("A;B\":D;E")]
    public async Task CsvWriterAsync_CsvReader(string data)
    {
        var rows = new List<List<string>>();
        foreach (var row in data.Split(':'))
        {
            rows.Add(new List<string>(row.Split(';')));
        }

        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        foreach (var row in rows)
        {
            await writer.WriteRowAsync(row);
        }

        var csv = sw.ToString();
        using var sr = new StringReader(csv);
        var reader = new CsvReader(sr);

        var rowIndex = -1;
        CsvRow? csvRow;
        while ((csvRow = await reader.ReadRowAsync()) is not null)
        {
            rowIndex++;
            Assert.Equal(rows[rowIndex], csvRow.Values.ToList());
        }

        Assert.Equal(rows.Count - 1, rowIndex);
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueContainingLineFeed()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw) { EndOfLine = "\n" };
        await writer.WriteRowAsync("a\nb", "c");
        await writer.WriteRowAsync("d", "e");
        Assert.Equal("\"a\nb\",c\nd,e", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueContainingCarriageReturn()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw) { EndOfLine = "\n" };
        await writer.WriteRowAsync("a\r\nb", "c");
        Assert.Equal("\"a\r\nb\",c", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueWithQuoteInTheMiddle()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("a\"b", "c");
        Assert.Equal("\"a\"\"b\",c", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueWithQuoteAtTheEnd()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("ab\"", "c");
        Assert.Equal("\"ab\"\"\",c", sw.ToString());
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueContainingCustomEndOfLine()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw) { EndOfLine = "|" };
        await writer.WriteRowAsync("a|b", "c");
        Assert.Equal("\"a|b\",c", sw.ToString());
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\"b")]
    [InlineData("a,b")]
    [InlineData("\"")]
    [InlineData("a\n\"b\",c")]
    public async Task CsvWriterAsync_SpecialCharacters_RoundTripThroughCsvReader(string value)
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw) { EndOfLine = "\n" };
        await writer.WriteRowAsync(value, "next");
        await writer.WriteRowAsync("second", "row");

        using var sr = new StringReader(sw.ToString());
        var reader = new CsvReader(sr);

        var row1 = await reader.ReadRowAsync();
        Assert.NotNull(row1);
        Assert.Equal([value, "next"], row1.Values);

        var row2 = await reader.ReadRowAsync();
        Assert.NotNull(row2);
        Assert.Equal(["second", "row"], row2.Values);

        Assert.Null(await reader.ReadRowAsync());
    }
}
