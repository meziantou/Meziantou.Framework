using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.Csv.Serialization;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Csv.Fuzz;

/// <summary>
/// <see cref="CsvReader"/> and <see cref="Utf8CsvReader"/> read any data without throwing, and return the same records whatever the SIMD path, the
/// size of the buffer and the number of elements each read of the source returns, as a character-by-character reference parser. What the writers
/// write reads back as the rows they were given, and is what a reference escaper writes, with every quote mode and buffer size. What the serializer writes reads back as the records it was given, with the generated metadata and
/// with the reflection-based metadata, and both read any data the same way.
/// </summary>
public static partial class CsvFuzzTargets
{
    // U+0000 is the value of the code units from U+8000 once narrowed by the SIMD path: as a separator or a quote, it must not match them
    private static readonly char[] ReaderSeparators = [',', ';', '\t', ' ', '\0', '"', '\n', (char)0xD800];

    private static readonly char?[] ReaderQuotes = ['"', '\'', null, ',', '\r', '\0', 'a', (char)0xDC00];

    // Characters the writer can separate values with and quote them with: a separator is never a quote, nor a line terminator
    private static readonly char[] WriterSeparators = [',', ';', '\t', '|', ' ', 'a', '\0', (char)0xD800];

    private static readonly char?[] WriterQuotes = ['"', '\'', null, (char)0xDC00];

    private static readonly string[] EndOfLines = ["\n", "\r\n", "\r", "\r\n"];

    private static readonly int[] VectorSizes = [0, 16, 32, 64];

    // Shared, so that the reflection-based metadata is discovered once
    private static readonly CsvSerializerOptions SmallBufferSerializerOptions = new() { BufferSize = 16 };

    private static readonly string[] Tokens = [",", ";", "\t", "|", "\"", "\"\"", "'", "\r", "\n", "\r\n", ",\"", "\",", "\"\r\n", "\uFEFF", "\u8000", "\uFF0C", "\u0100", "😀", "é", "true", "1.5", "2026-11-21", "First", "Text,Number,Ratio,Kind,Date,Flag,bytes"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("csv-reader", Reader) { Seeds = GetReaderSeeds, Dictionary = Tokens },
        new("csv-roundtrip", RoundTrip) { Seeds = GetRoundTripSeeds, Dictionary = [.. Tokens, "=", "+", "-", "@", "=1+1"] },
        new("csv-writer", Writer) { Seeds = GetWriterSeeds, Dictionary = [.. Tokens, "=", "+", "-", "@", "\t", "=1+1"] },
        new("csv-serializer-roundtrip", SerializerRoundTrip) { Seeds = GetSerializerRoundTripSeeds, Dictionary = Tokens },
        new("csv-deserialize", Deserialize) { Seeds = GetDeserializeSeeds, Dictionary = Tokens },
        new("csv-iso8601", ParseIso8601) { Seeds = GetIso8601Seeds, Dictionary = ["-", ":", "T", "Z", ".", "+", "2024", "02", "29", "23", "59", "0000000", "+02:00", "+14:00", "0001-01-01", "9999-12-31"] },
        new("csv-decimal", ParseDecimal) { Seeds = GetDecimalSeeds, Dictionary = ["-", ".", "0", "9", "00", "1.50", "123456789", "999999999999999999", " ", ",", "+", "e"] },
    ];

    /// <summary>
    /// <c>dialect, chunks, text</c>: <c>dialect</c> picks the separator (bits 0-2), the quote (bits 3-5), and whether the first row is a header
    /// (bit 6), and whether <c>#</c> starts comment lines (bit 7); <c>chunks</c> gives the size of the buffer and the number of elements each read returns. Every SIMD path, a <see cref="TextReader"/>
    /// read in chunks, and the UTF-8 reader over a stream read in chunks give the same records. Without a header row, they are the records of the
    /// reference parser.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ReadUtf16 and ReadUtf8 dispose the readers")]
    private static void Reader(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data);
        var chunks = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);
        var separator = ReaderSeparators[dialect & 0x07];
        var quote = ReaderQuotes[(dialect >> 3) & 0x07];
        var hasHeaderRow = (dialect & 0x40) != 0;
        char? comment = (dialect & 0x80) != 0 ? '#' : null;
        var options = new CsvReaderOptions { Separator = separator, Quote = quote, HasHeaderRow = hasHeaderRow, CommentCharacter = comment, BufferSize = 1 + (chunks % 17) };

        if (separator is '\r' or '\n' || quote is '\r' or '\n' || quote == separator)
        {
            try
            {
                using var reader = CsvReader.CreateFromString(text, options);
                FuzzAssert.Fail("The reader accepts invalid options");
            }
            catch (ArgumentException)
            {
            }

            return;
        }

        var expected = ReadUtf16(CsvReader.CreateFromString(text, options));
        foreach (var vectorSize in VectorSizes)
        {
            FuzzAssert.Equal(expected, ReadUtf16(CsvReader.CreateFromString(text, options with { VectorSize = vectorSize })), "A SIMD path reads different records; vector size: " + vectorSize.ToString(CultureInfo.InvariantCulture));
        }

        FuzzAssert.Equal(expected, ReadUtf16(CsvReader.Create(new ChunkedTextReader(text, new ChunkSizer(chunks)), options)), "Reading the text in chunks gives different records");

        if (!hasHeaderRow)
        {
            var referenceText = text is ['\uFEFF', ..] ? text[1..] : text;
            FuzzAssert.Equal(Describe(ReferenceParser.Parse(referenceText, separator, quote, comment)), expected, "The records are not the records of the reference parser");
        }

        if (separator > 0x7F || quote > 0x7F)
            return;

        // UTF-8 cannot represent a lone surrogate: the UTF-8 readers read the text with replacement characters
        var bytes = Encoding.UTF8.GetBytes(text);
        var expectedUtf8 = ReadUtf16(CsvReader.CreateFromString(Encoding.UTF8.GetString(bytes), options));
        foreach (var vectorSize in VectorSizes)
        {
            FuzzAssert.Equal(expectedUtf8, ReadUtf8(Utf8CsvReader.Create(bytes, options with { VectorSize = vectorSize })), "The UTF-8 reader reads different records; vector size: " + vectorSize.ToString(CultureInfo.InvariantCulture));
        }

        FuzzAssert.Equal(expectedUtf8, ReadUtf8(Utf8CsvReader.Create(new ChunkedStream(bytes, new ChunkSizer(chunks)), options)), "Reading the UTF-8 data in chunks gives different records");
    }

    /// <summary>
    /// <c>dialect, options, rows</c>: <c>dialect</c> picks the separator (bits 0-2), the quote (bits 3-4), the end of line (bits 5-6), and whether
    /// the UTF-8 writer writes the rows (bit 7); <c>options</c> says whether the first row is a header (bit 0), whether formulas are escaped (bit 6),
    /// whether every value is quoted (bit 7), and gives the number of characters each read returns (bits 1-5). An escaped formula reads back with its
    /// apostrophe prefix. The rows are separated by <c>FF</c> and their values by <c>FE</c>;
    /// <c>FD</c> alone is <see langword="null"/>. Without a quote, the values lose the separator and the line terminators, which cannot be written.
    /// </summary>
    private static void RoundTrip(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var separator = WriterSeparators[dialect & 0x07];
        var quote = WriterQuotes[(dialect >> 3) & 0x03];
        var newLine = EndOfLines[(dialect >> 5) & 0x03];
        var utf8 = (dialect & 0x80) != 0 && separator <= 0x7F && quote is null or <= (char)0x7F;
        var hasHeaderRow = (options & 1) != 0;
        var escapeFormulas = (options & 0x40) != 0;
        var quoteMode = (options & 0x80) != 0 ? CsvQuoteMode.Always : CsvQuoteMode.AsNeeded;

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

        // A byte order mark at the start of the data is not a value
        if (rows is [[{ } first, ..] firstRow, ..] && first is ['\uFEFF', ..])
        {
            firstRow[0] = first.TrimStart('\uFEFF');
        }

        var writerOptions = new CsvWriterOptions { Separator = separator, Quote = quote, NewLine = newLine, QuoteMode = quoteMode, EscapeFormulas = escapeFormulas };
        string text;
        if (utf8)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = Utf8CsvWriter.Create(buffer, writerOptions))
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
            var builder = new StringBuilder();
            using (var writer = CsvWriter.Create(builder, writerOptions))
            {
                foreach (var row in rows)
                {
                    writer.WriteRow(row);
                }
            }

            text = builder.ToString();
        }

        var expected = rows.Select(row =>
        {
            var values = row.Select(value =>
            {
                var text = utf8 ? Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value ?? "")) : value ?? "";
                return escapeFormulas && text is ['=' or '+' or '-' or '@' or '\t' or '\r', ..] ? "'" + text : text;
            }).ToArray();

            // Without a quote character, a row with one empty value is an empty line, which has no value
            return quote is null && values is [""] ? [] : values;
        }).ToList();

        var readerOptions = new CsvReaderOptions { Separator = separator, Quote = quote, HasHeaderRow = hasHeaderRow };
        using var reader = CsvReader.Create(new ChunkedTextReader(text, new ChunkSizer((byte)((options >> 1) & 0x1F))), readerOptions);
        var actual = new List<string[]>();
        while (reader.Read())
        {
            actual.Add(reader.Current.ToStringArray());
        }

        var expectedRecords = expected.Skip(hasHeaderRow ? 1 : 0).ToArray();
        FuzzAssert.Equal(Describe(expectedRecords), Describe([.. actual]), "The rows read back are not the rows written; text: " + FuzzAssert.Format(text));
        if (hasHeaderRow && expected.Count > 0)
        {
            FuzzAssert.Equal(Describe([expected[0]]), Describe([[.. reader.Header?.ColumnNames ?? []]]), "The header read back is not the header written; text: " + FuzzAssert.Format(text));
        }
    }

    /// <summary>
    /// <c>dialect, options, rows</c>: <c>dialect</c> picks the separator (bits 0-2), the quote (bits 3-4), the end of line (bits 5-6), and whether
    /// the UTF-8 writer writes the rows (bit 7); <c>options</c> picks the quote mode (bits 0-1), whether formulas are escaped (bit 2) and the size of
    /// the buffer (the other bits). The rows are separated by <c>FF</c> and their values by <c>FE</c>; <c>FD</c> alone is <see langword="null"/>.
    /// The writers write what a reference escaper writes, value by value; the UTF-8 writer writes the same data from the values as UTF-8 bytes.
    /// </summary>
    private static void Writer(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data);
        var options = FuzzInput.ReadByte(ref data);
        var separator = WriterSeparators[dialect & 0x07];
        var quote = WriterQuotes[(dialect >> 3) & 0x03];
        var utf8 = (dialect & 0x80) != 0 && separator <= 0x7F && quote is null or <= (char)0x7F;
        var writerOptions = new CsvWriterOptions
        {
            Separator = separator,
            Quote = quote,
            NewLine = EndOfLines[(dialect >> 5) & 0x03],
            QuoteMode = (options & 0x03) switch { 1 => CsvQuoteMode.Always, 2 => CsvQuoteMode.Never, _ => CsvQuoteMode.AsNeeded },
            EscapeFormulas = (options & 0x04) != 0,
            BufferSize = 1 + (options >> 3),
        };

        var rows = new List<string?[]>();
        while (!data.IsEmpty)
        {
            var rowData = FuzzInput.ReadUntil(ref data, 0xFF);
            var values = new List<string?>();
            while (!rowData.IsEmpty)
            {
                var value = FuzzInput.ReadUntil(ref rowData, 0xFE);
                values.Add(value is [0xFD] ? null : FuzzText.Decode(value));
            }

            rows.Add([.. values]);
        }

        string text;
        if (utf8)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = Utf8CsvWriter.Create(buffer, writerOptions))
            {
                foreach (var row in rows)
                {
                    writer.WriteRow(row);
                }
            }

            text = Encoding.UTF8.GetString(buffer.WrittenSpan);

            // The values written as UTF-8 bytes are escaped as the same values written as text
            var bytesBuffer = new ArrayBufferWriter<byte>();
            using (var writer = Utf8CsvWriter.Create(bytesBuffer, writerOptions))
            {
                foreach (var row in rows)
                {
                    foreach (var value in row)
                    {
                        writer.WriteField(Encoding.UTF8.GetBytes(value ?? ""));
                    }

                    writer.EndRow();
                }
            }

            FuzzAssert.Equal(FuzzAssert.Format(text), FuzzAssert.Format(Encoding.UTF8.GetString(bytesBuffer.WrittenSpan)), "The values written as UTF-8 bytes are not written as the same text");
        }
        else
        {
            var builder = new StringBuilder();
            using (var writer = CsvWriter.Create(builder, writerOptions))
            {
                foreach (var row in rows)
                {
                    writer.WriteRow(row);
                }
            }

            text = builder.ToString();
        }

        // UTF-8 cannot represent a lone surrogate: the UTF-8 writer writes a replacement character
        var expected = new StringBuilder();
        foreach (var row in rows)
        {
            WriteReferenceRow(expected, [.. row.Select(value => utf8 ? Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(value ?? "")) : value ?? "")], writerOptions);
        }

        FuzzAssert.Equal(FuzzAssert.Format(expected.ToString()), FuzzAssert.Format(text), "The writer does not write what the reference escaper writes");
    }

    // Quotes a value that contains the separator, the quote, a line terminator or a character of the new line, an escaped formula when the quote is
    // the apostrophe of its prefix, or every value with Always; doubles the quotes, the prefix included; prefixes an escaped formula with an
    // apostrophe; and quotes a row that has a single empty value, which would read as an empty line
    private static void WriteReferenceRow(StringBuilder output, string[] row, CsvWriterOptions options)
    {
        var canQuote = options.Quote.HasValue && options.QuoteMode is not CsvQuoteMode.Never;
        var quote = options.Quote.GetValueOrDefault();
        for (var i = 0; i < row.Length; i++)
        {
            var value = row[i];
            if (i > 0)
            {
                output.Append(options.Separator);
            }

            var formula = options.EscapeFormulas && value is ['=' or '+' or '-' or '@' or '\t' or '\r', ..];
            var needsQuotes = canQuote && (options.QuoteMode is CsvQuoteMode.Always || (formula && quote is '\'') || value.Contains(options.Separator, StringComparison.Ordinal) || value.Contains(quote, StringComparison.Ordinal) || value.AsSpan().ContainsAny('\r', '\n') || value.AsSpan().ContainsAny(options.NewLine));
            var content = (formula ? "'" : "") + value;
            if (needsQuotes)
            {
                output.Append(quote).Append(content.Replace(quote.ToString(), new string(quote, 2), StringComparison.Ordinal)).Append(quote);
            }
            else
            {
                output.Append(content);
            }
        }

        if (row is [""] && canQuote && options.QuoteMode is not CsvQuoteMode.Always)
        {
            output.Append(quote, 2);
        }

        output.Append(options.NewLine);
    }

    /// <summary>
    /// <c>records</c>: the records are separated by <c>FF</c>; each gives the values of the members of <see cref="FuzzRecord"/>. The serializer writes
    /// the same text with the generated metadata and with reflection, and the text reads back as the records, as UTF-16 and as UTF-8.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The fuzz targets compare the reflection-based serializer with the generated one.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The fuzz targets compare the reflection-based serializer with the generated one.")]
    private static void SerializerRoundTrip(ReadOnlySpan<byte> data)
    {
        var records = new List<FuzzRecord>();
        while (!data.IsEmpty)
        {
            records.Add(CreateRecord(FuzzInput.ReadUntil(ref data, 0xFF)));
        }

        var typeInfo = FuzzCsvContext.Default.FuzzRecord;
        var text = CsvSerializer.Serialize(records, typeInfo);
        FuzzAssert.Equal(text, CsvSerializer.Serialize(records), "The generated metadata and the reflection write different text");

        // A small buffer keeps the text in many chunks, of growing sizes, before the string is created
        FuzzAssert.Equal(text, CsvSerializer.Serialize(records, SmallBufferSerializerOptions), "The text written in chunks is not the text written in one buffer");

        var expected = string.Join('\n', records.Select(Describe));
        FuzzAssert.Equal(expected, string.Join('\n', CsvSerializer.Deserialize(text, typeInfo).Select(Describe)), "The records read back are not the records written; text: " + FuzzAssert.Format(text));

        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, records, typeInfo);
        FuzzAssert.Equal(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)), Encoding.UTF8.GetString(stream.ToArray()), "The UTF-8 data is not the UTF-16 text");
        stream.Position = 0;
        var utf8Expected = string.Join('\n', records.Select(record => Describe(record, utf8: true)));
        FuzzAssert.Equal(utf8Expected, string.Join('\n', CsvSerializer.Deserialize(stream, typeInfo).Select(record => Describe(record, utf8: true))), "The records read back from UTF-8 are not the records written");
    }

    /// <summary>
    /// <c>text</c>: the serializer reads the text as <see cref="FuzzRecord"/> records with the generated metadata and with reflection, as UTF-16 and as
    /// UTF-8. It throws only <see cref="CsvException"/>, and the four readings give the same records or all throw.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The fuzz targets compare the reflection-based serializer with the generated one.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The fuzz targets compare the reflection-based serializer with the generated one.")]
    private static void Deserialize(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        var generated = TryDeserialize(() => CsvSerializer.Deserialize(text, FuzzCsvContext.Default.FuzzRecord));
        var reflection = TryDeserialize(() => CsvSerializer.Deserialize<FuzzRecord>(text));
        FuzzAssert.Equal(generated, reflection, "The generated metadata and the reflection read different records");

        var bytes = Encoding.UTF8.GetBytes(text);
        var expectedUtf8 = TryDeserialize(() => CsvSerializer.Deserialize(Encoding.UTF8.GetString(bytes), FuzzCsvContext.Default.FuzzRecord));
        var actualUtf8 = TryDeserialize(() => CsvSerializer.Deserialize(new MemoryStream(bytes), FuzzCsvContext.Default.FuzzRecord));
        FuzzAssert.Equal(expectedUtf8, actualUtf8, "Reading UTF-8 data gives different records");

        static string TryDeserialize(Func<List<FuzzRecord>> deserialize)
        {
            try
            {
                return string.Join("\n", deserialize().Select(Describe));
            }
            catch (CsvException)
            {
                return "CsvException";
            }
        }
    }

    /// <summary>
    /// <c>text</c>: the fast parsers of the round-trip date and time format accept a subset of what the parsers of the BCL accept, and return the same
    /// values, from UTF-16 and from UTF-8.
    /// </summary>
    private static void ParseIso8601(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        var utf8 = Encoding.UTF8.GetBytes(text);
        var invariant = CultureInfo.InvariantCulture;
        try
        {
            if (Serialization.Converters.Iso8601.TryParse(text, out DateTime dateTime))
            {
                var expected = DateTime.Parse(text, invariant, DateTimeStyles.RoundtripKind);
                FuzzAssert.Equal((expected, expected.Kind), (dateTime, dateTime.Kind), "The DateTime is not the DateTime of the BCL");
            }

            if (Serialization.Converters.Iso8601.TryParse(utf8, out DateTime utf8DateTime))
            {
                var expected = DateTime.Parse(text, invariant, DateTimeStyles.RoundtripKind);
                FuzzAssert.Equal((expected, expected.Kind), (utf8DateTime, utf8DateTime.Kind), "The UTF-8 DateTime is not the DateTime of the BCL");
            }

            if (Serialization.Converters.Iso8601.TryParse(text, out DateTimeOffset offset))
            {
                var expected = DateTimeOffset.Parse(text, invariant, DateTimeStyles.None);
                FuzzAssert.Equal((expected, expected.Offset), (offset, offset.Offset), "The DateTimeOffset is not the DateTimeOffset of the BCL");
            }

            if (Serialization.Converters.Iso8601.TryParse(utf8, out DateTimeOffset utf8Offset))
            {
                var expected = DateTimeOffset.Parse(text, invariant, DateTimeStyles.None);
                FuzzAssert.Equal((expected, expected.Offset), (utf8Offset, utf8Offset.Offset), "The UTF-8 DateTimeOffset is not the DateTimeOffset of the BCL");
            }

            if (Serialization.Converters.Iso8601.TryParse(text.AsSpan(), out DateOnly date))
            {
                FuzzAssert.Equal(DateOnly.ParseExact(text, "O", invariant), date, "The DateOnly is not the DateOnly of the BCL");
            }

            if (Serialization.Converters.Iso8601.TryParse(text.AsSpan(), out TimeOnly time))
            {
                FuzzAssert.Equal(TimeOnly.Parse(text, invariant), time, "The TimeOnly is not the TimeOnly of the BCL");
            }
        }
        catch (FormatException ex)
        {
            FuzzAssert.Fail("The BCL rejects a value the fast parser accepts: " + FuzzAssert.Format(text) + " " + ex.Message);
        }
    }

    /// <summary>
    /// The fast parser of the decimal converter accepts a subset of what <see cref="decimal.Parse(string, IFormatProvider?)"/> accepts, and gives the
    /// same values, with the same scale and sign, in UTF-16 and in UTF-8. The fast formatter, and the writers, write the value made of the first 13 bytes
    /// as <see cref="decimal.ToString(IFormatProvider?)"/> does.
    /// </summary>
    private static void ParseDecimal(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 13)
        {
            var value = new decimal(BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data[4..]), (data[12] & 0x80) != 0 ? BinaryPrimitives.ReadInt32LittleEndian(data[8..]) : 0, (data[12] & 0x40) != 0, (byte)((data[12] & 0x3F) % 29));
            var expectedText = value.ToString(CultureInfo.InvariantCulture);
            Span<char> chars = stackalloc char[64];
            Span<byte> bytes = stackalloc byte[64];
            if (Serialization.Converters.DecimalCsvConverter.TryFormatPlain(value, chars, out var charsWritten, out _))
            {
                FuzzAssert.Equal(expectedText, chars[..charsWritten].ToString(), "The decimal is not written as the BCL writes it");
                FuzzAssert.True(Serialization.Converters.DecimalCsvConverter.TryFormatPlain(value, bytes, out var bytesWritten, out _), "The UTF-8 formatter does not handle a value the UTF-16 formatter handles");
                FuzzAssert.Equal(expectedText, Encoding.UTF8.GetString(bytes[..bytesWritten]), "The decimal is not written in UTF-8 as the BCL writes it");
            }

            // The writers write decimals with the fast formatter in the default format of the invariant culture, in a buffer that grows
            var builder = new StringBuilder();
            var buffer = new ArrayBufferWriter<byte>();
            var writerOptions = new CsvWriterOptions { NewLine = "\n", BufferSize = 1 };
            using (var writer = CsvWriter.Create(builder, writerOptions))
            using (var utf8Writer = Utf8CsvWriter.Create(buffer, writerOptions))
            {
                writer.WriteField(value);
                writer.WriteField(value);
                writer.EndRow();
                utf8Writer.WriteField(value);
                utf8Writer.WriteField(value);
                utf8Writer.EndRow();
            }

            FuzzAssert.Equal(expectedText + "," + expectedText + "\n", builder.ToString(), "The writer does not write the decimal as the BCL writes it");
            FuzzAssert.Equal(expectedText + "," + expectedText + "\n", Encoding.UTF8.GetString(buffer.WrittenSpan), "The UTF-8 writer does not write the decimal as the BCL writes it");
        }

        var text = FuzzInput.ReadText(data);
        var utf8 = Encoding.UTF8.GetBytes(text);
        var parsed = Serialization.Converters.DecimalCsvConverter.TryParsePlain(text.AsSpan(), out var result);
        var utf8Parsed = Serialization.Converters.DecimalCsvConverter.TryParsePlain((ReadOnlySpan<byte>)utf8, out var utf8Result);
        FuzzAssert.Equal(parsed, utf8Parsed, "The UTF-16 and UTF-8 fast parsers disagree");
        if (parsed)
        {
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
                FuzzAssert.Fail("The BCL rejects a value the fast parser accepts: " + FuzzAssert.Format(text));

            FuzzAssert.Equal(Describe(expected), Describe(result), "The decimal is not the decimal of the BCL");
            FuzzAssert.Equal(Describe(expected), Describe(utf8Result), "The UTF-8 decimal is not the decimal of the BCL");
        }

        static string Describe(decimal value) => string.Join(',', decimal.GetBits(value));
    }

    private static IEnumerable<FuzzSeed> GetDecimalSeeds()
    {
        string[] values = ["0", "-0", "1.50", "-123.45", "007", "123456789012345678", "-12345678901234567.8", "1234567890123456789", "0.000000000000000001", "1.", ".5", "1e3", "1,000"];
        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText("decimal-" + i.ToString(CultureInfo.InvariantCulture), values[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetIso8601Seeds()
    {
        string[] values = ["2020-01-01T00:00:00.0000000Z", "2026-11-21T00:00:00.0000000", "2020-03-05T12:34:56.7890000+02:00", "2024-02-29", "12:34:56", "12:34:56.1234567", "23:59:59.9999999", "0001-01-01T00:00:00.0000000-01:00", "9999-12-31T23:59:59.9999999+14:00"];
        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText("iso8601-" + i.ToString(CultureInfo.InvariantCulture), values[i]);
        }
    }

    private static FuzzRecord CreateRecord(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var number = data.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(data) : flags;
        var ratio = data.Length >= 12 ? BinaryPrimitives.ReadDoubleLittleEndian(data[4..]) : flags / 4.0;
        var dayNumber = data.Length >= 16 ? (int)(BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) % (uint)DateOnly.MaxValue.DayNumber) : flags * 1000;
        var text = FuzzText.Decode(data.Length > 16 ? data[16..] : data);
        return new FuzzRecord
        {
            Text = (flags & 0x01) != 0 ? null : text,
            Number = number,
            Ratio = (flags & 0x02) != 0 ? null : ratio,
            Kind = (FuzzKind)((flags >> 2) % 3),
            Date = (flags & 0x10) != 0 ? null : DateOnly.FromDayNumber(dayNumber),
            Flag = (flags & 0x20) != 0,
            Bytes = (flags & 0x40) != 0 ? null : data.ToArray(),
        };
    }

    private static string Describe(FuzzRecord record) => Describe(record, utf8: false);

    private static string Describe(FuzzRecord record, bool utf8)
    {
        // null and an empty value cannot be told apart: a string is read as an empty string, an array as null
        var text = record.Text ?? "";
        if (utf8)
        {
            text = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
        }

        var bytes = record.Bytes is null or [] ? "null" : Convert.ToBase64String(record.Bytes);
        return string.Create(CultureInfo.InvariantCulture, $"{FuzzText.Escape(text)}|{record.Number}|{record.Ratio?.ToString("R", CultureInfo.InvariantCulture) ?? "null"}|{record.Kind}|{record.Date?.ToString("O", CultureInfo.InvariantCulture) ?? "null"}|{record.Flag}|{bytes}");
    }

    private static string ReadUtf16(CsvReader reader)
    {
        using (reader)
        {
            var records = new List<string>();
            var previousLine = 0L;
            while (true)
            {
                // Each record consumes at least one character
                FuzzAssert.True(records.Count <= 1_000_000, "The reader returns too many records");
                if (!reader.Read())
                    break;

                FuzzAssert.Equal((long)records.Count, reader.RowIndex, "The row index is not the index of the record");
                FuzzAssert.True(reader.LineNumber > previousLine, "The line numbers of the records do not increase");
                previousLine = reader.LineNumber;
                var record = reader.Current;
                records.Add(DescribeRecord(record.ToStringArray(), reader.Header));
            }

            return string.Join("\n", records);
        }
    }

    private static string ReadUtf8(Utf8CsvReader reader)
    {
        using (reader)
        {
            var records = new List<string>();
            while (reader.Read())
            {
                records.Add(DescribeRecord(reader.Current.ToStringArray(), reader.Header));
            }

            return string.Join("\n", records);
        }
    }

    private static string DescribeRecord(string[] values, CsvHeader? header)
    {
        var description = "[" + string.Join(", ", values.Select(FuzzText.Escape)) + "]";
        return header is null ? description : "[" + string.Join(", ", header.ColumnNames.Select(FuzzText.Escape)) + "] " + description;
    }

    private static string Describe(string[][] records) => string.Join("\n", records.Select(record => DescribeRecord(record, header: null)));

    private static string RemoveUnquotable(string value, char separator, char? quote)
    {
        if (quote is not null)
            return value;

        return new string([.. value.Where(c => c != separator && c is not '\r' and not '\n')]);
    }

    private static IEnumerable<FuzzSeed> GetReaderSeeds()
    {
        var index = 0;
        foreach (var (name, text) in GetTexts())
        {
            // Comma and double quote, with and without a header, read whole or in small chunks; then other dialects
            var dialect = (byte)(index % 4 switch { 0 => 0x00, 1 => 0x40, 2 => 0x09, _ => 0x10 | 0x03 });
            if (index % 5 is 0)
            {
                dialect |= 0x80;
            }

            yield return FuzzSeed.FromText(name, [dialect, (byte)(index % 3 is 0 ? 0 : 0x13)], text);
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetRoundTripSeeds()
    {
        var index = 0;
        foreach (var (name, text) in GetTexts())
        {
            var data = new List<byte> { (byte)((index % 8) | ((index % 4) << 5) | (index % 5 is 0 ? 0x80 : 0)), (byte)((index % 2) | (index % 3 is 0 ? 0 : 0x26) | (index % 7 is 0 ? 0x80 : 0)) };
            AddRows(data, text);
            yield return new FuzzSeed(name, data.ToArray());
            index++;
        }

        yield return new FuzzSeed("special-values", (byte[])[0x00, 0x00, .. "a,b"u8, 0xFE, .. "\"q\""u8, 0xFE, .. "line\r\nbreak"u8, 0xFE, 0xFE, 0xFD, 0xFF, 0xFD, 0xFF, 0xFF, .. "x"u8]);
        yield return new FuzzSeed("header", (byte[])[0x00, 0x01, .. "A"u8, 0xFE, .. "B"u8, 0xFE, .. "A"u8, 0xFF, .. "1"u8, 0xFE, .. "2"u8, 0xFF, .. "3"u8]);
        yield return new FuzzSeed("no-quote", (byte[])[0x10 | 0x80, 0x22, .. "a,b"u8, 0xFE, .. "c\nd"u8, 0xFF, .. "e\"f"u8]);
        yield return new FuzzSeed("utf8-always", (byte[])[0x80, 0x80, .. "é"u8, 0xFE, .. "😀"u8, 0xFF, 0xFD]);
        yield return new FuzzSeed("surrogates", (byte[])[0x07 | 0x18 | 0x40, 0x03, .. FuzzText.Encode("\uD800x\uDC00"), 0xFE, .. FuzzText.Encode("\uDC00\uDC00"), 0xFF, .. FuzzText.Encode("😀")]);
        yield return new FuzzSeed("formulas", (byte[])[0x00, 0x40, .. "=1+1"u8, 0xFE, .. "+a"u8, 0xFE, .. "-"u8, 0xFE, .. "@x,y"u8, 0xFE, .. "\t\"q\""u8, 0xFF, .. "\rz"u8, 0xFE, .. "'=x"u8]);
        yield return new FuzzSeed("formulas-apostrophe-quote", (byte[])[0x08, 0x40, .. "=1"u8, 0xFE, .. "+a'b"u8, 0xFE, .. "plain"u8, 0xFE, .. "@x"u8, 0xFE, .. "-"u8, 0xFE, .. "="u8]);
        yield return new FuzzSeed("formulas-apostrophe-quote-utf8-always", (byte[])[0x08 | 0x80, 0x40 | 0x80, .. "=1"u8, 0xFE, .. "a"u8, 0xFF, .. "-'"u8]);
        yield return new FuzzSeed("formulas-no-quote", (byte[])[0x10, 0x40, .. "=1"u8, 0xFE, .. "@a"u8]);
    }

    private static IEnumerable<FuzzSeed> GetWriterSeeds()
    {
        var index = 0;
        foreach (var (name, text) in GetTexts())
        {
            var data = new List<byte> { (byte)((index % 8) | ((index % 4) << 5) | (index % 5 is 0 ? 0x80 : 0)), (byte)((index % 4) | (index % 3 is 0 ? 0x04 : 0) | ((index % 6) << 3)) };
            AddRows(data, text);
            yield return new FuzzSeed("writer-" + name, data.ToArray());
            index++;
        }

        yield return new FuzzSeed("formulas", (byte[])[0x00, 0x04, .. "=1+1"u8, 0xFE, .. "+a"u8, 0xFE, .. "-"u8, 0xFE, .. "@x,y"u8, 0xFE, .. "\t\"q\""u8, 0xFF, .. "\rz"u8]);
        yield return new FuzzSeed("always-small-buffer", (byte[])[0x80, 0x01 | 0x04, .. "a\"b"u8, 0xFE, 0xFD, 0xFE, 0xFE, .. "é😀"u8, 0xFF, 0xFD]);
        yield return new FuzzSeed("never", (byte[])[0x01, 0x02 | 0x08, .. "a;b"u8, 0xFE, .. "c\nd"u8]);
    }

    // The texts of the tests split into rows on their line terminators and into values on commas
    private static void AddRows(List<byte> data, string text)
    {
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
    }

    private static IEnumerable<FuzzSeed> GetSerializerRoundTripSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return new FuzzSeed("one", (byte[])[0x00, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xF8, 0x3F, 10, 0, 0, 0, .. "text, with \"quotes\""u8]);
        yield return new FuzzSeed("nulls", (byte[])[0x01 | 0x02 | 0x10 | 0x40, 0xFF, 0x24, .. "a\r\nb"u8]);
        yield return new FuzzSeed("several", (byte[])[0x04, .. "x"u8, 0xFF, 0x08, .. "y"u8, 0xFF, 0x20, .. "😀é"u8]);
    }

    private static IEnumerable<FuzzSeed> GetDeserializeSeeds()
    {
        yield return FuzzSeed.FromText("valid", [], "Text,Number,Ratio,Kind,Date,Flag,bytes\r\nabc,1,1.5,First,2026-11-21,true,AQID\r\n,0,,None,,false,\r\n");
        yield return FuzzSeed.FromText("invalid-number", [], "Text,Number\nabc,x");
        yield return FuzzSeed.FromText("reordered", [], "Flag,Text,Unknown\n1,\"a,b\",z\n0,c,");
        foreach (var (name, text) in GetTexts())
        {
            yield return FuzzSeed.FromText("deserialize-" + name, [], text);
        }
    }

    private static IEnumerable<(string Name, string Text)> GetTexts()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in (string[])["CsvReaderTests.cs", "CsvWriterTests.cs", "CsvSerializerTests.cs"])
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

        // The inputs of the public test suites the unit tests run
        foreach (var (suite, property) in ((string, string)[])[("csv-spectrum", "csv"), ("papaparse", "input")])
        {
            using var document = JsonDocument.Parse(FuzzResources.ReadText(typeof(CsvFuzzTargets).Assembly, suite + ".cases.json"));
            var index = 0;
            foreach (var testCase in document.RootElement.EnumerateArray())
            {
                var text = testCase.GetProperty(property).GetString()!;
                if (text.Length > 0 && seen.Add(text))
                {
                    yield return (string.Create(CultureInfo.InvariantCulture, $"{suite}-{index++}"), text);
                }
            }
        }

        yield return ("quoted", "a,\"b,c\",\"d\"\"e\"\r\n\"multi\nline\",,\"\"\n\"unterminated");
        yield return ("blank-lines", "\n\r\n\r\ra\n");
        yield return ("quote-in-value", "a\"b,\"c\"d\"\"e");
        yield return ("comments", "# c,\"x\na,b\n#\n\n\"#q\",1\r\n# end,\r\nz");
        yield return ("bom", "\uFEFFa,b\n1,2");
        yield return ("long", new string('a', 100) + "," + new string('b', 70) + "\n\"" + new string('q', 50) + "\"\"\"");
    }

    [GeneratedRegex("""(?<![@$"\\])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
