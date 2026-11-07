using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPath.Benchmarks;

[MemoryDiagnoser]
public class EvaluationBenchmark
{
    private JsonArray _array = null!;
    private JsonArray _objects = null!;
    private readonly Json.JsonPath _all = Json.JsonPath.Parse("$[*]");
    private readonly Json.JsonPath _wanted = Json.JsonPath.Parse("$[*].wanted");
    private readonly Json.JsonPath _rootCount = Json.JsonPath.Parse("$[?count($[*]) > 0]");
    private readonly Json.JsonPath _relativeCount = Json.JsonPath.Parse("$[?count(@.items[*]) > 0]");

    [Params(1_000, 10_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _array = [];
        _objects = [];
        for (var i = 0; i < Count; i++)
        {
            _array.Add(i);
            _objects.Add(new JsonObject { ["wanted"] = i, ["items"] = new JsonArray(1, 2, 3) });
        }
    }

    [Benchmark(Baseline = true)]
    public JsonNode? MaterializedFirst() => _all.Evaluate(_array)[0].Value;

    [Benchmark]
    public JsonNode? FirstValue() => _all.EvaluateValue(_array);

    [Benchmark]
    public JsonNode? FirstValueMultipleSegments() => _wanted.EvaluateValue(_objects);

    [Benchmark]
    public JsonNode? FirstValueStrict() => _all.EvaluateValue(_array, JsonPathEvaluationMode.Strict);

    [Benchmark]
    public int RootInvariantCount() => _rootCount.Evaluate(_array).Count;

    [Benchmark]
    public int CandidateDependentCount() => _relativeCount.Evaluate(_objects).Count;
}
