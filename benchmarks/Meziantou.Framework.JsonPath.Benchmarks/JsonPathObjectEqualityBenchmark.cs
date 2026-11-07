using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPath.Benchmarks;

[MemoryDiagnoser]
public class JsonPathObjectEqualityBenchmark
{
    private JsonDocument _jsonElementDocument = null!;
    private JsonNode _jsonNode = null!;
    private global::Meziantou.Framework.Json.JsonPath _path = null!;

    [Params(1_000, 4_000, 16_000)]
    public int PropertyCount { get; set; }

    [Params(false, true)]
    public bool ReverseMemberOrder { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var first = CreateObject(PropertyCount, reverse: false);
        var second = CreateObject(PropertyCount, ReverseMemberOrder);
        var json = "[" + first + "," + second + "]";

        _jsonElementDocument = JsonDocument.Parse(json);
        _jsonNode = JsonNode.Parse(json)!;
        _path = global::Meziantou.Framework.Json.JsonPath.Parse("$[?@ == $[0]]");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _jsonElementDocument.Dispose();
    }

    [Benchmark]
    public int JsonElement_ObjectsEqual() => _path.Evaluate(_jsonElementDocument).Count;

    [Benchmark]
    public int JsonNode_ObjectsEqual() => _path.Evaluate(_jsonNode).Count;

    private static string CreateObject(int count, bool reverse)
    {
        var indexes = Enumerable.Range(0, count);
        if (reverse)
        {
            indexes = indexes.Reverse();
        }

        return "{" + string.Join(",", indexes.Select(index => $"\"p{index}\":{index}")) + "}";
    }
}
