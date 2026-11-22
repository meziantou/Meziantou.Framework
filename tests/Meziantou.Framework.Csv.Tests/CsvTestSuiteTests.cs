using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Meziantou.Framework.Csv.Tests;

/// <summary>Runs public CSV test suites: csv-spectrum and the parser tests of Papa Parse (see <c>TestData/*/README.md</c>).</summary>
/// <remarks>
/// The records are compared as JSON, in the shape of the expected data of each suite. Each case is read by <see cref="CsvReader"/> and, when the
/// options are ASCII, by <see cref="Utf8CsvReader"/>, which must agree.
/// </remarks>
public sealed class CsvTestSuiteTests
{
    private static readonly JsonWriterOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly Lazy<Dictionary<string, JsonElement>> CsvSpectrum = new(() => Load("csv-spectrum"));

    private static readonly Lazy<Dictionary<string, JsonElement>> PapaParse = new(() => Load("papaparse"));

    // The cases whose expected data is a choice of Papa Parse that the reader does not make. The test checks that they still differ, so that a
    // change of the reader that makes one of them pass is noticed and removes it from this list.
    private static readonly Dictionary<string, string> PapaParseDeviations = new(StringComparer.Ordinal)
    {
        ["core/Fast mode with blank line at end"] = TrailingLineTerminator,
        ["core/Input with only a commented line and blank line after"] = TrailingLineTerminator,
        ["core/One column input with empty fields"] = TrailingLineTerminator,
        ["parse/Blank lines at end"] = TrailingLineTerminator,
        ["parse/Empty lines"] = TrailingLineTerminator,
        ["core/Simple duplicated header names"] = DuplicateColumnNames,
        ["core/Duplicated header names existing column"] = DuplicateColumnNames,
        ["core/Duplicate header names with __proto__ field"] = DuplicateColumnNames,
        ["parse/Quoted fields with spaces after closing quote"] = SpacesAfterClosingQuote,
        ["parse/Quoted fields with spaces between closing quote and newline and contains newline"] = SpacesAfterClosingQuote,
        ["parse/Quoted fields with spaces between closing quote and next delimiter"] = SpacesAfterClosingQuote,
        ["parse/Quoted fields with spaces between closing quote and next delimiter and contains delimiter"] = SpacesAfterClosingQuote,
        ["parse/Quoted fields with spaces between closing quote and next new line"] = SpacesAfterClosingQuote,
        ["core/Quoted field has invalid trailing quote after delimiter"] = QuoteNotFollowedBySeparator,
        ["core/Quoted field has invalid trailing quote after delimiter with a valid closer"] = QuoteNotFollowedBySeparator,
        ["core/Quoted field has invalid trailing quote after new line"] = QuoteNotFollowedBySeparator,
        ["core/Quoted field has invalid trailing quote before delimiter"] = QuoteNotFollowedBySeparator,
        ["parse/Mixed slash n and slash r should choose first as precident"] = "Papa Parse uses the first line terminator of the data and reads the others as characters; the reader accepts \\r, \\n and \\r\\n everywhere",
    };

    // The cases whose expected data is wrong upstream
    private static readonly Dictionary<string, string> CsvSpectrumDeviations = new(StringComparer.Ordinal)
    {
        ["location_coordinates"] = "The expected JSON of csv-spectrum is an object instead of an array of objects, and its phone number is not the one of the CSV file",
    };

    // The files that are not RFC 4180 data, which the strict reader rejects: a value that is not quoted contains a quote
    private static readonly HashSet<string> CsvSpectrumNotRfc4180 = new(StringComparer.Ordinal) { "location_coordinates" };

    private const string TrailingLineTerminator = "A line terminator at the end of the data ends the last record (RFC 4180); Papa Parse returns an empty record after it";

    private const string DuplicateColumnNames = "The header keeps the names of the columns as they are; Papa Parse renames the duplicates with a _1, _2, ... suffix";

    private const string SpacesAfterClosingQuote = "The characters after a closing quote are part of the value (CsvTrimOptions.Outer removes the spaces); Papa Parse ignores the spaces before a separator or a line terminator";

    private const string QuoteNotFollowedBySeparator = "A quote ends a quoted value; Papa Parse reads a quote that is not followed by a separator or a line terminator as a character of the value";

    public static TheoryData<string> CsvSpectrumCases() => [.. CsvSpectrum.Value.Keys];

    /// <summary>Every csv-spectrum file has a header row and reads to the expected objects; the strict reader reads the RFC 4180 files the same way, and rejects the others.</summary>
    [Theory]
    [MemberData(nameof(CsvSpectrumCases))]
    public void CsvSpectrumSuite(string name)
    {
        var testCase = CsvSpectrum.Value[name];
        var text = testCase.GetProperty("csv").GetString()!;
        var expected = ToJson(testCase.GetProperty("expected"));

        var actual = ReadAsJson(text, CsvReaderOptions.Default, objects: true, preview: 0, includeHeaderRow: false);
        if (CsvSpectrumDeviations.ContainsKey(name))
        {
            Assert.NotEqual(expected, actual);
        }
        else
        {
            Assert.Equal(expected, actual);
        }

        var strictOptions = CsvReaderOptions.Default with { Strict = true };
        if (CsvSpectrumNotRfc4180.Contains(name))
        {
            Assert.Throws<CsvException>(() => ReadAsJson(text, strictOptions, objects: true, preview: 0, includeHeaderRow: false));
        }
        else
        {
            Assert.Equal(actual, ReadAsJson(text, strictOptions, objects: true, preview: 0, includeHeaderRow: false));
        }
    }

    public static TheoryData<string> PapaParseCases()
    {
        var data = new TheoryData<string>();
        foreach (var (name, testCase) in PapaParse.Value)
        {
            if (TryCreateOptions(name, testCase, out _, out _))
            {
                data.Add(name);
            }
        }

        return data;
    }

    /// <summary>
    /// A case reads to the expected data. When Papa Parse reports a quote error, the strict reader throws; when it reports a row with the wrong number
    /// of fields, the reader that validates the column count throws.
    /// </summary>
    [Theory]
    [MemberData(nameof(PapaParseCases))]
    public void PapaParseSuite(string name)
    {
        var testCase = PapaParse.Value[name];
        Assert.True(TryCreateOptions(name, testCase, out var options, out var preview));
        var text = testCase.GetProperty("input").GetString()!;

        // The core parser returns the header row as a row; Papa.parse() returns objects when there is a header row
        var isCore = name.StartsWith("core/", StringComparison.Ordinal);
        var actual = ReadAsJson(text, options, objects: options.HasHeaderRow && !isCore, preview, includeHeaderRow: isCore);
        var expected = ToJson(testCase.GetProperty("data"));
        if (PapaParseDeviations.ContainsKey(name))
        {
            Assert.NotEqual(expected, actual);
        }
        else
        {
            Assert.Equal(expected, actual);
        }

        var errors = testCase.GetProperty("errors").EnumerateArray().Select(error => error.GetString()!).ToArray();
        if (errors.Any(error => error.StartsWith("Quotes/", StringComparison.Ordinal)))
        {
            Assert.Throws<CsvException>(() => ReadAsJson(text, options with { Strict = true }, objects: false, preview: 0, includeHeaderRow: false));
        }

        if (options.HasHeaderRow && errors.Any(error => error.StartsWith("FieldMismatch/", StringComparison.Ordinal)))
        {
            Assert.Throws<CsvException>(() => ReadAsJson(text, options with { ValidateColumnCount = true }, objects: false, preview: 0, includeHeaderRow: false));
        }
    }

    // Maps the configuration of a Papa Parse case to the options of the reader. Returns false when the case uses a feature the reader does not have.
    private static bool TryCreateOptions(string name, JsonElement testCase, out CsvReaderOptions options, out int preview)
    {
        // Papa.parse() detects the separator when there is none, the core parser uses a comma
        options = CsvReaderOptions.Default with { HasHeaderRow = false, Separator = name.StartsWith("core/", StringComparison.Ordinal) ? ',' : null };
        preview = 0;
        if (!testCase.TryGetProperty("config", out var config))
            return true;

        string? quote = null;
        string? escape = null;
        foreach (var property in config.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "delimiter" when value.GetString() is "":
                    options = options with { Separator = null };
                    break;

                case "delimiter" when value.GetString() is [var separator] && separator is not ('\r' or '\n'):
                    options = options with { Separator = separator };
                    break;

                case "quoteChar" when value.GetString() is [var quoteChar]:
                    quote = value.GetString();
                    options = options with { Quote = quoteChar };
                    break;

                case "escapeChar":
                    escape = value.GetString();
                    break;

                case "comments" when value.ValueKind is JsonValueKind.True:
                    options = options with { CommentCharacter = '#' };
                    break;

                case "comments" when value.ValueKind is JsonValueKind.False:
                    break;

                case "comments" when value.ValueKind is JsonValueKind.String && value.GetString() is [var comment]:
                    options = options with { CommentCharacter = comment };
                    break;

                case "header":
                    options = options with { HasHeaderRow = value.GetBoolean() };
                    break;

                case "skipEmptyLines" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    options = options with { SkipEmptyLines = value.GetBoolean() };
                    break;

                case "preview":
                    preview = value.GetInt32();
                    break;

                // The fast mode does not handle quotes
                case "fastMode":
                    if (value.GetBoolean())
                    {
                        options = options with { Quote = null };
                    }

                    break;

                // The reader accepts every line terminator
                case "newline":
                    break;

                // Multi-character separators and comment prefixes, "greedy" empty lines, typed values, skipped lines, and separator candidates
                case "delimiter" or "comments" or "skipEmptyLines" or "quoteChar" or "dynamicTyping" or "skipFirstNLines" or "delimitersToGuess":
                    return false;

                default:
                    throw new InvalidOperationException($"The case '{name}' uses the unknown option '{property.Name}'");
            }
        }

        // A quote is escaped by doubling it, never by another character
        return escape is null || escape == (quote ?? "\"");
    }

    private static string ReadAsJson(string text, CsvReaderOptions options, bool objects, int preview, bool includeHeaderRow)
    {
        var records = new List<string[]>();
        string[]? header = null;
        using (var reader = CsvReader.CreateFromString(text, options))
        {
            while ((preview is 0 || records.Count < preview) && reader.Read())
            {
                records.Add(reader.Current.ToStringArray());
            }

            header = reader.Header?.ColumnNames.ToArray();
        }

        if (options.Separator is null or <= '\x7F' && options.Quote is null or <= '\x7F' && options.CommentCharacter is null or <= '\x7F')
        {
            var utf8Records = new List<string[]>();
            using var reader = Utf8CsvReader.Create(Encoding.UTF8.GetBytes(text), options);
            while ((preview is 0 || utf8Records.Count < preview) && reader.Read())
            {
                utf8Records.Add(reader.Current.ToStringArray());
            }

            Assert.Equal(ToJson(header, records, objects: false, includeHeaderRow: true), ToJson(reader.Header?.ColumnNames.ToArray(), utf8Records, objects: false, includeHeaderRow: true));
        }

        return ToJson(header, records, objects, includeHeaderRow);
    }

    // The shape of the expected data: arrays of values, or objects whose extra values are in "__parsed_extra" when there is a header row
    private static string ToJson(string[]? header, List<string[]> records, bool objects, bool includeHeaderRow)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            writer.WriteStartArray();
            if (includeHeaderRow && header is not null)
            {
                WriteArray(writer, header);
            }

            foreach (var record in records)
            {
                // An empty line is a record without columns; Papa Parse and csv-spectrum have no such record, and write it as one empty value
                if (record is [])
                {
                    WriteArray(writer, [""]);
                    continue;
                }

                if (!objects || header is null)
                {
                    WriteArray(writer, record);
                    continue;
                }

                writer.WriteStartObject();
                for (var i = 0; i < Math.Min(header.Length, record.Length); i++)
                {
                    writer.WriteString(header[i], record[i]);
                }

                if (record.Length > header.Length)
                {
                    writer.WritePropertyName("__parsed_extra");
                    WriteArray(writer, record[header.Length..]);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteArray(Utf8JsonWriter writer, string[] values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string ToJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Dictionary<string, JsonElement> Load(string suite)
    {
        using var stream = typeof(CsvTestSuiteTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Csv.Tests.TestData." + suite + ".cases.json")!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!, item => item.Clone(), StringComparer.Ordinal);
    }
}
