using System;
using System.Collections;
using System.Collections.Generic;
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

namespace Meziantou.Framework.Toml.Tests;

/// <summary>
/// Tests against the official TOML specs of TOML https://github.com/BurntSushi/toml-test
/// </summary>
public static class StandardTests
{
    private const string InvalidSpec = "invalid";
    private const string ValidSpec = "valid";

    private static readonly Lazy<Dictionary<string, System.Text.Json.JsonElement>> Corpus = new(LoadCorpus);

    private static readonly string[] Toml11ValidButTomlTestMarksInvalid =
    [
        // TOML v1.1.0 additions (toml-test suite still marks them invalid).
        "/invalid/datetime/no-secs.toml",            // minute-only times
        "/invalid/local-datetime/no-secs.toml",
        "/invalid/local-time/no-secs.toml",
        "/invalid/string/basic-byte-escapes.toml",   // \xHH basic string escape
        "/invalid/inline-table/trailing-comma.toml", // trailing commas in inline tables
        "/invalid/inline-table/linebreak-01.toml",   // TOML 1.1 allows newlines in inline tables
        "/invalid/inline-table/linebreak-02.toml",
        "/invalid/inline-table/linebreak-03.toml",
        "/invalid/inline-table/linebreak-04.toml",
    ];

    private static readonly string[] Toml10SpecFolders =
    [
        "/valid/spec-1.0.0/",
        "/invalid/spec-1.0.0/",
    ];

    [Theory]
    [MemberData(nameof(ListTomlFiles), ValidSpec)]
    public static void SpecValid(string name)
    {
        var testCase = GetCase(name);
        ValidateSpec(ValidSpec, testCase.InputName, testCase.Toml, testCase.Json!);
    }

    [Theory]
    [MemberData(nameof(ListTomlFiles), InvalidSpec)]
    public static void SpecInvalid(string name)
    {
        var testCase = GetCase(name);
        ValidateSpec(InvalidSpec, testCase.InputName, testCase.Toml, testCase.Json!);
    }

    private static void ValidateSpec(string type, string inputName, string toml, string json)
    {
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
                var expectedJson = (JsonObject)NormalizeJson(ParseTomlTestJson(json))!;
                // Convert to the untyped model.
                var model = TomlSerializer.Deserialize<TomlTable>(toml);
                // Convert the model into the expected json
                var computedJson = ModelHelper.ToJson(model);
                AssertTomlTestJsonEquivalent(expectedJson, computedJson, inputName, toml, doc, roundtrip);

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
                Assert.Throws<TomlException>(() => ReadAllEvents(TomlParser.Create(toml)));

                var tolerantParser = TomlParser.Create(toml, new TomlParserOptions { Mode = TomlParserMode.Tolerant });
                ReadAllEvents(tolerantParser);
                Assert.True(tolerantParser.HasErrors, message: "The tolerant parser must report an error");
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

    public static TheoryData<string> ListTomlFiles(string type)
    {
        var tests = new TheoryData<string>();
        foreach (var name in Corpus.Value.Keys)
        {
            if (!name.StartsWith(type + "/", StringComparison.Ordinal))
            {
                continue;
            }

            var normalizedFile = "/" + name + ".toml";

            for (var i = 0; i < Toml10SpecFolders.Length; i++)
            {
                if (normalizedFile.Contains(Toml10SpecFolders[i], StringComparison.OrdinalIgnoreCase))
                {
                    goto next_file;
                }
            }

            if (type == InvalidSpec)
            {
                // The toml-test "invalid/encoding" suite validates raw UTF-8 byte-level correctness.
                // This test harness feeds TOML as text (string/TextReader), so these cases are not applicable here.
                if (normalizedFile.Contains("/invalid/encoding/", StringComparison.OrdinalIgnoreCase))
                {
                    goto next_file;
                }

                for (var i = 0; i < Toml11ValidButTomlTestMarksInvalid.Length; i++)
                {
                    if (normalizedFile.EndsWith(Toml11ValidButTomlTestMarksInvalid[i], StringComparison.OrdinalIgnoreCase))
                    {
                        goto next_file;
                    }
                }
            }

            tests.Add(name);

            next_file: ;
        }
        return tests;
    }

    /// <summary>
    /// Gets a toml-test case from the embedded corpus (see files/toml-test/README.md).
    /// </summary>
    /// <param name="name">The path of the case relative to the toml-test <c>tests</c> folder, without the extension.</param>
    internal static (string InputName, string Toml, string? Json) GetCase(string name)
    {
        Assert.True(Corpus.Value.TryGetValue(name, out var testCase), message: $"The toml-test case `{name}` does not exist");

        var inputName = Path.GetFileName(name) + ".toml";
        string toml;
        if (testCase.TryGetProperty("toml", out var text))
        {
            toml = text.GetString()!;
        }
        else
        {
            // Same decoding as File.ReadAllText: invalid UTF-8 sequences are replaced
            toml = Encoding.UTF8.GetString(Convert.FromBase64String(testCase.GetProperty("tomlBase64").GetString()!));
        }

        var json = testCase.TryGetProperty("expected", out var expected) ? expected.GetRawText() : null;
        return (inputName, toml, json);
    }

    private static Dictionary<string, System.Text.Json.JsonElement> LoadCorpus()
    {
        using var stream = typeof(StandardTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Toml.Tests.files.toml-test.cases.json")!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        var result = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        foreach (var testCase in document.RootElement.EnumerateArray())
        {
            result.Add(testCase.GetProperty("name").GetString()!, testCase.Clone());
        }

        return result;
    }

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
