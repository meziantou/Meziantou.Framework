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
    public void WriteField_MatchesReference()
    {
        // Every option that changes how a value is escaped, with values that need quotes, escaped quotes or an escaped formula, of lengths around the
        // vector sizes, values that are not ASCII, and small buffers that grow while a value is written
        string[] values = ["", "a", "plain", "a,b", "a\"b", "\"", "\"\"", "line\nbreak", "cr\r", "=1+1", "+1", "-1", "@a", "\tx", "\rx", "x;y", "a|b", new string('a', 15), new string('a', 16) + ",", new string('"', 33), new string('b', 70) + "\"" + new string('c', 70), "é", "é,ü", "日\"本😀"];
        CsvWriterOptions[] options =
        [
            new() { NewLine = "\n" },
            new() { NewLine = "\r\n", BufferSize = 1 },
            new() { QuoteMode = CsvQuoteMode.Always, BufferSize = 7 },
            new() { QuoteMode = CsvQuoteMode.Never },
            new() { Quote = null },
            new() { EscapeFormulas = true, BufferSize = 3 },
            new() { EscapeFormulas = true, QuoteMode = CsvQuoteMode.Always },
            new() { Separator = ';', Quote = '\'', NewLine = "|\n" },
            new() { Quote = '\'', EscapeFormulas = true, BufferSize = 1 },
            new() { Quote = '\'', EscapeFormulas = true, QuoteMode = CsvQuoteMode.Always },
        ];

        foreach (var option in options)
        {
            foreach (var first in values)
            {
                foreach (var second in (string?[])[null, .. values])
                {
                    string[] row = second is null ? [first] : [first, second];
                    var output = new StringBuilder();
                    using (var writer = CsvWriter.Create(output, option))
                    {
                        writer.WriteRow(row);
                    }

                    var buffer = new ArrayBufferWriter<byte>();
                    using (var writer = Utf8CsvWriter.Create(buffer, option))
                    {
                        writer.WriteRow(row);
                    }

                    var expected = Reference(row, option);
                    Assert.Equal(expected, output.ToString());
                    Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
                }
            }
        }
    }

    [Fact]
    public void WriteField_SpecialCharacterAtEveryPosition()
    {
        // The plain values are checked while they are copied, by blocks of 8 or 16 elements and an overlapping last block: a special character at
        // every position of values of every length around the block sizes. U+012C and U+0A0D narrow to the bytes of ',' and CR, which must not match.
        char[] specials = [',', '"', '\r', '\n', ';', '\'', 'é', 'Ĭ', '਍'];
        CsvWriterOptions[] options = [new() { NewLine = "\n" }, new() { Separator = ';', Quote = '\'' }, new() { NewLine = "\n", EscapeFormulas = true }];
        foreach (var option in options)
        {
            for (var length = 0; length <= 48; length++)
            {
                var plain = new string('a', length);
                string[] values = [plain, .. specials.SelectMany(special => Enumerable.Range(0, length).Select(position => string.Concat(plain.AsSpan(0, position), [special], plain.AsSpan(position + 1))))];
                foreach (var value in values)
                {
                    string[] row = [value, value];
                    var expected = Reference(row, option);

                    var output = new StringBuilder();
                    using (var writer = CsvWriter.Create(output, option))
                    {
                        writer.WriteRow(row);
                    }

                    Assert.Equal(expected, output.ToString());

                    var buffer = new ArrayBufferWriter<byte>();
                    using (var writer = Utf8CsvWriter.Create(buffer, option))
                    {
                        writer.WriteRow(row);
                        foreach (var field in row)
                        {
                            writer.WriteField(Encoding.UTF8.GetBytes(field));
                        }

                        writer.EndRow();
                    }

                    Assert.Equal(expected + expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
                }
            }
        }
    }

    [Fact]
    public void WriteField_QuotesAtEveryPosition()
    {
        // Quotes at every position of values of every length, apostrophes that are the quote of some options, consecutive quotes, and long values
        CsvWriterOptions[] options = [new() { NewLine = "\n" }, new() { NewLine = "\n", QuoteMode = CsvQuoteMode.Always }, new() { Quote = '\'', EscapeFormulas = true }];
        var values = new List<string>();
        for (var length = 1; length <= 150; length++)
        {
            for (var position = 0; position < length; position += 1 + (length / 40))
            {
                var chars = Enumerable.Range(0, length).Select(i => (char)('a' + (i % 26))).ToArray();
                chars[position] = '"';
                chars[(position * 7) % length] = '\'';
                chars[length - 1 - (position / 2)] = '"';
                values.Add("=" + new string(chars) + ",");
            }
        }

        values.Add(string.Concat(Enumerable.Repeat("x\"'y,", 500)));
        values.Add(new string('"', 2000));
        foreach (var option in options)
        {
            foreach (var value in values)
            {
                string[] row = [value, "a"];
                var expected = Reference(row, option);

                var output = new StringBuilder();
                using (var writer = CsvWriter.Create(output, option))
                {
                    writer.WriteRow(row);
                }

                Assert.Equal(expected, output.ToString());

                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = Utf8CsvWriter.Create(buffer, option))
                {
                    writer.WriteRow(row);
                    writer.WriteField(Encoding.UTF8.GetBytes(value));
                    writer.EndRow();
                }

                Assert.Equal(expected + Reference([value], option), Encoding.UTF8.GetString(buffer.WrittenSpan));
            }
        }
    }

    [Fact]
    public void WriteField_SpecialCharactersOfNumbers()
    {
        // Numbers and dates written with the invariant culture are not searched for characters that need quotes, unless one of their characters is
        // special or every value is quoted
        var negativeTilde = new NumberFormatInfo { NegativeSign = "~" };
        List<(Action<CsvWriter> Write, Action<Utf8CsvWriter> WriteUtf8, Func<IFormatProvider, string> GetText)> values = [];
        Add(-12, "");
        Add(-1234567890123L, "");
        Add(UInt128.MaxValue, "");
        Add(-1.5m, "");
        Add(12.345m, "");
        Add(-12, "D5");
        Add(new DateTime(2021, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc), "O");
        Add(new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Unspecified), "o");
        Add(new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.FromHours(-2)), "O");
        Add(new DateOnly(2021, 3, 4), "O");
        Add(new TimeOnly(5, 6, 7), "O");
        Add(TimeSpan.FromHours(-25.5), "c");
        Add(new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc), "yyyy,MM");
        Add(1.5, "");

        List<CsvWriterOptions> options = [new(), new() { QuoteMode = CsvQuoteMode.Always }, new() { QuoteMode = CsvQuoteMode.Never }, new() { Quote = null }, new() { FormatProvider = negativeTilde, Separator = '~' }];
        foreach (var c in "0123456789+-.:TZ")
        {
            options.Add(new() { Separator = c });
            options.Add(new() { Quote = c });
            options.Add(new() { NewLine = c + "\n" });
        }

        foreach (var option in options)
        {
            foreach (var (write, writeUtf8, getText) in values)
            {
                var output = new StringBuilder();
                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = CsvWriter.Create(output, option))
                using (var utf8Writer = Utf8CsvWriter.Create(buffer, option))
                {
                    writer.WriteField("a");
                    write(writer);
                    writer.EndRow();
                    utf8Writer.WriteField("a");
                    writeUtf8(utf8Writer);
                    utf8Writer.EndRow();
                }

                var expected = Reference(["a", getText(option.FormatProvider ?? CultureInfo.InvariantCulture)], option);
                Assert.Equal(expected, output.ToString());
                Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
            }
        }

        void Add<T>(T value, string format)
            where T : ISpanFormattable, IUtf8SpanFormattable
        {
            values.Add((writer => writer.WriteField(value, format), writer => writer.WriteField(value, format), provider => value.ToString(format, provider)));
        }
    }

    [Fact]
    public void WriteField_Decimal()
    {
        // Decimals have a faster path in the default format of the invariant culture: the text is the text of the BCL
        decimal[] values = [0m, 1m, -1m, 0.5m, -0.05m, 1.50m, 0.00m, 123456789.123456789m, ulong.MaxValue, -(decimal)ulong.MaxValue, decimal.MaxValue, decimal.MinValue, new(0, 0, 0, isNegative: true, scale: 3), new(1, 0, 0, isNegative: false, scale: 28), 12.34m];
        var commaDecimal = new NumberFormatInfo { NumberDecimalSeparator = "," };
        foreach (var value in values)
        {
            var output = new StringBuilder();
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n", BufferSize = 1 }))
            using (var utf8Writer = Utf8CsvWriter.Create(buffer, new CsvWriterOptions { NewLine = "\n", BufferSize = 1 }))
            {
                writer.WriteField(value);
                writer.WriteField(value, "F2");
                writer.WriteField(value, formatProvider: commaDecimal);
                writer.EndRow();
                utf8Writer.WriteField(value);
                utf8Writer.WriteField(value, "F2");
                utf8Writer.WriteField(value, formatProvider: commaDecimal);
                utf8Writer.EndRow();
            }

            var invariant = value.ToString(CultureInfo.InvariantCulture);
            var expected = invariant + "," + value.ToString("F2", CultureInfo.InvariantCulture) + "," + (invariant.Contains('.', StringComparison.Ordinal) ? "\"" + value.ToString(commaDecimal) + "\"" : value.ToString(commaDecimal)) + "\n";
            Assert.Equal(expected, output.ToString());
            Assert.Equal(expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }

    // Quotes a value that contains the separator, the quote, a line terminator or a character of the new line, or every value with Always; doubles
    // the quotes; prefixes an escaped formula with an apostrophe, which is a quote to double when the quote character is the apostrophe
    private static string Reference(string[] row, CsvWriterOptions options)
    {
        var canQuote = options.Quote.HasValue && options.QuoteMode is not CsvQuoteMode.Never;
        var quote = options.Quote.GetValueOrDefault();
        var result = new StringBuilder();
        for (var i = 0; i < row.Length; i++)
        {
            var value = row[i];
            if (i > 0)
            {
                result.Append(options.Separator);
            }

            var formula = options.EscapeFormulas && value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
            var needsQuotes = canQuote && (options.QuoteMode is CsvQuoteMode.Always || (formula && quote is '\'') || value.Contains(options.Separator, StringComparison.Ordinal) || value.Contains(quote, StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.AsSpan().ContainsAny(options.NewLine));
            var content = (formula ? "'" : "") + value;
            result.Append(needsQuotes ? quote + content.Replace(quote.ToString(), new string(quote, 2), StringComparison.Ordinal) + quote : content);
        }

        // A row with a single empty value is quoted, so that it is not read as an empty line
        if (row is [""] && canQuote && options.QuoteMode is not CsvQuoteMode.Always)
        {
            result.Append(quote, 2);
        }

        return result.Append(options.NewLine).ToString();
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
