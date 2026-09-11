using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPathTests;

public sealed class ComplianceTests
{
    /// <summary>
    /// Cases of the vendored suite this implementation deliberately does not satisfy, and why. A case listed here
    /// has to keep failing: the test fails when it starts passing, so the list cannot quietly go stale.
    /// </summary>
    private static readonly FrozenDictionary<string, string> ExpectedFailures = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // RFC 9485 §3 lists "^" and "$" among the NormalChars, and §4 - the normative one - gives an I-Regexp the
        // semantics of an XSD regexp, where neither character is an anchor. The suite follows the mapping sketched
        // in the non-normative §5.3 instead, which hands both straight to a dialect that does read them as anchors.
        ["functions, match, explicit caret"] = "'^ab.*' matches a literal '^', not the start of the string",
        ["functions, match, explicit dollar"] = "'.*bc$' matches a literal '$', not the end of the string",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly Lazy<ComplianceTestSuite> TestSuite = new(LoadTestSuite);

    private static ComplianceTestSuite LoadTestSuite()
    {
        using var stream = typeof(ComplianceTests).Assembly.GetManifestResourceStream("Meziantou.Framework.JsonPathTests.cts.json");
        if (stream is null)
        {
            throw new InvalidOperationException("Could not find embedded resource 'cts.json'");
        }

        return JsonSerializer.Deserialize<ComplianceTestSuite>(stream)!;
    }

    public static TheoryData<ComplianceTestCase> GetComplianceTestCases()
    {
        var suite = TestSuite.Value;
        var theoryData = new TheoryData<ComplianceTestCase>();
        foreach (var test in suite.Tests)
        {
            theoryData.Add(test);
        }
        return theoryData;
    }

    [Theory]
    [MemberData(nameof(GetComplianceTestCases))]
    public void ComplianceTest(ComplianceTestCase testCase)
    {
        if (ExpectedFailures.TryGetValue(testCase.Name, out var reason))
        {
            RunExpectedFailure(testCase, reason);
            return;
        }

        Run(testCase);
    }

    /// <summary>Runs a case that is known not to pass, and fails when it turns out to pass after all.</summary>
    private static void RunExpectedFailure(ComplianceTestCase testCase, string reason)
    {
        try
        {
            Run(testCase);
        }
        catch (Exception)
        {
            return;
        }

        Assert.Fail($"Case '{testCase.Name}' ({reason}) is listed as an expected failure but it passes now. Remove it from {nameof(ExpectedFailures)}.");
    }

    private static void Run(ComplianceTestCase testCase)
    {
        if (testCase.InvalidSelector)
        {
            // Expect parse failure
            Assert.False(JsonPath.TryParse(testCase.Selector, out _), $"Expected parse failure for: {testCase.Selector}");
            return;
        }

        var path = JsonPath.Parse(testCase.Selector);

        // Each built-in navigator runs every case, so neither can drift from the suite unnoticed.
        var nodeMatches = path.Evaluate(testCase.Document).Select(match => new ActualMatch(match.Value, match.Path)).ToArray();
        AssertExpectedResult(testCase, nodeMatches, nameof(JsonNode));

        using var document = JsonDocument.Parse(testCase.Document?.ToJsonString() ?? "null");
        var elementMatches = path.Evaluate(document).Select(match => new ActualMatch(JsonNode.Parse(match.Value.GetRawText()), match.Path)).ToArray();
        AssertExpectedResult(testCase, elementMatches, nameof(JsonElement));
    }

    private static void AssertExpectedResult(ComplianceTestCase testCase, ActualMatch[] actual, string navigator)
    {
        if (testCase.Result is not null)
        {
            // Deterministic result - expect exact match
            AssertResultMatch(actual, testCase.Result, testCase.ResultPaths, navigator);
        }
        else if (testCase.Results is not null)
        {
            // Non-deterministic result - expect match against any valid ordering. The values and the normalized
            // paths of an ordering go together, so both have to come from the same one.
            var matched = false;
            for (var i = 0; i < testCase.Results.Count; i++)
            {
                try
                {
                    AssertResultMatch(actual, testCase.Results[i]!.AsArray(), testCase.ResultsPaths?[i]!.AsArray(), navigator);
                    matched = true;
                    break;
                }
                catch (AssertionException)
                {
                    // Try next valid result
                }
            }

            Assert.True(matched, $"{navigator}: result did not match any valid ordering for: {testCase.Selector}\nActual: {FormatResult(actual)}");
        }
    }

    private static void AssertResultMatch(ActualMatch[] actual, JsonArray expected, JsonArray? expectedPaths, string navigator)
    {
        Assert.HasCount(expected.Count, actual);
        for (var i = 0; i < expected.Count; i++)
        {
            var expectedNode = expected[i];
            var actualNode = actual[i].Value;
            AssertJsonNodesEqual(expectedNode, actualNode, $"{navigator}: mismatch at index {i}");

            if (expectedPaths is not null)
            {
                Assert.Equal(expectedPaths[i]!.GetValue<string>(), actual[i].Path, message: $"{navigator}: normalized path mismatch at index {i}");
            }
        }
    }

    private static void AssertJsonNodesEqual(JsonNode? expected, JsonNode? actual, string context)
    {
        if (expected is null && actual is null)
        {
            return;
        }

        if (expected is null || actual is null)
        {
            Assert.Fail($"{context}: expected {FormatNode(expected)}, got {FormatNode(actual)}");
            return;
        }

        var expectedKind = expected.GetValueKind();
        var actualKind = actual.GetValueKind();

        if (expectedKind != actualKind)
        {
            Assert.Fail($"{context}: expected kind {expectedKind}, got {actualKind}. Expected: {FormatNode(expected)}, Actual: {FormatNode(actual)}");
        }

        switch (expectedKind)
        {
            case JsonValueKind.Object:
                var expectedObj = expected.AsObject();
                var actualObj = actual.AsObject();
                Assert.HasCount(expectedObj.Count, actualObj);
                foreach (var prop in expectedObj)
                {
                    Assert.Contains(prop.Key, actualObj, message: $"{context}: missing property '{prop.Key}'");
                    AssertJsonNodesEqual(prop.Value, actualObj[prop.Key], $"{context}.{prop.Key}");
                }

                break;

            case JsonValueKind.Array:
                var expectedArr = expected.AsArray();
                var actualArr = actual.AsArray();
                Assert.HasCount(expectedArr.Count, actualArr);
                for (var i = 0; i < expectedArr.Count; i++)
                {
                    AssertJsonNodesEqual(expectedArr[i], actualArr[i], $"{context}[{i}]");
                }

                break;

            case JsonValueKind.String:
                Assert.Equal(expected.GetValue<string>(), actual.GetValue<string>());
                break;

            case JsonValueKind.Number:
                var expectedDec = expected.GetValue<JsonElement>().GetDecimal();
                var actualDec = actual.GetValue<JsonElement>().GetDecimal();
                Assert.Equal(expectedDec, actualDec);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break; // kind match is sufficient

            default:
                Assert.Fail($"{context}: unexpected kind {expectedKind}");
                break;
        }
    }

    private static string FormatResult(ActualMatch[] result)
    {
        return string.Join(", ", result.Select(match => $"{match.Path}: {FormatNode(match.Value)}"));
    }

    private static string FormatNode(JsonNode? node)
    {
        if (node is null)
        {
            return "null";
        }

        return node.ToJsonString();
    }

    private readonly record struct ActualMatch(JsonNode? Value, string Path);
}

#pragma warning disable MA0048 // File name must match type name
public sealed class ComplianceTestSuite
{
    [System.Text.Json.Serialization.JsonPropertyName("tests")]
    public IList<ComplianceTestCase> Tests { get; set; } = [];
}

public sealed class ComplianceTestCase : IXunitSerializable
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("selector")]
    public string Selector { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("document")]
    public JsonNode? Document { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("result")]
    public JsonArray? Result { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("results")]
    public JsonArray? Results { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("result_paths")]
    public JsonArray? ResultPaths { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("results_paths")]
    public JsonArray? ResultsPaths { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("invalid_selector")]
    public bool InvalidSelector { get; set; }

    public override string ToString() => Name;

    public void Deserialize(IXunitSerializationInfo info)
    {
        Name = info.GetValue<string>(nameof(Name))!;
        Selector = info.GetValue<string>(nameof(Selector))!;
        InvalidSelector = info.GetValue<bool>(nameof(InvalidSelector));

        var docJson = info.GetValue<string?>(nameof(Document));
        Document = docJson is not null ? JsonNode.Parse(docJson) : null;

        var resultJson = info.GetValue<string?>(nameof(Result));
        Result = resultJson is not null ? JsonNode.Parse(resultJson)?.AsArray() : null;

        var resultsJson = info.GetValue<string?>(nameof(Results));
        Results = resultsJson is not null ? JsonNode.Parse(resultsJson)?.AsArray() : null;

        var resultPathsJson = info.GetValue<string?>(nameof(ResultPaths));
        ResultPaths = resultPathsJson is not null ? JsonNode.Parse(resultPathsJson)?.AsArray() : null;

        var resultsPathsJson = info.GetValue<string?>(nameof(ResultsPaths));
        ResultsPaths = resultsPathsJson is not null ? JsonNode.Parse(resultsPathsJson)?.AsArray() : null;
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Name), Name);
        info.AddValue(nameof(Selector), Selector);
        info.AddValue(nameof(InvalidSelector), InvalidSelector);
        info.AddValue(nameof(Document), Document?.ToJsonString());
        info.AddValue(nameof(Result), Result?.ToJsonString());
        info.AddValue(nameof(Results), Results?.ToJsonString());
        info.AddValue(nameof(ResultPaths), ResultPaths?.ToJsonString());
        info.AddValue(nameof(ResultsPaths), ResultsPaths?.ToJsonString());
    }
}
