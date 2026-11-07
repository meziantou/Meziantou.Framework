using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPath.Benchmarks;

[MemoryDiagnoser]
public class JsonPathSliceBenchmark
{
    private JsonDocument _jsonElementDocument = null!;
    private JsonNode _jsonNode = null!;
    private global::Meziantou.Framework.Json.JsonPath _emptySlice = null!;
    private global::Meziantou.Framework.Json.JsonPath _singleItemSlice = null!;
    private global::Meziantou.Framework.Json.JsonPath _smallSlice = null!;
    private global::Meziantou.Framework.Json.JsonPath _reverseSlice = null!;

    [Params(1_000, 10_000)]
    public int ArrayLength { get; set; }

    [Params("primitive", "compound")]
    public string ElementKind { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var element = ElementKind is "compound" ? """{"value":1}""" : "1";
        var json = "[" + string.Join(",", Enumerable.Repeat(element, ArrayLength)) + "]";

        _jsonElementDocument = JsonDocument.Parse(json);
        _jsonNode = JsonNode.Parse(json)!;
        _emptySlice = global::Meziantou.Framework.Json.JsonPath.Parse("$[1:1]");
        _singleItemSlice = global::Meziantou.Framework.Json.JsonPath.Parse("$[0:1]");
        _smallSlice = global::Meziantou.Framework.Json.JsonPath.Parse("$[0:4]");
        _reverseSlice = global::Meziantou.Framework.Json.JsonPath.Parse("$[::-1]");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _jsonElementDocument.Dispose();
    }

    [Benchmark]
    public int JsonElement_EmptySlice() => _emptySlice.Evaluate(_jsonElementDocument).Count;

    [Benchmark]
    public int JsonElement_SingleItemSlice() => _singleItemSlice.Evaluate(_jsonElementDocument).Count;

    [Benchmark]
    public int JsonElement_SmallSlice() => _smallSlice.Evaluate(_jsonElementDocument).Count;

    [Benchmark]
    public int JsonElement_ReverseSlice() => _reverseSlice.Evaluate(_jsonElementDocument).Count;

    [Benchmark]
    public int JsonNode_EmptySlice() => _emptySlice.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_SingleItemSlice() => _singleItemSlice.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_SmallSlice() => _smallSlice.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_ReverseSlice() => _reverseSlice.Evaluate(_jsonNode).Count;
}
