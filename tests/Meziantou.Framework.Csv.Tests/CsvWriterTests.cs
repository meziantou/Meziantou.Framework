using FluentAssertions;
using Xunit;

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

        sw.ToString().Should().Be($"A,B{Environment.NewLine}C,D");
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueWithSeparator()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("A", "B,");
        await writer.WriteRowAsync("C", "D");

        sw.ToString().Should().Be($@"A,""B,""{Environment.NewLine}C,D");
    }

    [Fact]
    public async Task CsvWriterAsync_EscapeValueWithStartingQuote()
    {
        using var sw = new StringWriter();
        var writer = new CsvWriter(sw);
        await writer.WriteRowAsync("A", "\"B");

        sw.ToString().Should().Be("A,\"\"\"B\"");
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

        sw.ToString().Should().Be("A,B,C,D\nE");
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

        sw.ToString().Should().Be("A\",B");
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
        CsvRow csvRow;
        while ((csvRow = await reader.ReadRowAsync()) is not null)
        {
            rowIndex++;
            csvRow.Values.ToList().Should().BeEquivalentTo(rows[rowIndex]);
        }

        rowIndex.Should().Be(rows.Count - 1);
    }
}
