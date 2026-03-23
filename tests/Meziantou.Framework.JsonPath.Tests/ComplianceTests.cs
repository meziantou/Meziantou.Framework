#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPathTests;

public sealed class ComplianceTests
{
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
        if (testCase.InvalidSelector)
        {
            // Expect parse failure
            Assert.False(JsonPath.TryParse(testCase.Selector, out _), $"Expected parse failure for: {testCase.Selector}");
            return;
        }

        var path = JsonPath.Parse(testCase.Selector);
        var document = testCase.Document;
        var result = path.Evaluate(document);

        if (testCase.Result is not null)
        {
            // Deterministic result - expect exact match
            AssertResultMatch(result, testCase.Result);
        }
        else if (testCase.Results is not null)
        {
            // Non-deterministic result - expect match against any valid ordering
            var matched = false;
            foreach (var validResult in testCase.Results)
            {
                try
                {
                    AssertResultMatch(result, validResult!.AsArray());
                    matched = true;
                    break;
                }
                catch (Xunit.Sdk.XunitException)
                {
                    // Try next valid result
                }
            }

            Assert.True(matched, $"Result did not match any valid ordering for: {testCase.Selector}\nActual: {FormatResult(result)}");
        }
    }

    private static void AssertResultMatch(JsonPathResult actual, JsonArray expected)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var expectedNode = expected[i];
            var actualNode = actual[i].Value;
            AssertJsonNodesEqual(expectedNode, actualNode, $"Mismatch at index {i}");
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
                Assert.Equal(expectedObj.Count, actualObj.Count);
                foreach (var prop in expectedObj)
                {
                    Assert.True(actualObj.ContainsKey(prop.Key), $"{context}: missing property '{prop.Key}'");
                    AssertJsonNodesEqual(prop.Value, actualObj[prop.Key], $"{context}.{prop.Key}");
                }

                break;

            case JsonValueKind.Array:
                var expectedArr = expected.AsArray();
                var actualArr = actual.AsArray();
                Assert.Equal(expectedArr.Count, actualArr.Count);
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

    private static string FormatResult(JsonPathResult result)
    {
        var array = result.ToJsonArray();
        return array.ToJsonString();
    }

    private static string FormatNode(JsonNode? node)
    {
        if (node is null)
        {
            return "null";
        }

        return node.ToJsonString();
    }


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
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Name), Name);
        info.AddValue(nameof(Selector), Selector);
        info.AddValue(nameof(InvalidSelector), InvalidSelector);
        info.AddValue(nameof(Document), Document?.ToJsonString());
        info.AddValue(nameof(Result), Result?.ToJsonString());
        info.AddValue(nameof(Results), Results?.ToJsonString());
    }
}