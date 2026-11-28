using System.Collections;
using System.Text.Json;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Schemas;

namespace Meziantou.Framework.Yaml.Tests;

/// <summary>The cases of https://github.com/yaml/yaml-test-suite, see files/yaml-test-suite/README.md.</summary>
internal static class YamlTestSuite
{
    public sealed record Case(string Id, string Yaml, string Events, bool Error, string? Json, string? Out, string? Emit);

    private static readonly Lazy<Dictionary<string, Case>> Cases = new(LoadCases);

    public static IEnumerable<Case> All => Cases.Value.Values;

    public static Case Get(string id) => Cases.Value[id];

    public static TheoryData<string> Ids(Func<Case, bool> predicate)
    {
        return new TheoryData<string>(All.Where(predicate).Select(testCase => testCase.Id));
    }

    private static Dictionary<string, Case> LoadCases()
    {
        using var stream = typeof(YamlTestSuite).Assembly.GetManifestResourceStream("Meziantou.Framework.Yaml.Tests.files.yaml-test-suite.cases.json")!;
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, Case>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var testCase = new Case(
                item.GetProperty("id").GetString()!,
                item.GetProperty("yaml").GetString()!,
                item.GetProperty("events").GetString()!,
                item.GetProperty("error").GetBoolean(),
                GetOptionalString(item, "json"),
                GetOptionalString(item, "out"),
                GetOptionalString(item, "emit"));
            result.Add(testCase.Id, testCase);
        }

        return result;

        static string? GetOptionalString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
        }
    }

    /// <summary>Formats the events like the test.event files of the suite.</summary>
    public static string ReadEvents(IParser parser)
    {
        var events = new List<string>();
        while (parser.MoveNext())
        {
            var current = parser.Current;
            var properties = current is NodeEvent node
                ? (node.Anchor is null ? "" : " &" + node.Anchor) + (node.Tag is null ? "" : " <" + node.Tag + ">")
                : "";
            events.Add(current switch
            {
                StreamStart => "+STR",
                StreamEnd => "-STR",
                DocumentStart start => start.IsImplicit ? "+DOC" : "+DOC ---",
                DocumentEnd end => end.IsImplicit ? "-DOC" : "-DOC ...",
                MappingStart mapping => "+MAP" + (mapping.Style is YamlStyle.Flow ? " {}" : "") + properties,
                MappingEnd => "-MAP",
                SequenceStart sequence => "+SEQ" + (sequence.Style is YamlStyle.Flow ? " []" : "") + properties,
                SequenceEnd => "-SEQ",
                AnchorAlias alias => "=ALI *" + alias.Value,
                Scalar scalar => "=VAL" + properties + " " + ScalarIndicator(scalar.Style) + EscapeEventValue(scalar.Value),
                _ => throw new InvalidOperationException($"Unexpected event: {current}"),
            });
        }

        return string.Join('\n', events);
    }

    /// <summary>
    /// Formats the events without the presentation details that an emitter may change: the document markers, the
    /// collection styles and the scalar styles. A scalar keeps its tag, resolved with the core schema when the scalar has
    /// none: an untagged plain scalar may be a null, a boolean or a number, while a quoted or block scalar is a string.
    /// Two streams with the same output represent the same data.
    /// </summary>
    public static string ReadRepresentation(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        var events = new List<string>();
        while (parser.MoveNext())
        {
            var current = parser.Current;
            var anchor = current is NodeEvent { Anchor: { } name } ? " &" + name : "";
            events.Add(current switch
            {
                StreamStart => "+STR",
                StreamEnd => "-STR",
                DocumentStart => "+DOC",
                DocumentEnd => "-DOC",
                MappingStart mapping => "+MAP" + anchor + " <" + ResolveCollectionTag(mapping.Tag, "!!map") + ">",
                MappingEnd => "-MAP",
                SequenceStart sequence => "+SEQ" + anchor + " <" + ResolveCollectionTag(sequence.Tag, "!!seq") + ">",
                SequenceEnd => "-SEQ",
                AnchorAlias alias => "=ALI *" + alias.Value,
                Scalar scalar => FormatScalar(scalar, anchor),
                _ => throw new InvalidOperationException($"Unexpected event: {current}"),
            });
        }

        return string.Join('\n', events);

        static string ResolveCollectionTag(string? tag, string defaultTag)
        {
            return tag is null or "!" ? CoreSchema.Instance.ExpandTag(defaultTag) : tag;
        }

        static string FormatScalar(Scalar scalar, string anchor)
        {
            var tag = scalar.Tag switch
            {
                null => CoreSchema.Instance.ExpandTag(CoreSchema.Instance.GetDefaultTag(scalar)),
                "!" => CoreSchema.Instance.ExpandTag("!!str"),
                _ => scalar.Tag,
            };

            // "", "~" and "null" are the same null
            var value = tag == CoreSchema.Instance.ExpandTag("!!null") && scalar.Tag is null ? "" : EscapeEventValue(scalar.Value);
            return "=VAL" + anchor + " <" + tag + "> " + value;
        }
    }

    /// <summary>Asserts that a value read as <see cref="object"/> is the JSON value of in.json.</summary>
    public static void AssertJsonEqual(JsonElement expected, object? actual, string path = "$")
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                Assert.Null(actual, path);
                break;

            case JsonValueKind.True or JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), Assert.IsType<bool>(actual, path), path);
                break;

            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), Assert.IsType<string>(actual, path), path);
                break;

            case JsonValueKind.Number:
                switch (actual)
                {
                    case long value when expected.TryGetInt64(out var expectedInt64):
                        Assert.Equal(expectedInt64, value, path);
                        break;
                    case ulong value when expected.TryGetUInt64(out var expectedUInt64):
                        Assert.Equal(expectedUInt64, value, path);
                        break;
                    case double value:
                        Assert.Equal(expected.GetDouble(), value, path);
                        break;
                    default:
                        Assert.Fail($"{path}: expected the number {expected.GetRawText()}, got {actual ?? "null"} ({actual?.GetType().Name})");
                        break;
                }

                break;

            case JsonValueKind.Array:
                var list = Assert.IsAssignableTo<IList>(actual, path);
                Assert.HasCount(expected.GetArrayLength(), list, path);
                var index = 0;
                foreach (var item in expected.EnumerateArray())
                {
                    AssertJsonEqual(item, list[index], $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]");
                    index++;
                }

                break;

            case JsonValueKind.Object:
                // A mapping is unordered: the keys are matched by their text, which is how JSON writes them
                var dictionary = Assert.IsAssignableTo<IDictionary>(actual, path);
                var entries = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    entries.Add(FormatKey(entry.Key), entry.Value);
                }

                var properties = expected.EnumerateObject().ToList();
                Assert.HasCount(properties.Count, entries, path);
                foreach (var property in properties)
                {
                    Assert.True(entries.TryGetValue(property.Name, out var value), $"{path}: missing key '{property.Name}'");
                    AssertJsonEqual(property.Value, value, path + "." + property.Name);
                }

                break;

            default:
                throw new InvalidOperationException($"Unexpected JSON value: {expected.ValueKind}");
        }

        static string FormatKey(object key)
        {
            return key switch
            {
                string value => value,
                bool value => value ? "true" : "false",
                IFormattable value => value.ToString(format: null, CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException($"Unexpected key type: {key.GetType()}"),
            };
        }
    }

    /// <summary>Reads the values of in.json: one JSON value per YAML document, separated by whitespace.</summary>
    public static List<JsonElement> ReadJsonValues(string json)
    {
        var values = new List<JsonElement>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { AllowMultipleValues = true });
        while (JsonElement.TryParseValue(ref reader, out var value))
        {
            values.Add(value.Value);
            if (!reader.Read())
                break;
        }

        return values;
    }

    internal static char ScalarIndicator(ScalarStyle style) => style switch
    {
        ScalarStyle.Plain => ':',
        ScalarStyle.SingleQuoted => '\'',
        ScalarStyle.DoubleQuoted => '"',
        ScalarStyle.Literal => '|',
        ScalarStyle.Folded => '>',
        _ => throw new InvalidOperationException($"Unexpected scalar style: {style}"),
    };

    internal static string EscapeEventValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\0", "\\0", StringComparison.Ordinal)
            .Replace("\b", "\\b", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}

