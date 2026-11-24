using System.Text;
using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.Tests;

public class CsvReaderTests
{
    public enum ReaderKind
    {
        Utf16Memory,
        Utf16TextReader,
        Utf16ChunkedTextReader,
        Utf8Memory,
        Utf8Stream,
        Utf8ChunkedStream,
    }

    public static TheoryData<ReaderKind, int> ReaderKinds()
    {
        var data = new TheoryData<ReaderKind, int>();
        foreach (var kind in Enum.GetValues<ReaderKind>())
        {
            foreach (var vectorSize in (int[])[0, 16, 32, 64])
            {
                data.Add(kind, vectorSize);
            }
        }

        return data;
    }

    [Fact]
    public void Read()
    {
        #region CsvReader_Read
        using var reader = CsvReader.CreateFromString("""
            Name,Age
            Alice,30
            Bob,25
            """);

        var lines = new List<string>();
        while (reader.Read())
        {
            var record = reader.Current;
            lines.Add($"{record["Name"].Span} is {record["Age"].Parse<int>()} years old");
        }

        Assert.Equal(["Alice is 30 years old", "Bob is 25 years old"], lines);
        #endregion
    }

    [Fact]
    public void Utf8Read()
    {
        #region Utf8CsvReader_Read
        using var stream = new MemoryStream("Name,Age\nAlice,30\nBob,25"u8.ToArray());
        using var reader = Utf8CsvReader.Create(stream);

        var totalAge = 0;
        var names = new List<string>();
        foreach (var record in reader)
        {
            names.Add(record["Name"].ToString());
            totalAge += record["Age"].Parse<int>(); // Parses the UTF-8 bytes, without decoding them
        }

        Assert.Equal(["Alice", "Bob"], names);
        Assert.Equal(55, totalAge);
        #endregion
    }

    [Fact]
    public async Task ReadFile()
    {
        await File.WriteAllTextAsync("people.csv", "Name,Age\nAlice,30\nBob,25\n");
        try
        {
            #region CsvReader_ReadFile
            await using var reader = CsvReader.CreateFromFile("people.csv");

            var names = new List<string>();
            var totalAge = 0;
            while (await reader.ReadAsync())
            {
                var record = reader.Current;
                names.Add(record["Name"].ToString());
                totalAge += record["Age"].Parse<int>();
            }

            Assert.Equal(["Alice", "Bob"], names);
            Assert.Equal(55, totalAge);
            #endregion
        }
        finally
        {
            File.Delete("people.csv");
        }
    }

    [Fact]
    public void ReadWithoutHeader()
    {
        #region CsvReader_ReadWithoutHeader
        var options = new CsvReaderOptions { HasHeaderRow = false };
        using var reader = CsvReader.CreateFromString("""
            Alice,30,New York
            Bob,25,London
            """, options);

        var lines = new List<string>();
        foreach (var record in reader)
        {
            lines.Add($"{record[0].Span}, {record[1].Span}, {record[^1].Span}");
        }

        Assert.Equal(["Alice, 30, New York", "Bob, 25, London"], lines);
        #endregion
    }

    [Fact]
    public void ReadWithHeader()
    {
        #region CsvReader_ReadWithHeader
        using var reader = CsvReader.CreateFromString("""
            Name,Age
            Alice,30
            """);

        Assert.True(reader.Read());
        var record = reader.Current;

        // The header row is read with the first record
        Assert.Equal(["Name", "Age"], reader.Header!.ColumnNames);
        Assert.Equal(1, reader.Header.IndexOf("Age"));

        // Access by column name or by index
        Assert.Equal("Alice", record["Name"].ToString());
        Assert.Equal("30", record[1].ToString());

        // TryGetField returns false for a column the header does not have
        Assert.False(record.TryGetField("City", out _));

        Assert.False(reader.Read());
        #endregion
    }

    [Fact]
    public void ReadSpans()
    {
        #region CsvReader_ReadSpans
        using var reader = CsvReader.CreateFromString("Id,Amount\n1,10.5\n2,20.25");

        var total = 0m;
        while (reader.Read())
        {
            // The values are slices of the buffer of the reader: reading them does not allocate
            ReadOnlySpan<char> amount = reader.Current["Amount"].Span;
            total += decimal.Parse(amount, CultureInfo.InvariantCulture);
        }

        Assert.Equal(30.75m, total);
        #endregion
    }

    [Fact]
    public void CustomSeparatorAndQuote()
    {
        #region CsvReader_CustomSeparatorAndQuote
        var options = new CsvReaderOptions
        {
            Separator = '\t',      // Tab-separated
            Quote = '\'',          // Single quote instead of double quote
        };
        using var reader = CsvReader.CreateFromString("Name\tComment\nAlice\t'Tab\tand ''quote'''", options);

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.Current["Name"].ToString());
        Assert.Equal("Tab\tand 'quote'", reader.Current["Comment"].ToString());
        #endregion
    }

    [Fact]
    public void DetectSeparator()
    {
        #region CsvReader_DetectSeparator
        var options = new CsvReaderOptions { Separator = null };
        using var reader = CsvReader.CreateFromString("Name;Age\nAlice;30", options);

        Assert.True(reader.Read());
        Assert.Equal(';', reader.Separator);
        Assert.Equal("30", reader.Current["Age"].ToString());
        #endregion
    }

    [Fact]
    public void QuotedValues()
    {
        #region CsvReader_QuotedValues
        using var reader = CsvReader.CreateFromString(""""
            "Value with, comma","Normal value","Value with ""quotes""","Multi-line
            value"
            """", new CsvReaderOptions { HasHeaderRow = false });

        Assert.True(reader.Read());
        var record = reader.Current;
        Assert.Equal("Value with, comma", record[0].ToString());
        Assert.Equal("Normal value", record[1].ToString());
        Assert.Equal("Value with \"quotes\"", record[2].ToString());
        Assert.Equal("Multi-line\nvalue", record[3].ToString(), ignoreLineEndingDifferences: true);
        Assert.True(record[0].IsQuoted);
        Assert.Equal("\"Value with, comma\"", record[0].RawSpan.ToString());
        #endregion
    }

    [Fact]
    public void BlankLinesAndShortRows()
    {
        #region CsvReader_BlankLinesAndShortRows
        using var reader = CsvReader.CreateFromString("Name,Age,City\nAlice,30\n\nBob,25,London");

        Assert.True(reader.Read());
        var alice = reader.Current;
        Assert.Equal(2, alice.ColumnCount);
        Assert.False(alice.TryGetField("City", out _)); // The record has no value for this column

        Assert.True(reader.Read());
        Assert.Equal(0, reader.Current.ColumnCount); // An empty line is a record without columns

        Assert.True(reader.Read());
        Assert.Equal("London", reader.Current["City"].ToString());
        #endregion
    }

    [Fact]
    public void SkipEmptyLinesAndComments()
    {
        #region CsvReader_CommentsAndEmptyLines
        var options = new CsvReaderOptions
        {
            CommentCharacter = '#',
            SkipEmptyLines = true,
        };
        using var reader = CsvReader.CreateFromString("""
            # Exported on 2026-11-21
            Name,Age

            Alice,30
            # Bob left
            Charlie,35
            """, options);

        var names = new List<string>();
        foreach (var record in reader)
        {
            names.Add(record["Name"].ToString());
        }

        Assert.Equal(["Alice", "Charlie"], names);
        #endregion
    }

    [Fact]
    public void Trim()
    {
        #region CsvReader_Trim
        var options = new CsvReaderOptions { HasHeaderRow = false, Trim = CsvTrimOptions.All };
        using var reader = CsvReader.CreateFromString("  a  , \" b,c \" ,d", options);

        Assert.True(reader.Read());
        Assert.Equal(["a", "b,c", "d"], reader.Current.ToStringArray());
        #endregion
    }

    [Fact]
    public void Strict()
    {
        #region CsvReader_Strict
        var options = new CsvReaderOptions { HasHeaderRow = false, Strict = true };
        using var reader = CsvReader.CreateFromString("a,b\"c\nd,e", options);

        var exception = Assert.Throws<CsvException>(() => reader.Read());
        Assert.Equal(1, exception.LineNumber);
        Assert.Equal(1, exception.ColumnIndex);
        #endregion
    }

    [Fact]
    public void Positions()
    {
        #region CsvReader_Positions
        using var reader = CsvReader.CreateFromString("Name,Comment\nAlice,\"Two\nlines\"\nBob,unknown");

        var positions = new List<string>();
        while (reader.Read())
        {
            positions.Add($"record {reader.RowIndex} starts at line {reader.LineNumber}");
        }

        Assert.Equal(["record 0 starts at line 2", "record 1 starts at line 4"], positions);
        #endregion
    }

    [Fact]
    public void ParseValues()
    {
        #region CsvReader_ParseValues
        using var reader = CsvReader.CreateFromString("Id,Price,Day\n1,9.99,monday\nx,1,sunday");

        Assert.True(reader.Read());
        Assert.Equal(1, reader.Current["Id"].Parse<int>());
        Assert.Equal(9.99m, reader.Current["Price"].Parse<decimal>()); // Parsed with the invariant culture
        Assert.Equal(DayOfWeek.Monday, reader.Current["Day"].ParseEnum<DayOfWeek>()); // Names are matched ignoring case

        Assert.True(reader.Read());
        Assert.False(reader.Current["Id"].TryParse<int>(out _));
        var exception = Assert.Throws<CsvException>(() => reader.Current["Id"].Parse<int>());
        Assert.Equal(3, exception.LineNumber);
        Assert.Equal("Id", exception.ColumnName);
        #endregion
    }

    [Theory]
    [InlineData("a\nb\"c\nd")]
    [InlineData("a\n\"b\"c\nd")]
    [InlineData("a\n\"b")]
    public void Strict_ErrorAfterValidRecords(string text)
    {
        // The records before the invalid one are returned first, and the error gives the position of the invalid record
        foreach (var vectorSize in (int[])[0, 16, 32, 64])
        {
            using var reader = CsvReader.CreateFromString(text, new CsvReaderOptions { HasHeaderRow = false, Strict = true, VectorSize = vectorSize });
            Assert.True(reader.Read());
            Assert.Equal("a", reader.Current[0].ToString());
            var exception = Assert.Throws<CsvException>(() => reader.Read());
            Assert.Equal(2, exception.LineNumber);
            Assert.Equal(1, exception.RowIndex);
            Assert.Equal(2, exception.Position);
        }
    }

    [Fact]
    public void BadDataHandler()
    {
        #region CsvReader_BadDataHandler
        var errors = new List<string>();
        var options = new CsvReaderOptions
        {
            BadDataHandler = context =>
            {
                errors.Add($"line {context.LineNumber}, column {context.ColumnName}: {context.Message}");
                return CsvBadDataAction.Skip; // or Accept to read the value as is, or Throw
            },
        };

        using var reader = CsvReader.CreateFromString("Name,Comment\nAlice,ok\nBob,\"quoted\" text\nCharlie,ok", options);
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.Current["Name"].ToString());
        }

        Assert.Equal(["Alice", "Charlie"], names);
        Assert.Equal(["line 3, column Comment: A quoted value is followed by characters that are not a separator."], errors);
        #endregion
    }

    public static TheoryData<string, string> BadDataCases() => new()
    {
        // Each error is "kind row line position column"
        { "a\nb\"c\nd", "QuoteInUnquotedValue 1 2 2 0" },
        { "a\n\"b\"c\nd", "CharactersAfterClosingQuote 1 2 2 0" },
        { "a\n\"b", "UnterminatedQuotedValue 1 2 2 0" },
        { "a\nx,\"y\"z,w\"v\nok", "CharactersAfterClosingQuote 1 2 2 1|QuoteInUnquotedValue 1 2 2 2" },
        { "\"a\nb\"c\n\"d\"\"e\"f", "CharactersAfterClosingQuote 0 1 0 0|CharactersAfterClosingQuote 1 3 7 0" },
        { "a\"b\n# c\"d\n\ne\"f\n", "QuoteInUnquotedValue 0 1 0 0|QuoteInUnquotedValue 2 4 11 0" },
        { "ok\n\"", "UnterminatedQuotedValue 1 2 3 0" },
        { "a,b\"\r\nc\r\n", "QuoteInUnquotedValue 0 1 0 1" },
    };

    [Theory]
    [MemberData(nameof(BadDataCases))]
    public void BadDataHandler_AllReaders(string text, string expectedErrors)
    {
        var baseOptions = new CsvReaderOptions { HasHeaderRow = false, CommentCharacter = '#' };
        foreach (var kind in Enum.GetValues<ReaderKind>())
        {
            foreach (var vectorSize in (int[])[0, 16, 32, 64])
            {
                var options = baseOptions with { VectorSize = vectorSize, BufferSize = kind is ReaderKind.Utf16TextReader or ReaderKind.Utf8Stream ? 2 : CsvReaderOptions.DefaultBufferSize };
                var nonStrict = ReadAll(text, kind, options);

                // Accept: the records are those of non-strict mode, and the handler is called for every error
                var errors = new List<CsvBadDataContext>();
                var accepted = ReadAll(text, kind, options with { BadDataHandler = context => { errors.Add(context); return CsvBadDataAction.Accept; } });
                Assert.Equal(Describe(nonStrict), Describe(accepted));
                Assert.Equal(expectedErrors, string.Join('|', errors.Select(DescribeError)));

                // Skip: the records are those of non-strict mode, without the records that have an error
                var skippedRows = errors.Select(e => e.RowIndex!.Value).ToHashSet();
                var skipErrors = new List<CsvBadDataContext>();
                var skipped = ReadAll(text, kind, options with { BadDataHandler = context => { skipErrors.Add(context); return CsvBadDataAction.Skip; } });
                Assert.Equal(Describe([.. nonStrict.Where((_, index) => !skippedRows.Contains(index))]), Describe(skipped));
                Assert.Equal(string.Join('|', errors.DistinctBy(e => e.RowIndex).Select(DescribeError)), string.Join('|', skipErrors.Select(DescribeError)));

                // Throw: the same exception as strict mode, even when strict mode is off
                var strictException = Assert.Throws<CsvException>(() => ReadAll(text, kind, options with { Strict = true }));
                var exception = Assert.Throws<CsvException>(() => ReadAll(text, kind, options with { BadDataHandler = _ => CsvBadDataAction.Throw }));
                Assert.Equal(strictException.Message, exception.Message);
                Assert.Equal(strictException.Position, exception.Position);
            }
        }

        static string DescribeError(CsvBadDataContext context) => string.Create(CultureInfo.InvariantCulture, $"{context.Kind} {context.RowIndex} {context.LineNumber} {context.Position} {context.ColumnIndex}");
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void BadDataHandler_ManyRecords(ReaderKind kind, int vectorSize)
    {
        // More records than a batch: the errors after the first record of a batch restart the scan from the invalid record
        var sb = new StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            if (i % 7 == 3)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{i},x\"y\n");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"{i},\"v{i}\"\n");
            }
        }

        var text = sb.ToString();
        var errors = new List<long>();
        var options = new CsvReaderOptions { HasHeaderRow = false, VectorSize = vectorSize, BufferSize = 64 };
        var records = new List<(long LineNumber, string[] Values)>();
        var skipOptions = options with { BadDataHandler = context => { errors.Add(context.RowIndex!.Value); return CsvBadDataAction.Skip; } };
        ReadRecords(text, kind, skipOptions, (lineNumber, values) => records.Add((lineNumber, values)));

        var expectedErrors = Enumerable.Range(0, 400).Where(i => i % 7 == 3).Select(i => (long)i).ToList();
        Assert.Equal(expectedErrors, errors);
        Assert.HasCount(400 - expectedErrors.Count, records);
        foreach (var (lineNumber, values) in records)
        {
            Assert.Equal(lineNumber - 1, long.Parse(values[0], CultureInfo.InvariantCulture));
            Assert.Equal("v" + values[0], values[1]);
        }
    }

    [Fact]
    public void BadDataHandler_RowIndexCountsSkippedRecords()
    {
        var options = new CsvReaderOptions { BadDataHandler = _ => CsvBadDataAction.Skip };
        using var reader = CsvReader.CreateFromString("h\na\nb\"\nc", options);
        Assert.True(reader.Read());
        Assert.Equal(0, reader.RowIndex);
        Assert.True(reader.Read());
        Assert.Equal("c", reader.Current[0].ToString());
        Assert.Equal(2, reader.RowIndex);
        Assert.Equal(4, reader.LineNumber);
        Assert.False(reader.Read());
    }

    [Fact]
    public void BadDataHandler_HeaderRow()
    {
        var errors = new List<CsvBadDataContext>();
        var options = new CsvReaderOptions { BadDataHandler = context => { errors.Add(context); return CsvBadDataAction.Skip; } };
        using (var reader = CsvReader.CreateFromString("a\"b,c\nName\nAlice", options))
        {
            // The header row is skipped: the next row is the header row
            Assert.True(reader.Read());
            Assert.Equal("Name", reader.Header!.ColumnNames[0]);
            Assert.Equal("Alice", reader.Current["Name"].ToString());
            Assert.Equal(0, reader.RowIndex);
        }

        Assert.Null(Assert.Single(errors).RowIndex);
        Assert.Null(errors[0].ColumnName);

        // The errors of the records have the name of the column
        errors.Clear();
        using (var reader = CsvReader.CreateFromString("Name,Comment\nAlice,a\"b", options))
        {
            Assert.False(reader.Read());
        }

        Assert.Equal("Comment", Assert.Single(errors).ColumnName);
        Assert.Equal(1, errors[0].ColumnIndex);
        Assert.Equal(0, errors[0].RowIndex);
        Assert.Equal(CsvBadDataKind.QuoteInUnquotedValue, errors[0].Kind);
        Assert.Equal("Comment", errors[0].Exception.ColumnName);
        Assert.Equal(2, errors[0].Exception.LineNumber);
    }

    [Fact]
    public void BadDataHandler_ColumnCount()
    {
        var errors = new List<CsvBadDataContext>();
        foreach (var kind in Enum.GetValues<ReaderKind>())
        {
            errors.Clear();
            var options = new CsvReaderOptions { ValidateColumnCount = true, BadDataHandler = context => { errors.Add(context); return context.RowIndex == 1 ? CsvBadDataAction.Accept : CsvBadDataAction.Skip; } };
            var records = ReadAll("a,b\n1,2\n3\n4,5,6\n7,8", kind, options);
            Assert.Equal("[1|2]\n[3]\n[7|8]", Describe(records));
            Assert.Equal([1L, 2L], errors.Select(e => e.RowIndex!.Value));
            Assert.All(errors, e => Assert.Equal(CsvBadDataKind.ColumnCountMismatch, e.Kind));
            Assert.Equal([3L, 4L], errors.Select(e => e.LineNumber));
            Assert.Equal("The record has 1 columns, but 2 were expected.", errors[0].Message);
            Assert.Null(errors[0].ColumnIndex);
        }

        var exception = Assert.Throws<CsvException>(() => ReadAll("a\n1\n2,3", ReaderKind.Utf8Memory, new CsvReaderOptions { ValidateColumnCount = true, BadDataHandler = _ => CsvBadDataAction.Throw }));
        Assert.Equal(1, exception.RowIndex);
        Assert.Equal(3, exception.LineNumber);
    }

    [Fact]
    public void BadDataHandler_Invalid()
    {
        var options = new CsvReaderOptions { HasHeaderRow = false, BadDataHandler = _ => (CsvBadDataAction)42 };
        using var reader = CsvReader.CreateFromString("a\"b", options);
        Assert.Throws<InvalidOperationException>(() => reader.Read());

        // The exceptions of the handler end the reading
        using var reader2 = CsvReader.CreateFromString("a\"b", options with { BadDataHandler = _ => throw new FormatException() });
        Assert.Throws<FormatException>(() => reader2.Read());
    }

    [Fact]
    public void BadDataHandler_ValidDataIsNotReported()
    {
        // Valid data, including quotes at the edges of the buffer, is read the same with a handler
        var options = new CsvReaderOptions { HasHeaderRow = false, BufferSize = 1, BadDataHandler = _ => throw new InvalidOperationException("No error expected") };
        foreach (var kind in Enum.GetValues<ReaderKind>())
        {
            Assert.Equal("[a|b\"c|d]\n[e\\nf| g ]", Describe(ReadAll("a,\"b\"\"c\",d\r\n\"e\nf\",\" g \"", kind, options)));
        }
    }

    [Fact]
    public void MaxRowLength_ErrorAfterValidRecords()
    {
        var options = new CsvReaderOptions { HasHeaderRow = false, MaxRowLength = 10 };
        using var reader = CsvReader.Create(new StringReader("short\n" + new string('a', 11) + "\nb"), options);
        Assert.True(reader.Read());
        Assert.Equal("short", reader.Current[0].ToString());
        var exception = Assert.Throws<CsvException>(() => reader.Read());
        Assert.Equal(2, exception.LineNumber);
    }

    [Fact]
    public void ManyRecords()
    {
        // More records than a batch, with comments and empty lines between them
        var sb = new StringBuilder();
        for (var i = 0; i < 1000; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{i},\"v{i}\"\n");
            if (i % 7 == 0)
            {
                sb.Append("# comment\n\n");
            }
        }

        foreach (var bufferSize in (int[])[1, 100, 16384])
        {
            using var reader = CsvReader.Create(new StringReader(sb.ToString()), new CsvReaderOptions { HasHeaderRow = false, CommentCharacter = '#', SkipEmptyLines = true, BufferSize = bufferSize });
            var count = 0;
            while (reader.Read())
            {
                Assert.Equal(count, reader.Current[0].Parse<int>());
                Assert.Equal("v" + count.ToString(CultureInfo.InvariantCulture), reader.Current[1].ToString());
                count++;
            }

            Assert.Equal(1000, count);
        }
    }

    [Fact]
    public void ValidateColumnCount()
    {
        var options = new CsvReaderOptions { ValidateColumnCount = true };
        using var reader = CsvReader.CreateFromString("a,b\n1,2\n3", options);
        Assert.True(reader.Read());
        var exception = Assert.Throws<CsvException>(() => reader.Read());
        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(1, exception.RowIndex);
    }

    [Fact]
    public void MaxRowLength()
    {
        var options = new CsvReaderOptions { HasHeaderRow = false, BufferSize = 4, MaxRowLength = 10 };
        using var reader = CsvReader.Create(new StringReader("short\n\"" + new string('a', 100)), options);
        Assert.True(reader.Read());
        Assert.Throws<CsvException>(() => reader.Read());
    }

    [Fact]
    public void MaxRowLength_RowInBuffer()
    {
        var options = new CsvReaderOptions { HasHeaderRow = false, MaxRowLength = 10 };
        using var reader = CsvReader.Create(new StringReader(new string('a', 11) + "\nb"), options);
        Assert.Throws<CsvException>(() => reader.Read());
    }

    [Fact]
    public void ByteOrderMark()
    {
        using var reader = Utf8CsvReader.Create((byte[])[0xEF, 0xBB, 0xBF, .. "A\n1"u8]);
        Assert.True(reader.Read());
        Assert.Equal(["A"], reader.Header!.ColumnNames);

        using var utf16Reader = CsvReader.CreateFromString("\uFEFFA\n1");
        Assert.True(utf16Reader.Read());
        Assert.Equal(["A"], utf16Reader.Header!.ColumnNames);
    }

    [Fact]
    public void StreamEncoding()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Name\nÉlise")).ToArray();
        using var reader = CsvReader.Create(new MemoryStream(bytes));
        Assert.True(reader.Read());
        Assert.Equal("Élise", reader.Current["Name"].ToString());
    }

    [Fact]
    public void InvalidUtf8IsReplaced()
    {
        using var reader = Utf8CsvReader.Create((byte[])[(byte)'a', 0xFF, (byte)'b'], new CsvReaderOptions { HasHeaderRow = false });
        Assert.True(reader.Read());
        Assert.Equal("a\uFFFDb", reader.Current[0].ToString());
    }

    [Fact]
    public void Utf8ValuesAreDecodedAsUtf8()
    {
        // ASCII values have a faster path: values of every length up to a few vectors, ASCII, or with a non-ASCII character or an invalid byte at
        // each position, decode as Encoding.UTF8 does
        var options = new CsvReaderOptions { HasHeaderRow = false };
        for (var length = 0; length <= 70; length++)
        {
            var ascii = Enumerable.Range(0, length).Select(i => (byte)('a' + (i % 26))).ToArray();
            List<byte[]> values = [ascii];
            for (var position = 0; position < length; position++)
            {
                values.Add([.. ascii[..position], 0xFF, .. ascii[(position + 1)..]]);
                values.Add([.. ascii[..position], .. "\u00E9"u8, .. ascii[(position + 1)..]]);
                values.Add([.. ascii[..position], .. "\u65E5"u8, .. ascii[(position + 1)..]]);
            }

            foreach (var value in values)
            {
                byte[] data = [.. "x,"u8, .. value];
                using var reader = Utf8CsvReader.Create(data, options);
                Assert.True(reader.Read());
                Assert.Equal(Encoding.UTF8.GetString(value), reader.Current[1].ToString());
                Assert.Equal(Encoding.UTF8.GetString(data), reader.Current.ToString());
                Assert.Equal(Encoding.UTF8.GetString(value), Assert.Single(CsvSerializer.Deserialize<Person>((byte[])[.. "Name,Age\n"u8, .. value, .. ",1\n"u8])).Name);
            }
        }
    }

    [Fact]
    public void Utf8ValuesOfLongRecordsAreDecodedAsUtf8()
    {
        // Values of many lengths in long records, read in any order and read again, with values that are quoted, escaped, not ASCII or not valid
        // UTF-8, decode as Encoding.UTF8 does
        string[] specials = ["\"q\"", "\"a,\"\"b\"", "été", "日", ""];
        for (var length = 1; length < 300; length += 7)
        {
            var values = new List<byte[]>();
            for (var i = 0; i < 40; i++)
            {
                var value = i % 9 == 4 ? specials[i % specials.Length] : new string((char)('a' + (i % 26)), (length * (i + 1)) % 301);
                values.Add(i % 13 == 7 ? [.. Encoding.UTF8.GetBytes(value), 0xFF] : Encoding.UTF8.GetBytes(value));
            }

            var data = values.SelectMany((value, i) => i == 0 ? value : [(byte)',', .. value]).ToArray();
            var expected = ReadAll(Encoding.UTF8.GetString(data), ReaderKind.Utf16Memory, new CsvReaderOptions { HasHeaderRow = false }).Single();
            foreach (var order in (int[][])[[.. Enumerable.Range(0, 40)], [.. Enumerable.Range(0, 40).Reverse()], [.. Enumerable.Range(0, 40).Select(i => (i * 17) % 40)]])
            {
                using var reader = Utf8CsvReader.Create(data, new CsvReaderOptions { HasHeaderRow = false });
                Assert.True(reader.Read());
                foreach (var column in order)
                {
                    Assert.Equal(expected[column], reader.Current[column].ToString());
                }

                // A value read again gives the same string
                Assert.Equal(expected[order[0]], reader.Current[order[0]].ToString());
            }
        }
    }

    [Fact]
    public void PoolStrings()
    {
        var options = new CsvReaderOptions { HasHeaderRow = false, PoolStrings = true };
        using var reader = CsvReader.CreateFromString("abc\nabc", options);
        Assert.True(reader.Read());
        var first = reader.Current[0].ToString();
        Assert.True(reader.Read());
        Assert.Same(first, reader.Current[0].ToString());
    }

    [Fact]
    public void CurrentWithoutRead()
    {
        using var reader = CsvReader.CreateFromString("a");
        Assert.Throws<InvalidOperationException>(() => _ = reader.Current);
    }

    [Fact]
    public void ReadByNameWithoutHeader()
    {
        using var reader = CsvReader.CreateFromString("a", new CsvReaderOptions { HasHeaderRow = false });
        Assert.True(reader.Read());
        Assert.Throws<InvalidOperationException>(() => { _ = reader.Current["a"]; });
    }

    [Fact]
    public void ReadUnknownColumn()
    {
        using var reader = CsvReader.CreateFromString("a\n1");
        Assert.True(reader.Read());
        Assert.Throws<KeyNotFoundException>(() => { _ = reader.Current["b"]; });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = reader.Current[1]; });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void StrictError_AfterColumnsGrow_ReturnsPooledArraysOnce(int vectorSize)
    {
        // The record has more columns than the first array of column ends: the error is thrown after the array is replaced by a larger one
        var text = string.Join(',', Enumerable.Range(0, 1000)) + ",a\"b\n";
        using (var reader = CsvReader.CreateFromString(text, new CsvReaderOptions { HasHeaderRow = false, Strict = true, VectorSize = vectorSize }))
        {
            Assert.Throws<CsvException>(() => reader.Read());
        }

        // An array returned twice to the pool is rented twice
        var rented = new int[64][];
        for (var i = 0; i < rented.Length; i++)
        {
            rented[i] = System.Buffers.ArrayPool<int>.Shared.Rent(256);
        }

        try
        {
            Assert.HasCount(rented.Length, rented.Distinct(ReferenceEqualityComparer.Instance));
        }
        finally
        {
            foreach (var array in rented)
            {
                System.Buffers.ArrayPool<int>.Shared.Return(array);
            }
        }
    }

    [Fact]
    public void DuplicateColumnNames()
    {
        using var reader = CsvReader.CreateFromString("a,a\n1,2");
        Assert.True(reader.Read());
        Assert.Equal("1", reader.Current["a"].ToString());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(100)]
    public void HeaderLookup(int columnCount)
    {
        // Small headers are searched linearly, large ones with a dictionary: both find the first column with a name, with the comparer
        var names = Enumerable.Range(0, columnCount).Select(i => "c" + (i % (columnCount - 1)).ToString(CultureInfo.InvariantCulture)).ToArray();
        var text = string.Join(',', names) + "\n" + string.Join(',', Enumerable.Range(0, columnCount));
        using var reader = CsvReader.CreateFromString(text, new CsvReaderOptions { ColumnNameComparer = StringComparer.OrdinalIgnoreCase });
        Assert.True(reader.Read());
        for (var i = 0; i < columnCount - 1; i++)
        {
            var name = "C" + i.ToString(CultureInfo.InvariantCulture);
            Assert.Equal(i, reader.Header!.IndexOf(name));
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture), reader.Current[name].ToString());
        }

        Assert.Equal(-1, reader.Header!.IndexOf("missing"));
        Assert.False(reader.Header.TryGetIndex("missing", out _));
        Assert.False(reader.Current.TryGetField("missing", out _));
    }

    [Fact]
    public void ColumnNameComparer()
    {
        using var reader = CsvReader.CreateFromString("Name\nAlice", new CsvReaderOptions { ColumnNameComparer = StringComparer.OrdinalIgnoreCase });
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.Current["NAME"].ToString());
    }

    [Theory]
    [InlineData('\n')]
    [InlineData('\r')]
    public void InvalidSeparator(char separator)
    {
        Assert.Throws<ArgumentException>(() => CsvReader.CreateFromString("a", new CsvReaderOptions { Separator = separator }));
    }

    [Fact]
    public void Utf8RequiresAsciiSeparator()
    {
        Assert.Throws<ArgumentException>(() => Utf8CsvReader.Create(Array.Empty<byte>(), new CsvReaderOptions { Separator = '§' }));
    }

    [Fact]
    public void NonAsciiSeparator()
    {
        using var reader = CsvReader.CreateFromString("a•b\u012Cc•\"d•e\"", new CsvReaderOptions { HasHeaderRow = false, Separator = '•' });
        Assert.True(reader.Read());
        Assert.Equal(["a", "b\u012Cc", "d•e"], reader.Current.ToStringArray());
    }

    [Fact]
    public async Task ReadAsync_Cancellation()
    {
        using var reader = CsvReader.Create(new StringReader("a\n1"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadAsync(cts.Token));
    }

    [Fact]
    public void Dispose_LeaveOpen()
    {
        var stream = new MemoryStream("a\n1"u8.ToArray());
        using (var reader = Utf8CsvReader.Create(stream, leaveOpen: true))
        {
            Assert.True(reader.Read());
        }

        Assert.True(stream.CanRead);
        using (Utf8CsvReader.Create(stream))
        {
        }

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void ReadAfterDispose()
    {
        var reader = CsvReader.CreateFromString("a\n1");
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void Scenarios(ReaderKind kind, int vectorSize)
    {
        foreach (var (input, expected) in GetScenarios())
        {
            var actual = ReadAll(input, kind, new CsvReaderOptions { HasHeaderRow = false, VectorSize = vectorSize, BufferSize = 7 });
            Assert.Equal(Describe(expected), Describe(actual), $"Input: {input.ReplaceLineEndings("\\n")}");
        }
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void AllPositions(ReaderKind kind, int vectorSize)
    {
        // A special character at every position around the 16, 32 and 64 element blocks
        string[] specials = [",", "\"", "\r\n", "\n", "\r", "\"\"", "é", "\u012C", "\u7FFF", "\u8000", "\uFF0C", "\uFFFF"];
        foreach (var special in specials)
        {
            for (var length = 1; length < 140; length++)
            {
                for (var position = 0; position < length; position += 1 + (length / 20))
                {
                    var sb = new StringBuilder();
                    for (var i = 0; i < length; i++)
                    {
                        sb.Append(i == position ? special : ((char)('a' + (i % 26))).ToString());
                    }

                    sb.Append(",x\"y\",\"z\"\"w\"\n\"q");
                    var text = sb.ToString();
                    var expected = ReferenceParser.Parse(text, ',', '"');
                    var actual = ReadAll(text, kind, new CsvReaderOptions { HasHeaderRow = false, VectorSize = vectorSize, BufferSize = 1 + (length % 13) });
                    Assert.Equal(Describe(expected), Describe(actual), $"Input: {text.ReplaceLineEndings("\\n")}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void QuotedValuesFollowedBySeparators(ReaderKind kind, int vectorSize)
    {
        // Quoted values that close anywhere in a SIMD block, followed by separators in the same block and the next ones: quoted separators, escaped
        // quotes, a quote in a value that is not quoted, and a closing quote followed by other characters
        for (var length = 0; length < 140; length++)
        {
            var value = new string('a', length);
            var text = "\"" + value + "\",b,c,\"d,e\",f\"g,h,\"i\"\"j\",k,\"l\"m,n,o\n" +
                "p,\"" + value + ",\"\"\",q,r,s,t,u,v,w,x,y,z\r\n" +
                "\"" + value + "\"\n\"\",\"\",,\"\"\"\",\"\r\n\",1,2,3,4,5,6,7,8,9";
            var expected = ReferenceParser.Parse(text, ',', '"');
            var actual = ReadAll(text, kind, new CsvReaderOptions { HasHeaderRow = false, VectorSize = vectorSize, BufferSize = 1 + (length % 13) });
            Assert.Equal(Describe(expected), Describe(actual), $"Input: {text.ReplaceLineEndings("\\n")}");
        }
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void EscapedQuotesAtAllPositions(ReaderKind kind, int vectorSize)
    {
        // Doubled quotes at every position of quoted values of every length, before and after quoted values without doubled quotes in the same
        // record, with text after the closing quote, and a value that is not closed
        var options = new CsvReaderOptions { HasHeaderRow = false, VectorSize = vectorSize };
        for (var length = 0; length < 150; length++)
        {
            var sb = new StringBuilder();
            for (var position = 0; position <= length; position += 1 + (length / 30))
            {
                var value = new string('a', position) + "\"\"" + new string('b', length - position);
                sb.Append('"').Append(value).Append("\",\"").Append(value).Append("\"\"\"").Append(value).Append("\"c,\"\"\"\",x\n");
            }

            sb.Append('"').Append('d', length).Append("\"\"e");
            var text = sb.ToString();
            var expected = ReferenceParser.Parse(text, ',', '"');
            var actual = ReadAll(text, kind, options);
            Assert.Equal(Describe(expected), Describe(actual), $"Input: {text.ReplaceLineEndings("\\n")}");
        }
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void NullSeparatorAndQuote(ReaderKind kind, int vectorSize)
    {
        // The SIMD path narrows the code units from U+8000 to 0x00: a separator or a quote U+0000 must not match them
        for (var length = 1; length < 140; length += 7)
        {
            var value = string.Concat(Enumerable.Range(0, length).Select(i => (char)(i % 3 == 0 ? 0x8000 + i : 'a' + (i % 26))));
            var text = value + "\0" + value + "\n" + value + "\0\"" + value + "\0\"\n";
            var expected = ReferenceParser.Parse(text, '\0', '"');
            var actual = ReadAll(text, kind, new CsvReaderOptions { HasHeaderRow = false, Separator = '\0', VectorSize = vectorSize });
            Assert.Equal(Describe(expected), Describe(actual), $"Length: {length}");

            text = value + ";\0" + value + ";" + value + "\0\0\0\n";
            expected = ReferenceParser.Parse(text, ';', '\0');
            actual = ReadAll(text, kind, new CsvReaderOptions { HasHeaderRow = false, Separator = ';', Quote = '\0', VectorSize = vectorSize });
            Assert.Equal(Describe(expected), Describe(actual), $"Length: {length}");
        }
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void CommentsAtAllPositions(ReaderKind kind, int vectorSize)
    {
        // Comment lines of every length after records of every length, so that they start and end anywhere in a SIMD block
        for (var recordLength = 0; recordLength < 70; recordLength += 3)
        {
            for (var commentLength = 0; commentLength < 70; commentLength += 5)
            {
                var record = new string('a', recordLength) + ",b";
                var text = record + "\n#" + new string('c', commentLength) + ",\"x\n" + record + "\r\n#\r\n" + record + ",\"q,\"\n";
                var expected = ReferenceParser.Parse(text, ',', '"', '#');
                var actual = ReadAll(text, kind, new CsvReaderOptions { HasHeaderRow = false, CommentCharacter = '#', VectorSize = vectorSize, BufferSize = 1 + (commentLength % 11) });
                Assert.Equal(Describe(expected), Describe(actual), $"Input: {text.ReplaceLineEndings("\\n")}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    public void LineNumbers(ReaderKind kind, int vectorSize)
    {
        var text = "a,\"b\r\nc\"\r\nd\n\"e\rf\"\rg";
        var lineNumbers = new List<long>();
        ReadRecords(text, kind, new CsvReaderOptions { HasHeaderRow = false, VectorSize = vectorSize, BufferSize = 3 }, (lineNumber, _) => lineNumbers.Add(lineNumber));
        Assert.Equal([1, 3, 4, 6], lineNumbers);
    }

    [Theory]
    [MemberData(nameof(ReaderKinds))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void RandomRecordsReadAsWithoutSimd(ReaderKind kind, int vectorSize)
    {
        // The SIMD scan reads the regular records and leaves the others to the general scan, from their start: records of every shape, whose quotes
        // and line terminators fall anywhere in the blocks, give the same values, line numbers and errors as without SIMD
        string[] values = ["", "a", "abc", "\"\"", "\"a\"", "\"a,b\"", "\"a\"\"b\"", "\"\"\"\"", "\"a\r\nb\"", "\"a\nb\"", "a\"b", "\"a\"b", " \"a\" ", "\"a", "#a", "é😀", new string('x', 40), "\"" + new string('y', 70) + "\""];
        string[] terminators = ["\n", "\r\n", "\r", "\n\n"];
        CsvReaderOptions[] optionsList =
        [
            new() { HasHeaderRow = false },
            new() { HasHeaderRow = false, Strict = true },
            new() { HasHeaderRow = false, BadDataHandler = _ => CsvBadDataAction.Skip },
            new() { HasHeaderRow = false, SkipEmptyLines = true },
            new() { HasHeaderRow = false, Trim = CsvTrimOptions.All },
            new() { HasHeaderRow = false, CommentCharacter = '#' },
            new() { HasHeaderRow = false, MaxRowLength = 150, BufferSize = 16 },
        ];

        var random = new Random(42);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            // The quotes that make a record irregular are rare in most documents: the general scan reads these records, and the next ones go back to
            // the SIMD scan
            var irregular = random.Next(4) == 0 ? values.Length : 13;
            var sb = new StringBuilder();
            var recordCount = random.Next(1, 200);
            for (var record = 0; record < recordCount; record++)
            {
                var columnCount = random.Next(0, 10);
                for (var column = 0; column < columnCount; column++)
                {
                    if (column > 0)
                    {
                        sb.Append(',');
                    }

                    var value = values[random.Next(random.Next(20) == 0 ? values.Length : irregular)];
                    sb.Append(value);
                }

                sb.Append(terminators[random.Next(terminators.Length)]);
            }

            var text = sb.ToString();
            foreach (var options in optionsList)
            {
                var expected = ReadSnapshot(text, kind, options with { VectorSize = 0 });
                var actual = ReadSnapshot(text, kind, options with { VectorSize = vectorSize });
                Assert.Equal(expected, actual, $"Input: {text.ReplaceLineEndings("\\n")}");
            }
        }
    }

    // The values and line numbers of the records, and the error that stops the reading
    private static string ReadSnapshot(string text, ReaderKind kind, CsvReaderOptions options)
    {
        var sb = new StringBuilder();
        try
        {
            ReadRecords(text, kind, options, (lineNumber, values) => sb.Append(lineNumber).Append(':').Append(Describe([values])).Append('\n'));
        }
        catch (CsvException ex)
        {
            sb.Append("Error ").Append(ex.RowIndex).Append(' ').Append(ex.LineNumber).Append(' ').Append(ex.ColumnIndex);
        }

        return sb.ToString();
    }

    private static IEnumerable<(string Input, string[][] Expected)> GetScenarios()
    {
        yield return ("", []);
        yield return ("\n", [[]]);
        yield return ("a", [["a"]]);
        yield return ("a\n", [["a"]]);
        yield return ("a\r\nb\rc\nd", [["a"], ["b"], ["c"], ["d"]]);
        yield return (",", [["", ""]]);
        yield return ("a,b,", [["a", "b", ""]]);
        yield return ("\n\na", [[], [], ["a"]]);
        yield return ("\"\"", [[""]]);
        yield return ("\"a,b\",c", [["a,b", "c"]]);
        yield return ("\"a\"\"b\"", [["a\"b"]]);
        yield return ("\"a\nb\"\nc", [["a\nb"], ["c"]]);
        yield return ("\"a\r\nb\"", [["a\r\nb"]]);
        yield return ("a\"b,c", [["a\"b", "c"]]);
        yield return ("\"a\"b\"c\"", [["ab\"c\""]]);
        yield return ("\"unterminated,\nvalue", [["unterminated,\nvalue"]]);
        yield return ("\"a\r", [["a\r"]]);
        yield return ("a,\"\"\"\"", [["a", "\""]]);
        yield return ("é,ü\n😀,\"x😀\"", [["é", "ü"], ["😀", "x😀"]]);
    }

    private static string[][] ReadAll(string text, ReaderKind kind, CsvReaderOptions options)
    {
        var result = new List<string[]>();
        ReadRecords(text, kind, options, (_, values) => result.Add(values));
        return [.. result];
    }

    private static void ReadRecords(string text, ReaderKind kind, CsvReaderOptions options, Action<long, string[]> onRecord)
    {
        switch (kind)
        {
            case ReaderKind.Utf16Memory:
                using (var reader = CsvReader.CreateFromString(text, options))
                {
                    while (reader.Read())
                    {
                        onRecord(reader.LineNumber, reader.Current.ToStringArray());
                    }
                }

                break;

            case ReaderKind.Utf16TextReader:
            case ReaderKind.Utf16ChunkedTextReader:
                using (var reader = CsvReader.Create(kind is ReaderKind.Utf16TextReader ? new StringReader(text) : new ChunkedTextReader(text), options))
                {
                    while (reader.ReadAsync().AsTask().GetAwaiter().GetResult())
                    {
                        onRecord(reader.LineNumber, reader.Current.ToStringArray());
                    }
                }

                break;

            case ReaderKind.Utf8Memory:
                using (var reader = Utf8CsvReader.Create(Encoding.UTF8.GetBytes(text), options))
                {
                    while (reader.Read())
                    {
                        onRecord(reader.LineNumber, reader.Current.ToStringArray());
                    }
                }

                break;

            default:
                var bytes = Encoding.UTF8.GetBytes(text);
                using (var reader = Utf8CsvReader.Create(kind is ReaderKind.Utf8Stream ? new MemoryStream(bytes) : new ChunkedStream(bytes), options))
                {
                    while (reader.Read())
                    {
                        onRecord(reader.LineNumber, reader.Current.ToStringArray());
                    }
                }

                break;
        }
    }

    private static string Describe(string[][] records) => string.Join("\n", records.Select(record => "[" + string.Join("|", record.Select(value => value.ReplaceLineEndings("\\n").Replace("\r", "\\r", StringComparison.Ordinal))) + "]"));

    private sealed class ChunkedTextReader(string text) : TextReader
    {
        private int _position;

        public override int Read(Span<char> buffer)
        {
            if (_position >= text.Length || buffer.IsEmpty)
                return 0;

            var count = Math.Min(Math.Min(buffer.Length, 1 + (_position % 3)), text.Length - _position);
            text.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));
    }

    private sealed class ChunkedStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1 + (int)(Position % 3))]);
    }
}
