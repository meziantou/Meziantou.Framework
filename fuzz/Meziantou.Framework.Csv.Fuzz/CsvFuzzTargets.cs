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
/// with the reflection-based metadata, and both read any data the same way. The bad data and deserialization error handlers report the errors that throw
/// without them, and accepting or ignoring every error reads what the lenient modes read.
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

    // Shared for the same reason: the handler reports the errors to the state of the current deserialization
    private static readonly CsvSerializerOptions ErrorHandlerSerializerOptions = new() { DeserializationErrorHandler = OnDeserializationError };

    private static readonly FuzzCsvContext ErrorHandlerCsvContext = new(new CsvSerializerOptions { DeserializationErrorHandler = OnDeserializationError });

    [ThreadStatic]
    private static DeserializeErrorState? s_deserializeErrorState;

    private static readonly string[] Tokens = [",", ";", "\t", "|", "\"", "\"\"", "'", "\r", "\n", "\r\n", ",\"", "\",", "\"\r\n", "\uFEFF", "\u8000", "\uFF0C", "\u0100", "😀", "é", "true", "1.5", "2026-11-21", "First", "Text,Number,Ratio,Kind,Date,Flag,bytes"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("csv-reader", Reader) { Seeds = GetReaderSeeds, Dictionary = Tokens },
        new("csv-reader-bad-data", ReaderBadData) { Seeds = GetReaderBadDataSeeds, Dictionary = Tokens },
        new("csv-roundtrip", RoundTrip) { Seeds = GetRoundTripSeeds, Dictionary = [.. Tokens, "=", "+", "-", "@", "=1+1"] },
        new("csv-writer", Writer) { Seeds = GetWriterSeeds, Dictionary = [.. Tokens, "=", "+", "-", "@", "\t", "=1+1"] },
        new("csv-serializer-roundtrip", SerializerRoundTrip) { Seeds = GetSerializerRoundTripSeeds, Dictionary = Tokens },
        new("csv-deserialize", Deserialize) { Seeds = GetDeserializeSeeds, Dictionary = Tokens },
        new("csv-deserialize-errors", DeserializeErrors) { Seeds = GetDeserializeSeeds, Dictionary = Tokens },
        new("csv-iso8601", ParseIso8601) { Seeds = GetIso8601Seeds, Dictionary = ["-", ":", "T", "Z", ".", "+", "2024", "02", "29", "23", "59", "0000000", "+02:00", "+14:00", "0001-01-01", "9999-12-31"] },
        new("csv-decimal", ParseDecimal) { Seeds = GetDecimalSeeds, Dictionary = ["-", ".", "0", "9", "00", "1.50", "123456789", "999999999999999999", " ", ",", "+", "e"] },
        new("csv-numbers", ParseNumbers) { Seeds = GetNumberSeeds, Dictionary = ["-", ".", "0", "9", "00", "1.5", "999999999", "2147483648", "999999999999999", "9223372036854775808", " ", ",", "+", "e", "E", "NaN"] },
        new("csv-floating-point", ParseFloatingPoint) { Seeds = GetFloatingPointSeeds, Dictionary = ["-", "+", ".", "0", "9", "e", "E", "e-", "e+", "22", "23", "2048", "65504", "16777216", "9007199254740992", "0.1", "NaN", "Infinity", " ", ","] },
        new("csv-header", HeaderLookup) { Seeds = GetHeaderSeeds, Dictionary = [",", "Id", "Name", "id", "NAME", "é"] },
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
    /// <c>dialect, chunks, text</c>, as <see cref="Reader"/>; bit 6 of <c>chunks</c> sets <see cref="CsvReaderOptions.ValidateColumnCount"/> and bit 7
    /// trims the spaces around the values. With a bad data handler that accepts every error, the readers return the records of non-strict mode; the errors
    /// are the same whatever the SIMD path and the chunks, they are in the order of the data and point into the records, and strict mode throws the first
    /// one. With a handler that skips every record with an error, the readers return the other records.
    /// </summary>
    private static void ReaderBadData(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.ReadByte(ref data);
        var chunks = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(data);
        var separator = ReaderSeparators[dialect & 0x07];
        var quote = ReaderQuotes[(dialect >> 3) & 0x07];
        if (separator is '\r' or '\n' || quote is '\r' or '\n' || quote == separator)
            return;

        var validateColumnCount = (chunks & 0x40) != 0;
        var options = new CsvReaderOptions
        {
            Separator = separator,
            Quote = quote,
            HasHeaderRow = (dialect & 0x40) != 0,
            CommentCharacter = (dialect & 0x80) != 0 ? '#' : null,
            Trim = (chunks & 0x80) != 0 ? CsvTrimOptions.Outer : CsvTrimOptions.None,
            BufferSize = 1 + (chunks % 17),
        };

        // Without a handler, the column count is only checked in strict mode: a record with other columns throws
        var lenient = Describe([.. ReadWithErrors(options, o => CsvReader.CreateFromString(text, o), action: null, out _)]);
        options = options with { ValidateColumnCount = validateColumnCount };
        var acceptedRecords = ReadWithErrors(options, o => CsvReader.CreateFromString(text, o), CsvBadDataAction.Accept, out var errors);
        var accepted = Describe([.. acceptedRecords]);
        var acceptedErrors = DescribeErrors(errors);
        FuzzAssert.Equal(lenient, accepted, "Accepting every error does not read the records of non-strict mode");
        CheckErrors(text.Length, acceptedRecords, errors);

        foreach (var vectorSize in VectorSizes)
        {
            var records = ReadWithErrors(options with { VectorSize = vectorSize }, o => CsvReader.CreateFromString(text, o), CsvBadDataAction.Accept, out var otherErrors);
            FuzzAssert.Equal(accepted + "\n" + acceptedErrors, Describe([.. records]) + "\n" + DescribeErrors(otherErrors), "A SIMD path reports different errors; vector size: " + vectorSize.ToString(CultureInfo.InvariantCulture));
        }

        var chunkedRecords = ReadWithErrors(options, o => CsvReader.Create(new ChunkedTextReader(text, new ChunkSizer(chunks)), o), CsvBadDataAction.Accept, out var chunkedErrors);
        FuzzAssert.Equal(accepted + "\n" + acceptedErrors, Describe([.. chunkedRecords]) + "\n" + DescribeErrors(chunkedErrors), "Reading the text in chunks reports different errors");

        // Strict mode throws the first error
        try
        {
            ReadWithErrors(options with { Strict = true }, o => CsvReader.CreateFromString(text, o), action: null, out _);
            FuzzAssert.True(errors.Count is 0, "Strict mode accepts data with errors: " + acceptedErrors);
        }
        catch (CsvException ex)
        {
            FuzzAssert.True(errors.Count > 0, "Strict mode throws for data without errors: " + ex.Message);
            FuzzAssert.Equal(errors[0].Exception, ex.Message, "Strict mode does not throw the first error");
        }

        // Skipping every record with an error returns the other records. A skipped header row makes the next row the header row, and the expected
        // number of columns comes from the first record that is not skipped: the records are compared when neither applies.
        var skippedRecords = ReadWithErrors(options, o => CsvReader.CreateFromString(text, o), CsvBadDataAction.Skip, out var skipErrors);
        if (!validateColumnCount && errors.TrueForAll(e => e.RowIndex is not null))
        {
            var skippedRows = errors.Select(e => e.RowIndex!.Value).ToHashSet();
            FuzzAssert.Equal(Describe([.. acceptedRecords.Where((_, index) => !skippedRows.Contains(index))]), Describe([.. skippedRecords]), "Skipping the records with an error does not return the other records");
            FuzzAssert.Equal(DescribeErrors([.. errors.DistinctBy(e => e.RowIndex)]), DescribeErrors(skipErrors), "Skipping the records reports other errors than the first error of each record");
        }

        if (separator > 0x7F || quote > 0x7F)
            return;

        // UTF-8 cannot represent a lone surrogate: the UTF-8 readers read the text with replacement characters. The positions are in bytes.
        var bytes = Encoding.UTF8.GetBytes(text);
        var expectedUtf8 = Describe([.. ReadWithErrors(options, o => CsvReader.CreateFromString(Encoding.UTF8.GetString(bytes), o), CsvBadDataAction.Accept, out var expectedUtf8Errors)]) + "\n" + DescribeWithoutPositions(expectedUtf8Errors);
        foreach (var vectorSize in VectorSizes)
        {
            var records = ReadWithErrors(options with { VectorSize = vectorSize }, o => Utf8CsvReader.Create(bytes, o), CsvBadDataAction.Accept, out var utf8Errors);
            CheckErrors(bytes.Length, records, utf8Errors);
            FuzzAssert.Equal(expectedUtf8, Describe([.. records]) + "\n" + DescribeWithoutPositions(utf8Errors), "The UTF-8 reader reports different errors; vector size: " + vectorSize.ToString(CultureInfo.InvariantCulture));
        }

        var chunkedUtf8 = ReadWithErrors(options, o => Utf8CsvReader.Create(new ChunkedStream(bytes, new ChunkSizer(chunks)), o), CsvBadDataAction.Accept, out var chunkedUtf8Errors);
        FuzzAssert.Equal(expectedUtf8, Describe([.. chunkedUtf8]) + "\n" + DescribeWithoutPositions(chunkedUtf8Errors), "Reading the UTF-8 data in chunks reports different errors");

        static string DescribeErrors(List<BadData> errors) => string.Join("\n", errors);

        static string DescribeWithoutPositions(List<BadData> errors) => string.Join("\n", errors.Select(e => e with { Position = 0, Exception = "" }));
    }

    private static void CheckErrors(int length, List<string[]> records, List<BadData> errors)
    {
        BadData? previous = null;
        foreach (var error in errors)
        {
            FuzzAssert.True(error.Position >= 0 && error.Position <= length, "The position of an error is out of the data: " + error);
            FuzzAssert.True(error.LineNumber >= 1, "The line number of an error is not positive: " + error);
            if (previous is not null)
            {
                FuzzAssert.True(error.Position >= previous.Position && error.LineNumber >= previous.LineNumber, "The errors are not in the order of the data: " + error);
            }

            if (error.RowIndex is { } rowIndex)
            {
                FuzzAssert.True(previous?.RowIndex is null || rowIndex >= previous.RowIndex, "The row indexes of the errors decrease: " + error);
                FuzzAssert.True(rowIndex < records.Count, "The row index of an error is not the index of a record: " + error);
                if (error.ColumnIndex is { } columnIndex)
                {
                    FuzzAssert.True(columnIndex >= 0 && columnIndex < records[(int)rowIndex].Length, "The column of an error is not a column of the record: " + error);
                }
            }
            else
            {
                FuzzAssert.True(previous is null || previous.RowIndex is null, "An error of the header row follows an error of a record: " + error);
            }

            FuzzAssert.Equal(error.Kind is CsvBadDataKind.ColumnCountMismatch, error.ColumnIndex is null, "Only a column count error is about the whole record: " + error);
            previous = error;
        }
    }

    private static List<string[]> ReadWithErrors(CsvReaderOptions options, Func<CsvReaderOptions, CsvReader> create, CsvBadDataAction? action, out List<BadData> errors)
    {
        var list = new List<BadData>();
        errors = list;
        if (action is { } value)
        {
            options = options with { BadDataHandler = context => { list.Add(BadData.Create(context)); return value; } };
        }

        using var reader = create(options);
        var records = new List<string[]>();
        while (reader.Read())
        {
            FuzzAssert.True(records.Count <= 1_000_000, "The reader returns too many records");
            records.Add(reader.Current.ToStringArray());
        }

        return records;
    }

    private static List<string[]> ReadWithErrors(CsvReaderOptions options, Func<CsvReaderOptions, Utf8CsvReader> create, CsvBadDataAction? action, out List<BadData> errors)
    {
        var list = new List<BadData>();
        errors = list;
        if (action is { } value)
        {
            options = options with { BadDataHandler = context => { list.Add(BadData.Create(context)); return value; } };
        }

        using var reader = create(options);
        var records = new List<string[]>();
        while (reader.Read())
        {
            FuzzAssert.True(records.Count <= 1_000_000, "The reader returns too many records");
            records.Add(reader.Current.ToStringArray());
        }

        return records;
    }

    /// <summary>
    /// <c>text</c>, as <see cref="Deserialize"/>, with a deserialization error handler. Ignoring every error never throws, and reads the records that the
    /// serializer reads without a handler when it reports no error. Skipping the records with an error reads the other records. The generated metadata,
    /// the reflection-based metadata and the UTF-8 path report the same errors.
    /// </summary>
    private static void DeserializeErrors(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        var ignored = DeserializeWithErrors(text, utf8: false, generated: true, CsvDeserializationErrorAction.Ignore, out var errors);
        var reflection = DeserializeWithErrors(text, utf8: false, generated: false, CsvDeserializationErrorAction.Ignore, out var reflectionErrors);
        FuzzAssert.Equal(DescribeResult(ignored, errors), DescribeResult(reflection, reflectionErrors), "The generated metadata and the reflection report different errors");

        var withoutHandler = TryDeserialize(() => CsvSerializer.Deserialize(text, FuzzCsvContext.Default.FuzzRecord));
        FuzzAssert.Equal(errors.Count is 0 ? string.Join("\n", ignored) : "CsvException", withoutHandler, "The errors reported are not the errors that throw without a handler: " + string.Join("\n", errors));

        var skipped = DeserializeWithErrors(text, utf8: false, generated: true, CsvDeserializationErrorAction.SkipRecord, out var skipErrors);
        var skippedRows = errors.Select(e => e.RowIndex).ToHashSet();
        FuzzAssert.Equal(DescribeResult([.. ignored.Where((_, index) => !skippedRows.Contains(index))], [.. errors.DistinctBy(e => e.RowIndex)]), DescribeResult(skipped, skipErrors), "Skipping the records with an error does not return the other records, or reports other errors than the first error of each record");

        // UTF-8 cannot represent a lone surrogate: the UTF-8 path reads the text with replacement characters
        var utf8Text = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
        var expectedUtf8 = DeserializeWithErrors(utf8Text, utf8: false, generated: true, CsvDeserializationErrorAction.Ignore, out var expectedUtf8Errors);
        var actualUtf8 = DeserializeWithErrors(utf8Text, utf8: true, generated: true, CsvDeserializationErrorAction.Ignore, out var utf8Errors);
        FuzzAssert.Equal(DescribeResult(expectedUtf8, expectedUtf8Errors), DescribeResult(actualUtf8, utf8Errors), "Reading UTF-8 data reports different errors");

        static string DescribeResult(List<string> records, List<DeserializeError> errors) => string.Join("\n", records) + "\n" + string.Join("\n", errors);

        static List<string> DeserializeWithErrors(string text, bool utf8, bool generated, CsvDeserializationErrorAction action, out List<DeserializeError> errors)
        {
            var state = new DeserializeErrorState(action);
            s_deserializeErrorState = state;
            try
            {
                List<FuzzRecord> records;
                if (generated)
                {
                    var typeInfo = ErrorHandlerCsvContext.FuzzRecord;
                    records = utf8 ? CsvSerializer.Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(text)), typeInfo) : CsvSerializer.Deserialize(text, typeInfo);
                }
                else
                {
                    records = CsvSerializer.Deserialize<FuzzRecord>(text, ErrorHandlerSerializerOptions);
                }

                errors = state.Errors;
                return [.. records.Select(Describe)];
            }
            finally
            {
                s_deserializeErrorState = null;
            }
        }
    }

    private static CsvDeserializationErrorAction OnDeserializationError(CsvDeserializationErrorContext context)
    {
        var state = s_deserializeErrorState ?? throw new InvalidOperationException("No deserialization is in progress");
        FuzzAssert.True(context.Type == typeof(FuzzRecord), "The error is not about the type read");
        FuzzAssert.Equal(context.Kind is CsvDeserializationErrorKind.ConversionFailed, context.RawValue is not null, "Only a conversion error has a value");
        FuzzAssert.Equal(context.Kind is CsvDeserializationErrorKind.ConversionFailed, context.Exception.InnerException is not null, "Only a conversion error has an inner exception");
        state.Errors.Add(new DeserializeError(context.Kind, context.RowIndex, context.LineNumber, context.ColumnIndex, context.MemberName, context.RawValue is null ? null : FuzzText.Escape(context.RawValue)));
        return state.Action;
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
    }

    private static string TryDeserialize(Func<List<FuzzRecord>> deserialize)
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

    /// <summary>
    /// <c>mode, value</c>: when bit 0 of <c>mode</c> is set and <c>value</c> has 8 bytes, they are a double, written with its shortest text or with
    /// up to 15 decimals (bits 1-4); otherwise <c>value</c> is the text. The fast parsers of <see cref="int"/> and <see cref="long"/> agree
    /// in UTF-16 and UTF-8, and give the value of the BCL; the texts of doubles check that they are rejected or parsed as the BCL parses them.
    /// </summary>
    private static void ParseNumbers(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        string text;
        if ((mode & 1) != 0 && data.Length >= 8)
        {
            var number = BinaryPrimitives.ReadDoubleLittleEndian(data);
            var decimals = (mode >> 1) & 0x0F;
            text = decimals == 0 ? number.ToString("R", CultureInfo.InvariantCulture) : number.ToString("F" + Math.Min(decimals, 15).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
        else
        {
            text = FuzzInput.ReadText(data);
        }

        var utf8 = Encoding.UTF8.GetBytes(text);
        Check<int>(text, utf8, value => value.ToString(CultureInfo.InvariantCulture));
        Check<long>(text, utf8, value => value.ToString(CultureInfo.InvariantCulture));

        static void Check<T>(string text, byte[] utf8, Func<T, string> describe)
            where T : ISpanParsable<T>
        {
            var invariant = CultureInfo.InvariantCulture;
            var parsed = Serialization.Converters.PlainNumberParser.TryParse<char, T>(text, invariant, out var result);
            var utf8Parsed = Serialization.Converters.PlainNumberParser.TryParse<byte, T>(utf8, invariant, out var utf8Result);
            FuzzAssert.Equal(parsed, utf8Parsed, "The UTF-16 and UTF-8 fast parsers disagree: " + typeof(T).Name);
            if (!parsed)
                return;

            if (!T.TryParse(text, invariant, out var expected))
                FuzzAssert.Fail("The BCL rejects a value the fast parser accepts: " + typeof(T).Name + " " + FuzzAssert.Format(text));

            FuzzAssert.Equal(describe(expected), describe(result), "The number is not the number of the BCL: " + typeof(T).Name);
            FuzzAssert.Equal(describe(expected), describe(utf8Result), "The UTF-8 number is not the number of the BCL: " + typeof(T).Name);
        }
    }

    private static IEnumerable<FuzzSeed> GetNumberSeeds()
    {
        string[] values = ["0", "-0", "0.0", "-0.00", "007", "1.5", "-123.45", "0.1", "999999999", "1234567890", "2147483648", "-2147483648", "999999999999999999", "9223372036854775808", "999999999999999", "9999999999999999", "0.123456789012345", "12345678.9012345", "9007199254740993", "1.", ".5", "1e3", "+1", " 1", "1,000", "NaN"];
        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText("number-" + i.ToString(CultureInfo.InvariantCulture), [0], values[i]);
        }

        double[] doubles = [0.1, 123.456, -42.5, 1e-7, 1e22, 6.02214076e23, double.Epsilon, double.MaxValue, -0.0, 23.4];
        for (var i = 0; i < doubles.Length; i++)
        {
            var bytes = new byte[9];
            bytes[0] = (byte)(1 | ((i % 8) << 1));
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(1), doubles[i]);
            yield return new FuzzSeed("double-" + i.ToString(CultureInfo.InvariantCulture), bytes);
        }
    }

    private static IEnumerable<FuzzSeed> GetDecimalSeeds()
    {
        string[] values = ["0", "-0", "1.50", "-123.45", "007", "123456789012345678", "-12345678901234567.8", "1234567890123456789", "0.000000000000000001", "1.", ".5", "1e3", "1,000"];
        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText("decimal-" + i.ToString(CultureInfo.InvariantCulture), values[i]);
        }
    }

    /// <summary>
    /// The fast parser of <see cref="double"/>, <see cref="float"/> and <see cref="Half"/> values accepts a subset of what the BCL accepts with the
    /// invariant culture, and gives the same values, with the same sign, in UTF-16 and in UTF-8. The data is read as text, and its first bytes as the
    /// bits of a value, written as the BCL writes it, so that the values around the limits of the fast path are covered. The fast formatter of
    /// <see cref="double"/> values writes the text of the BCL for this value, and for the value rounded to a few decimals.
    /// </summary>
    private static void ParseFloatingPoint(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8)
        {
            var number = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data));
            var decimals = (data[0] & 0x7).ToString(CultureInfo.InvariantCulture);
            CheckFloatingPoint<double>(number.ToString("R", CultureInfo.InvariantCulture));
            CheckFloatingPoint<double>(number.ToString("F" + decimals, CultureInfo.InvariantCulture));
            CheckFloatingPoint<double>(number.ToString("E" + decimals, CultureInfo.InvariantCulture));
            CheckFormat(number);
            CheckFormat(Math.Round(number % 1e9, data[0] & 0x7, MidpointRounding.ToEven));

            var single = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
            CheckFloatingPoint<float>(single.ToString("R", CultureInfo.InvariantCulture));
            CheckFloatingPoint<float>(single.ToString("F" + decimals, CultureInfo.InvariantCulture));

            var half = BitConverter.Int16BitsToHalf(BinaryPrimitives.ReadInt16LittleEndian(data));
            CheckFloatingPoint<Half>(half.ToString("R", CultureInfo.InvariantCulture));
            CheckFloatingPoint<Half>(half.ToString("F" + decimals, CultureInfo.InvariantCulture));
        }

        var text = FuzzInput.ReadText(data);
        CheckFloatingPoint<double>(text);
        CheckFloatingPoint<float>(text);
        CheckFloatingPoint<Half>(text);
    }

    // The fast formatter of doubles writes the text of the BCL, in UTF-16 and UTF-8, when it handles a value
    private static void CheckFormat(double value)
    {
        Span<char> chars = stackalloc char[64];
        Span<byte> bytes = stackalloc byte[64];
        var handled = Internals.InvariantFloatingPoint.TryFormat(value, chars, out var written, out var tooSmall);
        FuzzAssert.Equal(handled, Internals.InvariantFloatingPoint.TryFormat(value, bytes, out var bytesWritten, out _), "The UTF-16 and UTF-8 formatters disagree on " + value.ToString("R", CultureInfo.InvariantCulture));
        if (handled)
        {
            var expected = value.ToString(CultureInfo.InvariantCulture);
            FuzzAssert.False(tooSmall, "The destination is too small for " + expected);
            FuzzAssert.Equal(expected, chars[..written].ToString(), "The text is not the text of the BCL");
            FuzzAssert.Equal(expected, Encoding.UTF8.GetString(bytes[..bytesWritten]), "The UTF-8 text is not the text of the BCL");
        }
    }

    private static void CheckFloatingPoint<T>(string text)
        where T : IFormattable, IUtf8SpanParsable<T>, ISpanParsable<T>
    {
        var parsed = Internals.InvariantFloatingPoint.TryParse(text.AsSpan(), out T result);
        var utf8Parsed = Internals.InvariantFloatingPoint.TryParse((ReadOnlySpan<byte>)Encoding.UTF8.GetBytes(text), out T utf8Result);
        FuzzAssert.Equal(parsed, utf8Parsed, "The UTF-16 and UTF-8 fast parsers disagree on " + FuzzAssert.Format(text));
        if (parsed)
        {
            if (!T.TryParse(text, CultureInfo.InvariantCulture, out var expected))
                FuzzAssert.Fail("The BCL rejects a value the fast parser accepts: " + FuzzAssert.Format(text));

            // The round-trip format distinguishes every value, including the negative zero
            var expectedText = expected.ToString("R", CultureInfo.InvariantCulture);
            FuzzAssert.Equal(expectedText, result.ToString("R", CultureInfo.InvariantCulture), "The " + typeof(T).Name + " is not the value of the BCL for " + FuzzAssert.Format(text));
            FuzzAssert.Equal(expectedText, utf8Result.ToString("R", CultureInfo.InvariantCulture), "The UTF-8 " + typeof(T).Name + " is not the value of the BCL for " + FuzzAssert.Format(text));
        }
    }

    private static IEnumerable<FuzzSeed> GetFloatingPointSeeds()
    {
        // The values of the unit tests
        string[] values =
        [
            "0", "-0", "+0", "0.0", "-0.000", "1", "-1", "+1.5", "1.50", "-123.45", "0.1", "0.3", "3.14159", "1.", ".5", "-.5", "1e3", "1E3", "1e+3",
            "1.5e-3", "1.e3", ".5e1", "1e0000", "1e22", "1e23", "1e-22", "1e-23", "9007199254740992", "9007199254740993", "16777216", "16777217", "2048",
            "2049", "65504", "65520", "100000", "0.00001", "1234567890123456789", "12345678901234567890", "0.0000000000000000001", "12345.678901234567",
            "1.7976931348623157E+308", "4.9406564584124654E-324", "1e400", "NaN", "-Infinity", "1,000", " 1", "1 ", "1e", "1e+", "e3", ".", "-", "--1",
            "1.2.3", "0x10", "",
        ];
        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText("floating-point-" + i.ToString(CultureInfo.InvariantCulture), values[i]);
        }
    }

    /// <summary>
    /// <c>flags, names, 0xFF, lookups</c>: bit 0 of <c>flags</c> compares the names ignoring the case; <c>names</c> are the names of the header, separated
    /// by commas; each byte of <c>lookups</c> picks a name of a column, one of another case, or a missing one, as the instance of the header, a
    /// reused instance, or a new one. Whatever the order and the instances, the header finds the first column with the name, as a linear search does.
    /// </summary>
    private static void HeaderLookup(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var names = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0xFF)).Split(',');
        if (names.Length > 64)
        {
            names = names[..64];
        }

        var comparer = (flags & 1) != 0 ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var header = new CsvHeader(names, comparer);
        string[] queries = [.. names.Select(name => new string(name.AsSpan())), .. names.Select(name => name.ToUpperInvariant()), "missing"];
        foreach (var lookup in data)
        {
            var index = lookup & 0x3F;
            var query = (lookup >> 6) switch
            {
                0 => queries[index % queries.Length],
                1 => new string(queries[index % queries.Length].AsSpan()),
                _ => names[index % names.Length],
            };

            var expected = Array.FindIndex(names, name => comparer.Equals(name, query));
            FuzzAssert.Equal(expected, header.IndexOf(query), "The header does not find the first column with the name: " + query);
        }
    }

    private static IEnumerable<FuzzSeed> GetHeaderSeeds()
    {
        yield return new FuzzSeed("header-in-order", (byte[])[0x00, .. "Id,Name,Email,Amount"u8, 0xFF, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3]);
        yield return new FuzzSeed("header-any-order", (byte[])[0x01, .. "Id,Name,id,Email"u8, 0xFF, 3, 2, 1, 0, 0x44, 0x83, 6, 5, 4, 8, 0xC2, 2, 1]);
        yield return new FuzzSeed("header-wide", (byte[])[0x00, .. Encoding.UTF8.GetBytes(string.Join(',', Enumerable.Range(0, 20).Select(i => "c" + (i % 18).ToString(CultureInfo.InvariantCulture)))), 0xFF, 0, 1, 19, 18, 0x52, 0x93, 40, 20, 19, 0, 17]);
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
            var dialect = (byte)((index % 4) switch { 0 => 0x00, 1 => 0x40, 2 => 0x09, _ => 0x10 | 0x03 });
            if (index % 5 is 0)
            {
                dialect |= 0x80;
            }

            yield return FuzzSeed.FromText(name, [dialect, (byte)(index % 3 is 0 ? 0 : 0x13)], text);
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetReaderBadDataSeeds()
    {
        var index = 0;
        foreach (var (name, text) in GetTexts())
        {
            // Comma and double quote, with and without a header, with the column count checked, with the values trimmed, then other dialects
            var dialect = (byte)((index % 4) switch { 0 => 0x00, 1 => 0x40, 2 => 0x09, _ => 0x10 | 0x03 });
            var chunks = (byte)((index % 3 is 0 ? 0 : 0x13) | (index % 5 is 1 ? 0x40 : 0) | (index % 7 is 2 ? 0x80 : 0));
            yield return FuzzSeed.FromText("bad-data-" + name, [dialect, chunks], text);
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

        // Records longer than the SIMD blocks, whose quotes, escaped quotes and line terminators fall anywhere in them, then records the SIMD scan
        // leaves to the general scan, and regular records again
        yield return ("long-records", string.Concat(Enumerable.Range(0, 12).Select(i => "\"" + new string('a', i * 7) + "\"\"x\"," + new string('b', 61 - i) + ",\"c,d\"" + (i % 3 is 0 ? "\r\n" : "\n"))) + "\"e\"f,g\nh,\"i\nj\"\n\"k\",l\r\n" + string.Concat(Enumerable.Repeat("m,\"n\"\"o\",p\n", 10)));

        // ASCII records, whose values become strings without validation, around records with other characters in the same blocks
        yield return ("ascii-records", string.Concat(Enumerable.Range(0, 16).Select(i => new string('a', i * 5) + "," + (i % 4 is 1 ? "é" : i % 4 is 2 ? "😀" : "b") + ",\"c\"\"d\"\n")));
    }

    [GeneratedRegex("""(?<![@$"\\])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();

    private sealed record BadData(CsvBadDataKind Kind, long? RowIndex, long LineNumber, long Position, int? ColumnIndex, string? ColumnName, string Message, string Exception)
    {
        public static BadData Create(CsvBadDataContext context) => new(context.Kind, context.RowIndex, context.LineNumber, context.Position, context.ColumnIndex, context.ColumnName, context.Message, context.Exception.Message);
    }

    private sealed class DeserializeErrorState(CsvDeserializationErrorAction action)
    {
        public CsvDeserializationErrorAction Action { get; } = action;

        public List<DeserializeError> Errors { get; } = [];
    }

    private sealed record DeserializeError(CsvDeserializationErrorKind Kind, long? RowIndex, long? LineNumber, int? ColumnIndex, string? MemberName, string? RawValue);
}
