using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Language.Toml.Tests;

/// <summary>Runs the toml-test conformance suite: every valid case must parse without diagnostics to the expected data, every invalid one must be reported.</summary>
/// <remarks>
/// The expected data of a valid case is compared with a model built here from the syntax tree, following the TOML
/// rules for tables, dotted keys, and arrays of tables. It is deliberately a second implementation of those rules: the
/// one inside the parser only reports what is wrong, and never builds the data.
/// </remarks>
public sealed class TomlTestSuiteTests
{
    private static readonly Lazy<Dictionary<string, JsonElement>> Corpus = new(LoadCorpus);

    public static TheoryData<string, TomlVersion> Cases()
    {
        var result = new TheoryData<string, TomlVersion>();
        foreach (var (name, testCase) in Corpus.Value)
        {
            foreach (var version in testCase.GetProperty("versions").EnumerateArray())
            {
                result.Add(name, version.GetString() == "1.0.0" ? TomlVersion.V1_0 : TomlVersion.V1_1);
            }
        }

        return result;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TomlTestSuite(string name, TomlVersion version)
    {
        var testCase = Corpus.Value[name];
        if (!TryGetText(testCase, out var text))
        {
            // A file that is not valid UTF-8 is rejected by the decoder, before there is any text to parse.
            Assert.StartsWith("invalid/", name, StringComparison.Ordinal);
            return;
        }

        var tree = TomlSyntaxTree.ParseText(text, new TomlParseOptions { Version = version });
        Assert.Equal(text, tree.GetRoot().ToFullString());

        var diagnostics = tree.GetDiagnostics();
        if (name.StartsWith("invalid/", StringComparison.Ordinal))
        {
            Assert.NotEmpty(diagnostics);
            return;
        }

        Assert.Empty(diagnostics);
        AssertMatches(testCase.GetProperty("expected"), BuildModel(tree.GetRoot()), "$");
    }

    public static TheoryData<string> ValidCasesWithLineFeeds()
    {
        var result = new TheoryData<string>();
        foreach (var (name, testCase) in Corpus.Value)
        {
            if (name.StartsWith("valid/", StringComparison.Ordinal) && TryGetText(testCase, out var text) && !text.Contains('\r', StringComparison.Ordinal))
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>A valid document stays valid, and means the same, with Windows line breaks.</summary>
    [Theory]
    [MemberData(nameof(ValidCasesWithLineFeeds))]
    public void TomlTestSuite_WithCarriageReturnLineFeeds(string name)
    {
        var testCase = Corpus.Value[name];
        Assert.True(TryGetText(testCase, out var text));
        text = text.Replace("\n", "\r\n", StringComparison.Ordinal);

        var tree = TomlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Empty(tree.GetDiagnostics());
        AssertMatches(testCase.GetProperty("expected"), BuildModel(tree.GetRoot()), "$");
    }

    private static Dictionary<string, JsonElement> LoadCorpus()
    {
        using var stream = typeof(TomlTestSuiteTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Language.Toml.Tests.files.toml-test.cases.json")!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!, item => item.Clone(), StringComparer.Ordinal);
    }

    private static bool TryGetText(JsonElement testCase, out string text)
    {
        if (testCase.TryGetProperty("toml", out var toml))
        {
            text = toml.GetString()!;
            return true;
        }

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(Convert.FromBase64String(testCase.GetProperty("tomlBase64").GetString()!));
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }

    private static Dictionary<string, object> BuildModel(TomlDocumentSyntax document)
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        var current = root;
        foreach (var entry in document.Entries)
        {
            switch (entry)
            {
                case TomlTableSyntax { IsArrayOfTables: true } table:
                    var names = table.Key.Names;
                    var parent = GetTable(root, names.Take(names.Count - 1));
                    if (!parent.TryGetValue(names[^1], out var existing))
                    {
                        existing = new List<object>();
                        parent.Add(names[^1], existing);
                    }

                    current = new Dictionary<string, object>(StringComparer.Ordinal);
                    ((List<object>)existing).Add(current);
                    break;

                case TomlTableSyntax table:
                    current = GetTable(root, table.Key.Names);
                    break;

                case TomlPropertySyntax property:
                    Assign(current, property);
                    break;

                default:
                    Assert.Fail($"Unexpected entry '{entry}'.");
                    break;
            }
        }

        return root;
    }

    private static void Assign(Dictionary<string, object> table, TomlPropertySyntax property)
    {
        var names = property.Key.Names;
        GetTable(table, names.Take(names.Count - 1)).Add(names[^1], BuildValue(property.Value));
    }

    private static Dictionary<string, object> GetTable(Dictionary<string, object> root, IEnumerable<string> names)
    {
        var current = root;
        foreach (var name in names)
        {
            if (!current.TryGetValue(name, out var child))
            {
                child = new Dictionary<string, object>(StringComparer.Ordinal);
                current.Add(name, child);
            }

            current = child switch
            {
                List<object> array => (Dictionary<string, object>)array[^1],
                _ => (Dictionary<string, object>)child,
            };
        }

        return current;
    }

    private static object BuildValue(TomlValueSyntax value)
    {
        switch (value)
        {
            case TomlArraySyntax array:
                return array.Elements.Select(BuildValue).ToList();

            case TomlInlineTableSyntax inlineTable:
                var table = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var property in inlineTable.Properties)
                {
                    Assign(table, property);
                }

                return table;

            case TomlStringSyntax text:
                return new Leaf("string", text.Value);
            case TomlIntegerSyntax integer:
                return new Leaf("integer", integer.Value);
            case TomlFloatSyntax number:
                return new Leaf("float", number.Value);
            case TomlBooleanSyntax boolean:
                return new Leaf("bool", boolean.Value);
            case TomlDateTimeSyntax dateTime:
                return new Leaf(dateTime.Kind() switch
                {
                    SyntaxKind.TomlOffsetDateTime => "datetime",
                    SyntaxKind.TomlLocalDateTime => "datetime-local",
                    SyntaxKind.TomlLocalDate => "date-local",
                    _ => "time-local",
                }, dateTime.Value);
            default:
                throw new InvalidOperationException($"Unexpected value '{value}'.");
        }
    }

    private static void AssertMatches(JsonElement expected, object actual, string path)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Array:
                var list = Assert.IsType<List<object>>(actual);
                Assert.HasCount(expected.GetArrayLength(), list, message: path);
                var index = 0;
                foreach (var item in expected.EnumerateArray())
                {
                    AssertMatches(item, list[index], $"{path}[{index}]");
                    index++;
                }

                break;

            case JsonValueKind.Object when IsLeaf(expected):
                var leaf = Assert.IsType<Leaf>(actual);
                var type = expected.GetProperty("type").GetString()!;
                Assert.Equal(type, leaf.Type, message: path);
                AssertLeafValue(type, expected.GetProperty("value").GetString()!, leaf.Value, path);
                break;

            case JsonValueKind.Object:
                var table = Assert.IsType<Dictionary<string, object>>(actual);
                var expectedKeys = expected.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
                var actualKeys = table.Keys.Order(StringComparer.Ordinal).ToArray();
                Assert.True(expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal), $"{path}: expected keys [{string.Join(", ", expectedKeys)}], got [{string.Join(", ", actualKeys)}]");
                foreach (var property in expected.EnumerateObject())
                {
                    AssertMatches(property.Value, table[property.Name], $"{path}.{property.Name}");
                }

                break;

            default:
                Assert.Fail($"{path}: unexpected JSON {expected.ValueKind}");
                break;
        }
    }

    private static bool IsLeaf(JsonElement element)
        => element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            && element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            && element.EnumerateObject().Count() == 2;

    private static void AssertLeafValue(string type, string expected, object actual, string path)
    {
        var equal = type switch
        {
            "string" => string.Equals(expected, (string)actual, StringComparison.Ordinal),
            "integer" => long.Parse(expected, CultureInfo.InvariantCulture) == (long)actual,
            "bool" => bool.Parse(expected) == (bool)actual,
            "float" => ParseFloat(expected) is var number && (double.IsNaN(number) ? double.IsNaN((double)actual) : number.Equals((double)actual)),
            "datetime" => DateTimeOffset.Parse(Truncate(expected), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcTicks == ((DateTimeOffset)actual).UtcTicks,
            "datetime-local" => DateTime.Parse(Truncate(expected), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) == (DateTime)actual,
            "date-local" => DateOnly.Parse(expected, CultureInfo.InvariantCulture) == (DateOnly)actual,
            "time-local" => TimeOnly.Parse(Truncate(expected), CultureInfo.InvariantCulture) == (TimeOnly)actual,
            _ => false,
        };

        Assert.True(equal, $"{path}: expected {type} '{expected}', got '{actual}'");

        // .NET reads at most seven digits of a fraction of a second, which is also what TOML values are truncated to here.
        static string Truncate(string value) => Regex.Replace(value.Replace(' ', 'T'), @"(?<fraction>\.\d{7})\d+", "${fraction}", RegexOptions.None, TimeSpan.FromSeconds(1));

        static double ParseFloat(string value) => value switch
        {
            "inf" or "+inf" => double.PositiveInfinity,
            "-inf" => double.NegativeInfinity,
            "nan" or "+nan" or "-nan" => double.NaN,
            _ => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
        };
    }

    private sealed record Leaf(string Type, object Value);
}
