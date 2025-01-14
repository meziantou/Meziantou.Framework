using System.Text;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

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

        using (new AssertionScope())
        {
            Assert.Equal("value1.1", row1[0]);
            Assert.Equal("value1.2", row1[1]);
            Assert.Equal("value1.3", row1[2]);
            Assert.Equal("value2.1", row2[0]);
            Assert.Equal("value2.2", row2[1]);
            Assert.Equal("value2.3", row2[2]);

            row3.Should().BeNull();
        }
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

        using (new AssertionScope())
        {
            Assert.Equal("value1.1", row1["column1"]);
            Assert.Equal("value1.2", row1["column2"]);
            Assert.Equal("value1.3", row1["column3"]);
            Assert.Equal("value2.1", row2["column1"]);
            Assert.Equal("value2.2", row2["column2"]);
            Assert.Equal("value2.3", row2["column3"]);

            row3.Should().BeNull();
        }
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

        using (new AssertionScope())
        {
            Assert.Equal("value1.1", row1["column1"]);
            Assert.Equal("value1.2\r\nline2", row1["column2"]);
            Assert.Equal("value1.3", row1["column3"]);
            Assert.Equal("value2.1", row2["column1"]);
            Assert.Equal("value2.2", row2["column2"]);
            Assert.Equal("value2.3", row2["column3"]);

            row3.Should().BeNull();
        }
    }

    [Fact]
    public async Task CsvReader_QuoteInTheMiddleOfAValue()
    {
        var sb = new StringBuilder();
        sb.Append("a\"c");

        using var sr = new StringReader(sb.ToString());
        var reader = new CsvReader(sr);
        var row1 = await reader.ReadRowAsync();
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
        Assert.Equal("ab", row1[0]);
        Assert.Equal("cd", row1[1]);
    }
}
