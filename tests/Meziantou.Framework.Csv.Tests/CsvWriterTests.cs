using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Csv.Tests;

public class CsvWriterTests
{
    [Fact]
    public void Write()
    {
        #region CsvWriter_Write
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
        {
            writer.WriteRow("Name", "Age", "City");
            writer.WriteRow("Alice", "30", "New York");

            // Write a row value by value: numbers are formatted in the buffer, with the invariant culture
            writer.WriteField("Bob");
            writer.WriteField(25);
            writer.WriteField("London, UK"); // Quoted: the value contains the separator
            writer.EndRow();
        } // Disposing the writer flushes the data

        Assert.Equal("Name,Age,City\nAlice,30,New York\nBob,25,\"London, UK\"\n", output.ToString());
        #endregion
    }

    [Fact]
    public void Utf8Write()
    {
        #region Utf8CsvWriter_Write
        using var stream = new MemoryStream();
        using (var writer = Utf8CsvWriter.Create(stream, leaveOpen: true))
        {
            writer.WriteRow("Product", "Price");
            writer.WriteField("Café"u8); // UTF-8 bytes are written as they are
            writer.WriteField(3.5m, "F2");
            writer.EndRow();
        }

        Assert.Equal("Product,Price\r\nCafé,3.50\r\n", Encoding.UTF8.GetString(stream.ToArray()));
        #endregion
    }

    [Fact]
    public async Task WriteFile()
    {
        try
        {
            #region CsvWriter_WriteFile
            await using (var writer = CsvWriter.CreateToFile("export.csv"))
            {
                writer.WriteRow("Name", "Age");
                await writer.WriteRowAsync(["Alice", "30"]);
            } // Disposing the writer flushes the data and closes the file

            var content = await File.ReadAllTextAsync("export.csv");
            Assert.Equal("Name,Age\r\nAlice,30\r\n", content);
            #endregion
        }
        finally
        {
            File.Delete("export.csv");
        }
    }

    [Fact]
    public void Quoting()
    {
        #region CsvWriter_Quoting
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
        {
            writer.WriteRow("comma, here", "quote \" here", "line\nbreak", "plain");
        }

        Assert.Equal("\"comma, here\",\"quote \"\" here\",\"line\nbreak\",plain\n", output.ToString());
        #endregion
    }

    [Fact]
    public void QuoteModes()
    {
        #region CsvWriter_QuoteModes
        var always = new StringBuilder();
        using (var writer = CsvWriter.Create(always, new CsvWriterOptions { QuoteMode = CsvQuoteMode.Always, NewLine = "\n" }))
        {
            writer.WriteRow("a", "b");
        }

        var never = new StringBuilder();
        using (var writer = CsvWriter.Create(never, new CsvWriterOptions { QuoteMode = CsvQuoteMode.Never, NewLine = "\n" }))
        {
            writer.WriteRow("a,b", "c");
        }

        Assert.Equal("\"a\",\"b\"\n", always.ToString());
        Assert.Equal("a,b,c\n", never.ToString()); // Never: the value is written as it is
        #endregion
    }

    [Fact]
    public void EscapeFormulas()
    {
        #region CsvWriter_EscapeFormulas
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { EscapeFormulas = true, NewLine = "\n" }))
        {
            writer.WriteField("=HYPERLINK(\"http://example.com\")");
            writer.WriteField(-5); // Numbers are not escaped
            writer.EndRow();
        }

        Assert.Equal("\"'=HYPERLINK(\"\"http://example.com\"\")\",-5\n", output.ToString());
        #endregion
    }

    [Fact]
    public void EscapeFormulas_SingleQuote()
    {
        #region CsvWriter_EscapeFormulasSingleQuote
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { EscapeFormulas = true, Quote = '\'', NewLine = "\n" }))
        {
            writer.WriteRow("=1", "+a'b", "plain");
        }

        // The escaped values are quoted and their prefix is doubled, so they read back with the prefix
        Assert.Equal("'''=1','''+a''b',plain\n", output.ToString());
        #endregion
    }

    [Theory]
    [InlineData(CsvQuoteMode.AsNeeded, false)]
    [InlineData(CsvQuoteMode.AsNeeded, true)]
    [InlineData(CsvQuoteMode.Always, false)]
    [InlineData(CsvQuoteMode.Always, true)]
    public void EscapeFormulas_SingleQuote_RoundTrip(CsvQuoteMode quoteMode, bool utf8)
    {
        string?[] values = ["=1", "+a'b", "-", "@x", "\tt", "\rr", "plain", "'", "=", ""];
        var options = new CsvWriterOptions { EscapeFormulas = true, Quote = '\'', QuoteMode = quoteMode, NewLine = "\n" };
        string text;
        if (utf8)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = Utf8CsvWriter.Create(buffer, options))
            {
                writer.WriteRow(values);
            }

            text = Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        else
        {
            var builder = new StringBuilder();
            using (var writer = CsvWriter.Create(builder, options))
            {
                writer.WriteRow(values);
            }

            text = builder.ToString();
        }

        using var reader = CsvReader.CreateFromString(text, new CsvReaderOptions { Quote = '\'', HasHeaderRow = false });
        Assert.True(reader.Read());
        Assert.Equal(["'=1", "'+a'b", "'-", "'@x", "'\tt", "'\rr", "plain", "'", "'=", ""], reader.Current.ToStringArray());
        Assert.False(reader.Read());
    }

    [Fact]
    public void CustomSeparator()
    {
        #region CsvWriter_CustomSeparator
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { Separator = ';', NewLine = "\n" }))
        {
            writer.WriteRow("Column1", "Column2");
            writer.WriteField(1.5); // The invariant culture writes a dot, not the separator
            writer.WriteField("a;b");
            writer.EndRow();
        }

        Assert.Equal("Column1;Column2\n1.5;\"a;b\"\n", output.ToString());
        #endregion
    }

    [Fact]
    public async Task WriteAsync()
    {
        #region CsvWriter_WriteAsync
        using var stream = new MemoryStream();
        await using (var writer = Utf8CsvWriter.Create(stream, leaveOpen: true))
        {
            for (var i = 0; i < 1000; i++)
            {
                writer.WriteField(i);
                writer.WriteField("value");
                await writer.EndRowAsync(); // Sends the buffer to the stream asynchronously when it is mostly full
            }
        }

        Assert.HasCount(1000, Encoding.UTF8.GetString(stream.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        #endregion
    }

    [Fact]
    public void SingleEmptyValue()
    {
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
        {
            writer.WriteRow("");
            writer.WriteRow((string?)null);
            writer.WriteRow("", "");
        }

        Assert.Equal("\"\"\n\"\"\n,\n", output.ToString());
    }

    [Fact]
    public void NoQuoteCharacter()
    {
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { Quote = null, NewLine = "\n" }))
        {
            writer.WriteRow("A\"", "B");
        }

        Assert.Equal("A\",B\n", output.ToString());
    }

    [Fact]
    public void NewLineCharactersAreQuoted()
    {
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "|\n" }))
        {
            writer.WriteRow("a|b", "c");
        }

        Assert.Equal("\"a|b\",c|\n", output.ToString());
    }

    [Fact]
    public void LargeValues()
    {
        var value = new string('a', 100_000) + "\"" + new string('b', 100_000);
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { BufferSize = 16 }))
        {
            writer.WriteRow(value, "x");
        }

        using var reader = CsvReader.CreateFromString(output.ToString(), new CsvReaderOptions { HasHeaderRow = false });
        Assert.True(reader.Read());
        Assert.Equal(value, reader.Current[0].ToString());
        Assert.Equal("x", reader.Current[1].ToString());
    }

    [Fact]
    public void Utf8_NonAscii()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Utf8CsvWriter.Create(buffer, new CsvWriterOptions { NewLine = "\n" }))
        {
            writer.WriteRow("é,ü", "😀", "\uD800");
        }

        Assert.Equal("\"é,ü\",😀,\uFFFD\n", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void Utf8_NonAsciiNewLine()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Utf8CsvWriter.Create(buffer, new CsvWriterOptions { NewLine = "§" }))
        {
            writer.WriteRow("a§b", "c", "é");
        }

        Assert.Equal("\"a§b\",c,\"é\"§", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void BufferWriter()
    {
        var buffer = new ArrayBufferWriter<char>();
        using (var writer = CsvWriter.Create(buffer))
        {
            writer.WriteRow("a", "b");
        }

        Assert.Equal("a,b\r\n", buffer.WrittenSpan.ToString());
    }

    [Fact]
    public void Stream_Encoding()
    {
        using var stream = new MemoryStream();
        using (var writer = CsvWriter.Create(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.WriteRow("é");
        }

        Assert.Equal("\uFEFFé\r\n", Encoding.Unicode.GetString(stream.ToArray()));
    }

    [Fact]
    public void FormatProvider()
    {
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { FormatProvider = new NumberFormatInfo { NumberDecimalSeparator = "," },NewLine = "\n" }))
        {
            writer.WriteField(1.5);
            writer.WriteField(2.5, formatProvider: CultureInfo.InvariantCulture);
            writer.EndRow();
        }

        Assert.Equal("\"1,5\",2.5\n", output.ToString());
    }

    [Fact]
    public void FlushWritesToTarget()
    {
        using var stringWriter = new StringWriter();
        using var writer = CsvWriter.Create(stringWriter, leaveOpen: true);
        writer.WriteRow("a");
        Assert.Equal("", stringWriter.ToString());
        writer.Flush();
        Assert.Equal("a\r\n", stringWriter.ToString());
    }

    [Theory]
    [InlineData(',', ',', "\n")]
    [InlineData('\n', '"', "\n")]
    [InlineData(',', '"', ",")]
    [InlineData(',', '"', "")]
    public void InvalidOptions(char separator, char quote, string newLine)
    {
        Assert.Throws<ArgumentException>(() => CsvWriter.Create(new StringBuilder(), new CsvWriterOptions { Separator = separator, Quote = quote, NewLine = newLine }));
    }

    [Fact]
    public void RoundTrip_SpecialCharacters()
    {
        string?[][] rows =
        [
            ["a", "b,c", "d\"e", "f\r\ng", "h\ri", "j\nk"],
            ["", "\"", "\"\"", ",", "\r\n", " "],
            [""],
            ["😀", "é", "x"],
        ];

        foreach (var utf8 in (bool[])[false, true])
        {
            string text;
            if (utf8)
            {
                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = Utf8CsvWriter.Create(buffer))
                {
                    foreach (var row in rows)
                    {
                        writer.WriteRow(row);
                    }
                }

                text = Encoding.UTF8.GetString(buffer.WrittenSpan);
            }
            else
            {
                var output = new StringBuilder();
                using (var writer = CsvWriter.Create(output))
                {
                    foreach (var row in rows)
                    {
                        writer.WriteRow(row);
                    }
                }

                text = output.ToString();
            }

            using var reader = CsvReader.CreateFromString(text, new CsvReaderOptions { HasHeaderRow = false });
            var actual = new List<string[]>();
            while (reader.Read())
            {
                actual.Add(reader.Current.ToStringArray());
            }

            Assert.Equal(rows.Select(row => string.Join("|", row)), actual.Select(row => string.Join("|", row)));
        }
    }
}
