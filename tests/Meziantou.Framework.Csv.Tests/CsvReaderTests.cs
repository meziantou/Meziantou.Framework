using System.Text;

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
