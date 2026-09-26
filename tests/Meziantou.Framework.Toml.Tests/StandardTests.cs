using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Syntax;
using LanguageToml = Meziantou.Framework.Language.Toml;

namespace Meziantou.Framework.Toml.Tests;

/// <summary>
/// Tests against the official TOML specs of TOML https://github.com/BurntSushi/toml-test
/// </summary>
public static class StandardTests
{
    private const string InvalidSpec = "invalid";
    private const string ValidSpec = "valid";

    private static readonly Lazy<Dictionary<string, TomlTestCase>> Corpus = new(LoadCorpus);

    [Theory]
    [MemberData(nameof(ListTomlFiles), ValidSpec)]
    public static void SpecValid(string name)
    {
        var testCase = GetCase(name);
        ValidateSpec(ValidSpec, testCase);
    }

    [Theory]
    [MemberData(nameof(ListTomlFiles), InvalidSpec)]
    public static void SpecInvalid(string name)
    {
        var testCase = GetCase(name);
        ValidateSpec(InvalidSpec, testCase);
    }

    /// <summary>A valid document stays valid, and means the same, with Windows line breaks.</summary>
    [Theory]
    [MemberData(nameof(ListTomlFiles), ValidSpec)]
    public static void SpecValid_WithCarriageReturnLineFeeds(string name)
    {
        var testCase = GetCase(name);
        Assert.NotNull(testCase.Toml);
        var toml = testCase.Toml.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

        var doc = SyntaxParser.Parse(toml, testCase.InputName);
        Assert.False(doc.HasErrors, message: "Unexpected parsing errors");
        Assert.Equal(toml, doc.ToString(), message: "The roundtrip doesn't match");

        // A parser may normalize the line breaks of a multiline string, and this one keeps them
        var expectedJson = NormalizeLineEndings(NormalizeJson(ParseTomlTestJson(testCase.Json!))!);
        var actualJson = NormalizeLineEndings(ModelHelper.ToJson(TomlSerializer.Deserialize<TomlTable>(toml)));
        AssertTomlTestJsonEquivalent(expectedJson, actualJson, testCase.InputName, toml, doc, doc.ToString());
    }

    private static JsonNode NormalizeLineEndings(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => new JsonObject(obj.Select(property => KeyValuePair.Create(property.Key, (JsonNode?)NormalizeLineEndings(property.Value!)))),
            JsonArray array => new JsonArray(array.Select(item => (JsonNode?)NormalizeLineEndings(item!)).ToArray()),
            JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(text.Replace("\r\n", "\n", StringComparison.Ordinal)),
            _ => node.DeepClone(),
        };
    }

    private static void ValidateSpec(string type, TomlTestCase testCase)
    {
        var inputName = testCase.InputName;
        if (testCase.Toml is not { } toml)
        {
            // A file that is not valid UTF-8 is rejected by the decoder, before there is any text to parse
            Assert.Equal(InvalidSpec, type);
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(new MemoryStream(testCase.Bytes)));
            return;
        }

        var doc = SyntaxParser.Parse(toml, inputName);
        var roundtrip = doc.ToString();
        switch (type)
        {
            case ValidSpec:
                if (doc.HasErrors || toml != roundtrip)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine($"Testing {inputName}");
                    Dump(toml, doc, roundtrip);
                }
                Assert.False(doc.HasErrors, message: "Unexpected parsing errors");
                // Only in the case of a valid spec we check for rountrip
                Assert.Equal(toml, roundtrip, message: "The roundtrip doesn't match");

                // Read the original json (toml-test encodes datetimes as strings; avoid Json.NET date coercion).
                var expectedJson = (JsonObject)NormalizeJson(ParseTomlTestJson(testCase.Json!))!;
                // Convert to the untyped model.
                var model = TomlSerializer.Deserialize<TomlTable>(toml);
                // Convert the model into the expected json
                var computedJson = ModelHelper.ToJson(model);
                AssertTomlTestJsonEquivalent(expectedJson, computedJson, inputName, toml, doc, roundtrip);

                var modelFromStream = TomlSerializer.Deserialize<TomlTable>(new MemoryStream(testCase.Bytes));
                AssertTomlTestJsonEquivalent(expectedJson, ModelHelper.ToJson(modelFromStream), inputName, toml, doc, roundtrip);

                var tolerantParser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
                ReadAllEvents(tolerantParser);
                Assert.False(tolerantParser.HasErrors, message: "The tolerant parser must not report an error");

                var tomlFromModel = TomlSerializer.Serialize(model);

                var model2 = TomlSerializer.Deserialize<TomlTable>(tomlFromModel);
                var computedJson2 = ModelHelper.ToJson(model2);
                AssertTomlTestJsonEquivalent(expectedJson, computedJson2, inputName, toml, doc, roundtrip, tomlFromModel);
                break;
            case InvalidSpec:
                if (!doc.HasErrors)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine($"Testing {inputName}");
                    Dump(toml, doc, roundtrip);
                }

                Assert.True(doc.HasErrors, message: "The TOML requires parsing/validation errors");
                Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(toml));
                Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(new MemoryStream(testCase.Bytes)));
                Assert.Throws<TomlException>(() => ReadAllEvents(TomlParser.Create(toml)));

                var tolerantInvalidParser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
                ReadAllEvents(tolerantInvalidParser);
                Assert.True(tolerantInvalidParser.HasErrors, message: "The tolerant parser must report an error");
                break;
        }

        {
            using var reader = new StringReader(toml);
            var docFromReader = SyntaxParser.Parse(reader, inputName);
            var roundtripFromReader = docFromReader.ToString();
            if (roundtrip != roundtripFromReader)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"Testing {inputName}");
                Dump(toml, doc, roundtrip);
            }
            Assert.Equal(roundtrip, roundtripFromReader, message: "The TextReader version doesn't match with the string version");
        }
    }

    public static TheoryData<int> DifferentialSeeds() => new(Enumerable.Range(0, 8));

    /// <summary>
    /// Mutates the documents of the corpus and checks that every parser agrees with
    /// <c>Meziantou.Framework.Language.Toml</c>, an independent implementation, about which ones are valid.
    /// </summary>
    [Theory]
    [MemberData(nameof(DifferentialSeeds))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public static void Differential_AgreesWithLanguageToml(int seed)
    {
        var sources = Corpus.Value.Values
            .Where(testCase => testCase.IsToml11 && testCase.Toml is not null)
            .Select(testCase => testCase.Toml!)
            .ToArray();
        var random = new Random(seed);
        var failures = new List<string>();
        for (var i = 0; i < 1500 && failures.Count < 10; i++)
        {
            var toml = sources[random.Next(sources.Length)];
            var mutationCount = random.Next(1, 4);
            for (var j = 0; j < mutationCount; j++)
            {
                toml = Mutate(toml, random);
            }

            var failure = CompareWithLanguageToml(toml);
            if (failure is not null)
            {
                failures.Add($"{failure}: {JsonSerializer.Serialize(toml)}");
            }
        }

        Assert.Empty(failures, string.Join(Environment.NewLine, failures));
    }

    private const string MutationAlphabet = "=[]{},.\"'#\n\r\t _-+:0123456789eExobTZaz\\";

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    private static string Mutate(string toml, Random random)
    {
        var position = random.Next(toml.Length + 1);
        switch (random.Next(5))
        {
            case 0 when toml.Length > 0 && position < toml.Length:
                return toml.Remove(position, 1);

            case 1 when toml.Length > 0 && position < toml.Length:
                return string.Concat(toml.AsSpan(0, position), MutationAlphabet[random.Next(MutationAlphabet.Length)].ToString(), toml.AsSpan(position + 1));

            case 2:
                var lines = toml.Split('\n');
                var line = random.Next(lines.Length);
                return string.Join('\n', lines.Take(line + 1).Append(lines[line]).Concat(lines.Skip(line + 1)));

            case 3:
                var allLines = toml.Split('\n');
                var from = random.Next(allLines.Length);
                var to = random.Next(allLines.Length);
                (allLines[from], allLines[to]) = (allLines[to], allLines[from]);
                return string.Join('\n', allLines);

            default:
                return toml.Insert(position, MutationAlphabet[random.Next(MutationAlphabet.Length)].ToString());
        }
    }

    private static string? CompareWithLanguageToml(string toml)
    {
        var expected = LanguageToml.TomlSyntaxTree.ParseText(toml, new LanguageToml.TomlParseOptions { Version = LanguageToml.TomlVersion.V1_1 }).GetDiagnostics().Count == 0;

        var doc = SyntaxParser.Parse(toml);
        if (doc.HasErrors == expected)
        {
            // A few valid values do not fit in the .NET types, and both implementations do not handle them the same way
            if (expected && doc.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("cannot be represented", StringComparison.Ordinal)))
            {
                return null;
            }

            return $"SyntaxParser: expected valid={expected}";
        }

        if (expected && doc.ToString() != toml)
        {
            return "SyntaxParser: the roundtrip doesn't match";
        }

        var tolerantParser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
        ReadAllEvents(tolerantParser);
        if (tolerantParser.HasErrors == expected)
        {
            return $"TomlParser (tolerant): expected valid={expected}";
        }

        try
        {
            ReadAllEvents(TomlParser.Create(toml));
            if (!expected)
            {
                return "TomlParser: expected an error";
            }
        }
        catch (TomlException ex)
        {
            if (expected)
            {
                return $"TomlParser: unexpected error {ex.Message}";
            }
        }

        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(toml)!;
            if (!expected)
            {
                return "Deserialize: expected an error";
            }
        }
        catch (TomlException ex)
        {
            return expected ? $"Deserialize: unexpected error {ex.Message}" : null;
        }

        var json = ModelHelper.ToJson(model);
        var roundtrip = ModelHelper.ToJson(TomlSerializer.Deserialize<TomlTable>(TomlSerializer.Serialize(model)));
        return JsonNode.DeepEquals(json, roundtrip) ? null : "Serialize: the document doesn't roundtrip";
    }

    private static void ReadAllEvents(TomlParser parser)
    {
        while (parser.MoveNext())
        {
        }
    }

    private static JsonNode? NormalizeJson(JsonNode? token)
    {
        if (token is JsonArray array)
        {
            var newArray = new JsonArray();
            foreach (var item in array)
            {
                newArray.Add(NormalizeJson(item));
            }

            return newArray;
        }
        else if (token is JsonObject obj)
        {
            var newObject = new JsonObject();

            var items = new List<KeyValuePair<string, JsonNode?>>();
            foreach (var item in obj)
            {
                items.Add(item);
            }

            foreach (var item in items.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                newObject.Add(item.Key, NormalizeJson(item.Value));
            }

            return newObject;
        }
        else if (token is JsonValue value && value.TryGetValue<string>(out var str))
        {
            if (string.Equals(str, "inf", StringComparison.Ordinal)) return JsonValue.Create("+inf");
            if (str.Length > 0 && char.IsDigit(str[0]) && str.Contains('.', StringComparison.Ordinal) && str.Contains('e', StringComparison.Ordinal))
            {
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    return JsonValue.Create(TomlFormatHelper.ToString(doubleValue));
                }
            }
        }

        return token?.DeepClone();
    }


    internal static void Dump(string input, DocumentSyntax doc, string roundtrip)
    {
        TestContext.Current.TestOutputHelper?.WriteLine("");
        DisplayHeader("input");
        TestContext.Current.TestOutputHelper?.WriteLine(input);

        TestContext.Current.TestOutputHelper?.WriteLine("");
        DisplayHeader("round-trip");
        TestContext.Current.TestOutputHelper?.WriteLine(roundtrip);

        if (doc.Diagnostics.Count > 0)
        {
            TestContext.Current.TestOutputHelper?.WriteLine("");
            DisplayHeader("messages");

            foreach (var syntaxMessage in doc.Diagnostics)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(syntaxMessage.ToString());
            }
        }
    }

    internal static void DisplayHeader(string name)
    {
        TestContext.Current.TestOutputHelper?.WriteLine($"// ----------------------------------------------------------");
        TestContext.Current.TestOutputHelper?.WriteLine($"// {name}");
        TestContext.Current.TestOutputHelper?.WriteLine($"// ----------------------------------------------------------");
    }

    /// <summary>Lists the toml-test cases of the TOML 1.1 list.</summary>
    public static TheoryData<string> ListTomlFiles(string type)
    {
        var tests = new TheoryData<string>();
        foreach (var testCase in Corpus.Value.Values)
        {
            if (testCase.IsToml11 && testCase.Name.StartsWith(type + "/", StringComparison.Ordinal))
            {
                tests.Add(testCase.Name);
            }
        }

        return tests;
    }

    /// <summary>Lists the documents that are invalid in TOML 1.0 only, which are the TOML 1.1 extensions.</summary>
    public static TheoryData<string> ListToml10OnlyInvalidFiles()
    {
        var tests = new TheoryData<string>();
        foreach (var testCase in Corpus.Value.Values)
        {
            // The examples of the 1.0 specification are listed under another name for 1.1
            if (!testCase.IsToml11 && testCase.Name.StartsWith(InvalidSpec + "/", StringComparison.Ordinal) && !testCase.Name.StartsWith("invalid/spec-1.0.0/", StringComparison.Ordinal))
            {
                tests.Add(testCase.Name);
            }
        }

        return tests;
    }

    /// <summary>
    /// Gets a toml-test case from the embedded corpus (see files/toml-test/README.md in Meziantou.Framework.Language.Toml.Tests).
    /// </summary>
    /// <param name="name">The path of the case relative to the toml-test <c>tests</c> folder, without the extension.</param>
    internal static TomlTestCase GetCase(string name)
    {
        Assert.True(Corpus.Value.TryGetValue(name, out var testCase), message: $"The toml-test case `{name}` does not exist");
        return testCase;
    }

    private static Dictionary<string, TomlTestCase> LoadCorpus()
    {
        using var stream = typeof(StandardTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Toml.Tests.files.toml-test.cases.json")!;
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, TomlTestCase>(StringComparer.Ordinal);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            string? toml;
            byte[] bytes;
            if (item.TryGetProperty("toml", out var text))
            {
                toml = text.GetString()!;
                bytes = Encoding.UTF8.GetBytes(toml);
            }
            else
            {
                bytes = Convert.FromBase64String(item.GetProperty("tomlBase64").GetString()!);
                try
                {
                    toml = utf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    toml = null;
                }
            }

            var versions = item.GetProperty("versions").EnumerateArray().Select(version => version.GetString()).ToArray();
            var json = item.TryGetProperty("expected", out var expected) ? expected.GetRawText() : null;
            result.Add(name, new TomlTestCase(name, Path.GetFileName(name) + ".toml", toml, bytes, json, versions.Contains("1.1.0", StringComparer.Ordinal)));
        }

        return result;
    }

    /// <param name="Toml">The text of the case, or <see langword="null"/> when <paramref name="Bytes"/> is not valid UTF-8.</param>
    internal sealed record TomlTestCase(string Name, string InputName, string? Toml, byte[] Bytes, string? Json, bool IsToml11);

    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    private static JsonObject ParseTomlTestJson(string json)
    {
        return JsonNode.Parse(json)!.AsObject();
    }

    private static void AssertTomlTestJsonEquivalent(JsonNode expected, JsonNode actual, string inputName, string toml, DocumentSyntax doc, string roundtrip, string? tomlFromModel = null)
    {
        if (TryCompareTomlTestJson(expected, actual, out var difference))
        {
            return;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Testing {inputName}");
        Dump(toml, doc, roundtrip);
        DisplayHeader("json");
        TestContext.Current.TestOutputHelper?.WriteLine(actual.ToJsonString(IndentedJsonOptions));
        DisplayHeader("expected json");
        TestContext.Current.TestOutputHelper?.WriteLine(expected.ToJsonString(IndentedJsonOptions));
        if (tomlFromModel is not null)
        {
            DisplayHeader("toml from model");
            TestContext.Current.TestOutputHelper?.WriteLine(tomlFromModel);
        }

        Assert.Fail($"TOML test JSON mismatch: {difference}");
    }

    private static bool TryCompareTomlTestJson(JsonNode expected, JsonNode actual, out string difference)
    {
        var path = "$";
        if (TryCompareTomlTestJsonCore(expected, actual, ref path, out difference))
        {
            return true;
        }

        difference = $"{path}: {difference}";
        return false;
    }

    private static bool TryCompareTomlTestJsonCore(JsonNode expected, JsonNode actual, ref string path, out string difference)
    {
        if (expected.GetValueKind() != actual.GetValueKind())
        {
            difference = $"Expected token type {expected.GetValueKind()} but was {actual.GetValueKind()}.";
            return false;
        }

        switch (expected.GetValueKind())
        {
            case JsonValueKind.Object:
                return TryCompareTomlTestJsonObject(expected.AsObject(), actual.AsObject(), ref path, out difference);
            case JsonValueKind.Array:
                return TryCompareTomlTestJsonArray(expected.AsArray(), actual.AsArray(), ref path, out difference);
            default:
                if (JsonNode.DeepEquals(expected, actual))
                {
                    difference = string.Empty;
                    return true;
                }

                difference = $"Expected `{expected}` but was `{actual}`.";
                return false;
        }
    }

    private static bool TryCompareTomlTestJsonObject(JsonObject expected, JsonObject actual, ref string path, out string difference)
    {
        if (TryGetTomlTestTypedValue(expected, out var expectedType, out var expectedValue))
        {
            if (!TryGetTomlTestTypedValue(actual, out var actualType, out var actualValue))
            {
                difference = "Expected a typed TOML value object.";
                return false;
            }

            if (!string.Equals(expectedType, actualType, StringComparison.Ordinal))
            {
                difference = $"Expected type `{expectedType}` but was `{actualType}`.";
                return false;
            }

            return expectedType switch
            {
                "string" => CompareString(expectedValue, actualValue, out difference),
                "bool" => CompareBool(expectedValue, actualValue, out difference),
                "integer" => CompareInteger(expectedValue, actualValue, out difference),
                "float" => CompareFloat(expectedValue, actualValue, out difference),
                "datetime" => CompareDateTime(expectedValue, actualValue, out difference),
                "array" => TryCompareTomlTestJsonCore(expectedValue, actualValue, ref path, out difference),
                _ => TryCompareTomlTestJsonCore(expectedValue, actualValue, ref path, out difference),
            };
        }

        var expectedProperties = expected.ToList();
        var actualProperties = actual.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        foreach (var expectedProperty in expectedProperties)
        {
            if (!actualProperties.TryGetValue(expectedProperty.Key, out var actualValue))
            {
                difference = $"Missing property `{expectedProperty.Key}`.";
                return false;
            }

            var previous = path;
            path = $"{path}.{expectedProperty.Key}";
            if (!TryCompareTomlTestJsonCore(expectedProperty.Value!, actualValue!, ref path, out difference))
            {
                return false;
            }
            path = previous;
        }

        if (actualProperties.Count != expectedProperties.Count)
        {
            var extra = actualProperties.Keys.Except(expectedProperties.Select(p => p.Key), StringComparer.Ordinal).FirstOrDefault();
            difference = extra is null ? "Object size mismatch." : $"Unexpected property `{extra}`.";
            return false;
        }

        difference = string.Empty;
        return true;
    }

    private static bool TryGetTomlTestTypedValue(JsonObject obj, out string type, out JsonNode value)
    {
        type = string.Empty;
        value = default!;

        if (!obj.TryGetPropertyValue("type", out var typeToken) || !obj.TryGetPropertyValue("value", out var valueToken) || typeToken is null || valueToken is null)
        {
            return false;
        }

        if (typeToken.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        type = typeToken.GetValue<string>();
        value = valueToken;
        return type.Length > 0;
    }

    private static bool TryCompareTomlTestJsonArray(JsonArray expected, JsonArray actual, ref string path, out string difference)
    {
        if (expected.Count != actual.Count)
        {
            difference = $"Expected array length {expected.Count} but was {actual.Count}.";
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var previous = path;
            path = $"{path}[{index}]";
            if (!TryCompareTomlTestJsonCore(expected[index]!, actual[index]!, ref path, out difference))
            {
                return false;
            }
            path = previous;
        }

        difference = string.Empty;
        return true;
    }

    private static bool CompareString(JsonNode expected, JsonNode actual, out string difference)
    {
        var expectedValue = expected.GetValue<string>();
        var actualValue = actual.GetValue<string>();
        if (string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
        {
            difference = string.Empty;
            return true;
        }

        difference = $"Expected string `{expectedValue}` but was `{actualValue}`.";
        return false;
    }

    private static bool CompareBool(JsonNode expected, JsonNode actual, out string difference)
    {
        if (!TryParseBool(expected.GetValue<string>(), out var expectedValue) ||
            !TryParseBool(actual.GetValue<string>(), out var actualValue))
        {
            difference = "Invalid boolean representation.";
            return false;
        }

        if (expectedValue == actualValue)
        {
            difference = string.Empty;
            return true;
        }

        difference = $"Expected bool `{expectedValue}` but was `{actualValue}`.";
        return false;
    }

    private static bool CompareInteger(JsonNode expected, JsonNode actual, out string difference)
    {
        if (!long.TryParse(expected.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedValue) ||
            !long.TryParse(actual.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var actualValue))
        {
            difference = "Invalid integer representation.";
            return false;
        }

        if (expectedValue == actualValue)
        {
            difference = string.Empty;
            return true;
        }

        difference = $"Expected integer `{expectedValue}` but was `{actualValue}`.";
        return false;
    }

    private static bool CompareFloat(JsonNode expected, JsonNode actual, out string difference)
    {
        if (!TryParseFloat(expected.GetValue<string>(), out var expectedValue) ||
            !TryParseFloat(actual.GetValue<string>(), out var actualValue))
        {
            difference = "Invalid float representation.";
            return false;
        }

        if (double.IsNaN(expectedValue) && double.IsNaN(actualValue))
        {
            difference = string.Empty;
            return true;
        }

        if (expectedValue.Equals(actualValue))
        {
            difference = string.Empty;
            return true;
        }

        difference = $"Expected float `{expected}` but was `{actual}`.";
        return false;
    }

    private static bool CompareDateTime(JsonNode expected, JsonNode actual, out string difference)
    {
        var expectedText = expected.GetValue<string>();
        var actualText = actual.GetValue<string>();

        if (string.Equals(expectedText, actualText, StringComparison.Ordinal))
        {
            difference = string.Empty;
            return true;
        }

        if (expectedText is not null && actualText is not null)
        {
            var expectedNormalized = NormalizeTomlTestDateTimeText(expectedText);
            var actualNormalized = NormalizeTomlTestDateTimeText(actualText);
            if (string.Equals(expectedNormalized, actualNormalized, StringComparison.Ordinal))
            {
                difference = string.Empty;
                return true;
            }
        }

        difference = $"Expected datetime `{expectedText}` but was `{actualText}`.";
        return false;
    }

    private static string NormalizeTomlTestDateTimeText(string text)
    {
        // toml-test encodes timestamps as RFC3339 strings, often normalizing fractional seconds
        // to millisecond precision (e.g. ".600"). the library may preserve the original precision
        // (e.g. ".6"). Normalize by trimming trailing zeros from the fractional part.
        var dotIndex = text.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            return text;
        }

        var index = dotIndex + 1;
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }

        if (index == dotIndex + 1)
        {
            return text;
        }

        var fracEnd = index;
        while (fracEnd > dotIndex + 1 && text[fracEnd - 1] == '0')
        {
            fracEnd--;
        }

        if (fracEnd == dotIndex + 1)
        {
            // Fraction becomes empty: remove the '.' as well.
            return string.Concat(text.AsSpan(0, dotIndex), text.AsSpan(index));
        }

        if (fracEnd == index)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, fracEnd), text.AsSpan(index));
    }

    private static bool TryParseBool(string? text, out bool value)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseFloat(string? text, out double value)
    {
        if (text is null)
        {
            value = default;
            return false;
        }

        if (string.Equals(text, "nan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "+nan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "-nan", StringComparison.OrdinalIgnoreCase))
        {
            value = double.NaN;
            return true;
        }

        if (string.Equals(text, "inf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "+inf", StringComparison.OrdinalIgnoreCase))
        {
            value = double.PositiveInfinity;
            return true;
        }

        if (string.Equals(text, "-inf", StringComparison.OrdinalIgnoreCase))
        {
            value = double.NegativeInfinity;
            return true;
        }

        // toml-test values are typically JSON numbers-as-strings, sometimes using normalized exponent forms.
        // Double parsing already accepts both "1e06" and "1e+06".
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
