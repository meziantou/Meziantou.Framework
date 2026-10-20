using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Csv.Fuzz;

/// <summary>
/// <see cref="CsvReader"/> reads any text without throwing, and returns the same rows whatever the number of characters
/// each read of the underlying <see cref="TextReader"/> returns. Its positions are consistent: the row numbers follow the
/// rows, the line number ends at the number of line terminators of the text, the column numbers of the rows add up to the
/// length of the text, and <see cref="CsvReader.EndOfStream"/> says whether another row follows. A row is consistent with
/// its columns. What <see cref="CsvWriter"/> writes reads back as the rows it was given.
/// </summary>
public static partial class CsvFuzzTargets
{
    private static readonly char[] ReaderSeparators = [',', ';', '\t', '|', ' ', '"', '\n', '\uD800'];

    private static readonly char?[] ReaderQuotes = ['"', '\'', null, ',', '\r', '`', 'a', '\uDC00'];

    // Characters the writer can separate values with and quote them with: a separator is never a quote, nor a line terminator
    private static readonly char[] WriterSeparators = [',', ';', '\t', '|', ' ', 'a', '\0', '\uD800'];

    private static readonly char?[] WriterQuotes = ['"', '\'', null, '\uDC00'];

    private static readonly string[] EndOfLines = ["\n", "\r\n", "\r", "\r\n"];

    private static readonly string[] Tokens = [",", ";", "\t", "|", "\"", "\"\"", "'", "\r", "\n", "\r\n", ",\"", "\",", "\"\r\n", "\uD800", "\uDC00", "😀"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("csv-reader", Reader) { Seeds = GetReaderSeeds, Dictionary = Tokens },
        new("csv-roundtrip", RoundTrip) { Seeds = GetRoundTripSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// <c>dialect, chunks, text</c>: <c>dialect</c> picks the separator (bits 0-2), the quote (bits 3-5), and whether the first
    /// row is a header (bit 6); <c>chunks</c> gives the number of characters each read returns. Reading the text from a
    /// <see cref="StringReader"/> and from a <see cref="ChunkedTextReader"/> gives the same rows and positions. The positions
    /// are checked when the separator and the quote are distinct characters that do not end a line.
    /// </summary>
    private static void Reader(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data);
        var sizer = new ChunkSizer(FuzzInput.ReadByte(ref data));
        var text = FuzzInput.ReadText(data);
        var separator = ReaderSeparators[dialect & 0x07];
        var quote = ReaderQuotes[(dialect >> 3) & 0x07];
        var hasHeaderRow = (dialect & 0x40) != 0;

        var expected = ReadAll(new StringReader(text), separator, quote, hasHeaderRow, text.Length, out var lineNumber);
        var actual = ReadAll(new ChunkedTextReader(text, sizer), separator, quote, hasHeaderRow, text.Length, out _);
        FuzzAssert.Equal(string.Join('\n', expected), string.Join('\n', actual), "Reading the text in chunks gives different rows");

        if (separator is '\r' or '\n' || quote is '\r' or '\n' || quote == separator)
            return;

        FuzzAssert.Equal(CountLineTerminators(text), lineNumber, "The line number is not the number of line terminators");
        if (!hasHeaderRow)
        {
            FuzzAssert.Equal(text.Length, expected.Sum(row => row.ColumnNumber), "The column numbers of the rows do not add up to the length of the text");
        }
    }

    /// <summary>
    /// <c>dialect, options, rows</c>: <c>dialect</c> picks the separator (bits 0-2), the quote (bits 3-4), the end of line
    /// (bits 5-6), and whether the rows are written with <see cref="CsvWriter.WriteRowAsync(IEnumerable{string?})"/> or value
    /// by value (bit 7); <c>options</c> says whether the first row is a header (bit 0) and gives the number of characters each
    /// read returns (the other bits). The rows are separated by <c>FF</c> and their values by <c>FE</c>; <c>FD</c> alone is
    /// <see langword="null"/>. Without a quote, the values lose the separator and the line terminators, which cannot be written.
    /// </summary>
    /// <remarks>
    /// Some rows cannot be told apart in CSV: <see langword="null"/> reads back as an empty value, a row with one empty value
    /// as a row without values, and a trailing row without values as no row.
    /// </remarks>
    private static void RoundTrip(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var separator = WriterSeparators[dialect & 0x07];
        var quote = WriterQuotes[(dialect >> 3) & 0x03];
        var endOfLine = EndOfLines[(dialect >> 5) & 0x03];
        var valueByValue = (dialect & 0x80) != 0;
        var hasHeaderRow = (options & 1) != 0;

        var rows = new List<string?[]>();
        while (!data.IsEmpty)
        {
            var rowData = FuzzInput.ReadUntil(ref data, 0xFF);
            var values = new List<string?>();
            while (!rowData.IsEmpty)
            {
                var value = FuzzInput.ReadUntil(ref rowData, 0xFE);
                values.Add(value is [0xFD] ? null : RemoveUnquotable(FuzzText.Decode(value), separator, quote));
            }

            rows.Add([.. values]);
        }

        using var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new CsvWriter(stringWriter) { Separator = separator, Quote = quote, EndOfLine = endOfLine };
        foreach (var row in rows)
        {
            if (valueByValue)
            {
                writer.BeginRowAsync().GetAwaiter().GetResult();
                foreach (var value in row)
                {
                    writer.WriteValueAsync(value).GetAwaiter().GetResult();
                }
            }
            else
            {
                writer.WriteRowAsync(row).GetAwaiter().GetResult();
            }
        }

        var text = stringWriter.ToString();
        var expected = rows.Select(row => row is [null or ""] ? [] : row.Select(value => value ?? "").ToList()).ToList();
        if (expected is [.., []])
        {
            expected.RemoveAt(expected.Count - 1);
        }

        var actual = ReadAll(new ChunkedTextReader(text, new ChunkSizer((byte)(options >> 1))), separator, quote, hasHeaderRow, text.Length, out _);
        var expectedRows = expected.Skip(hasHeaderRow ? 1 : 0).Select(values => Describe(values)).ToList();
        FuzzAssert.Equal(string.Join('\n', expectedRows), string.Join('\n', actual.Select(row => Describe(row.Values))), "The rows read back are not the rows written; text: " + FuzzAssert.Format(text));
        if (hasHeaderRow && expected.Count > 1)
        {
            FuzzAssert.Equal(Describe(expected[0]), Describe(actual[0].ColumnNames!), "The columns read back are not the header written; text: " + FuzzAssert.Format(text));
        }
    }

    private static List<RowSnapshot> ReadAll(TextReader textReader, char separator, char? quote, bool hasHeaderRow, int length, out int lineNumber)
    {
        var reader = new CsvReader(textReader) { Separator = separator, Quote = quote, HasHeaderRow = hasHeaderRow };
        var rows = new List<RowSnapshot>();
        while (true)
        {
            // Each row consumes at least one character
            FuzzAssert.True(rows.Count <= length, "The reader returns more rows than the text has characters");
            var row = reader.ReadRowAsync().GetAwaiter().GetResult();
            if (rows.Count > 0)
            {
                FuzzAssert.Equal(row is null, rows[^1].EndOfStream, "EndOfStream does not say whether another row follows");
            }

            if (row is null)
            {
                FuzzAssert.True(reader.EndOfStream, "EndOfStream is false once the reader returned no row");
                lineNumber = reader.LineNumber;
                return rows;
            }

            FuzzAssert.Equal(rows.Count + (hasHeaderRow ? 1 : 0), reader.RowNumber, "The row number is not the index of the row");
            CheckRow(row, hasHeaderRow);
            rows.Add(new RowSnapshot([.. row.Values], row.Columns?.Select(column => column.Name ?? "").ToArray(), reader.RowNumber, reader.LineNumber, reader.ColumnNumber, reader.EndOfStream));
        }
    }

    private static void CheckRow(CsvRow row, bool hasHeaderRow)
    {
        var values = row.Values;
        for (var i = 0; i < values.Count; i++)
        {
            FuzzAssert.Equal(values[i], row[i], "The indexer and Values disagree");
        }

        ExpectArgumentOutOfRange(() => row[-1]);
        ExpectArgumentOutOfRange(() => row[values.Count]);

        IReadOnlyDictionary<string, string?> dictionary = row;
        var columns = row.Columns;
        FuzzAssert.Equal(hasHeaderRow, columns is not null, "A row has columns exactly when the reader reads a header row");
        if (columns is null)
        {
            FuzzAssert.Equal(values.Count, dictionary.Count, "The dictionary of a row without columns does not count its values");
            try
            {
                _ = row["name"];
                FuzzAssert.Fail("A row without columns returned a value by name");
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        FuzzAssert.Equal(columns.Count, dictionary.Count, "The dictionary of a row does not count its columns");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            FuzzAssert.Equal(i, column.Index, "A column does not have its index");
            var value = i < values.Count ? values[i] : null;
            FuzzAssert.Equal(value, row[column], "The value of a column is not the value at its index");

            var name = column.Name ?? "";
            var first = columns.First(other => string.Equals(other.Name, name, StringComparison.Ordinal));
            FuzzAssert.Equal(row[first], row[name], "The value of a name is not the value of its first column");
            FuzzAssert.True(dictionary.TryGetValue(name, out var found) && string.Equals(found, row[first], StringComparison.Ordinal), "TryGetValue does not find the value of a column");
        }

        static void ExpectArgumentOutOfRange(Func<string?> action)
        {
            try
            {
                _ = action();
                FuzzAssert.Fail("An index out of range returned a value");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
    }

    private static int CountLineTerminators(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\n' || (text[i] is '\r' && (i + 1 >= text.Length || text[i + 1] is not '\n')))
            {
                count++;
            }
        }

        return count;
    }

    private static string RemoveUnquotable(string value, char separator, char? quote)
    {
        if (quote is not null)
            return value;

        return new string([.. value.Where(c => c != separator && c is not '\r' and not '\n')]);
    }

    private static string Describe(IEnumerable<string> values) => "[" + string.Join(", ", values.Select(FuzzText.Escape)) + "]";

    private static IEnumerable<FuzzSeed> GetReaderSeeds()
    {
        var index = 0;
        foreach (var (name, text) in GetTexts())
        {
            // Comma and double quote, with and without a header, read whole or in small chunks; then other dialects
            var dialect = (byte)(index % 4 switch { 0 => 0x00, 1 => 0x40, 2 => 0x09, _ => 0x10 | 0x03 });
            yield return FuzzSeed.FromText(name, [dialect, (byte)(index % 3 is 0 ? 0 : 0x13)], text);
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetRoundTripSeeds()
    {
        var index = 0;
        foreach (var (name, text) in GetTexts())
        {
            // The texts of the tests split into rows on their line terminators and into values on commas
            var data = new List<byte> { (byte)(index % 8 | ((index % 4) << 5)), (byte)(index % 2 | (index % 3 is 0 ? 0 : 0x26)) };
            var lines = text.ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    data.Add(0xFF);
                }

                var values = lines[i].Split(',');
                for (var j = 0; j < values.Length; j++)
                {
                    if (j > 0)
                    {
                        data.Add(0xFE);
                    }

                    data.AddRange(FuzzText.Encode(values[j]));
                }
            }

            yield return new FuzzSeed(name, data.ToArray());
            index++;
        }

        yield return new FuzzSeed("special-values", (byte[])[0x00, 0x00, .. "a,b"u8, 0xFE, .. "\"q\""u8, 0xFE, .. "line\r\nbreak"u8, 0xFE, 0xFE, 0xFD, 0xFF, 0xFD, 0xFF, 0xFF, .. "x"u8]);
        yield return new FuzzSeed("header", (byte[])[0x00, 0x01, .. "A"u8, 0xFE, .. "B"u8, 0xFE, .. "A"u8, 0xFF, .. "1"u8, 0xFE, .. "2"u8, 0xFF, .. "3"u8]);
        yield return new FuzzSeed("no-quote", (byte[])[0x10 | 0x80, 0x22, .. "a,b"u8, 0xFE, .. "c\nd"u8, 0xFF, .. "e\"f"u8]);
        yield return new FuzzSeed("surrogates", (byte[])[0x07 | 0x18 | 0x40, 0x03, .. FuzzText.Encode("\uD800x\uDC00"), 0xFE, .. FuzzText.Encode("\uDC00\uDC00"), 0xFF, .. FuzzText.Encode("😀")]);
    }

    private static IEnumerable<(string Name, string Text)> GetTexts()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in (string[])["CsvReaderTests.cs", "CsvWriterTests.cs", "CsvRowTests.cs"])
        {
            var source = FuzzResources.ReadText(typeof(CsvFuzzTargets).Assembly, file);
            var index = 0;
            foreach (Match match in StringLiteralRegex().Matches(source))
            {
                var text = Regex.Unescape(match.Groups["value"].Value);
                if (text.Length > 0 && seen.Add(text))
                {
                    yield return (string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(file)}-{index++}"), text);
                }
            }
        }

        yield return ("quoted", "a,\"b,c\",\"d\"\"e\"\r\n\"multi\nline\",,\"\"\n\"unterminated");
        yield return ("blank-lines", "\n\r\n\r\ra\n");
        yield return ("quote-in-value", "a\"b,\"c\"d\"\"e");
    }

    [GeneratedRegex("""(?<![@$"\\])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();

    private sealed record RowSnapshot(string[] Values, string[]? ColumnNames, int RowNumber, int LineNumber, int ColumnNumber, bool EndOfStream)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Describe(Values)} columns={(ColumnNames is null ? "null" : Describe(ColumnNames))} row={RowNumber} line={LineNumber} column={ColumnNumber} end={EndOfStream}");
    }
}
