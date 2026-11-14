using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.JsonPath.Benchmarks;

/// <summary>
/// Evaluates descendant segments (<c>$..</c>) on a document shaped like a batch of analytics records: 100 records,
/// objects nested up to 6 levels deep, arrays of objects, and 20 to 40 properties per object (2 MB, 115,000 nodes).
/// The document holds random synthetic values only.
/// </summary>
[MemoryDiagnoser]
public class DescendantSegmentBenchmark
{
    private static readonly global::Meziantou.Framework.Json.JsonPath Name1 = global::Meziantou.Framework.Json.JsonPath.Parse("$..id");
    private static readonly global::Meziantou.Framework.Json.JsonPath Union8 = global::Meziantou.Framework.Json.JsonPath.Parse(
        "$..['id','score','currency','url','latitude','os','segment','deleted']");
    private static readonly global::Meziantou.Framework.Json.JsonPath Union32 = global::Meziantou.Framework.Json.JsonPath.Parse(
        "$..['id','name','status','duration','score','visible','category','version','currency','amount','quantity','path','url','description','size','width'," +
        "'latitude','longitude','region','zip','os','language','medium','content','segment','cohort','experiment','created','deleted','group','note','token']");
    private static readonly global::Meziantou.Framework.Json.JsonPath Wildcard = global::Meziantou.Framework.Json.JsonPath.Parse("$..*");
    private static readonly global::Meziantou.Framework.Json.JsonPath DescendantChild = global::Meziantou.Framework.Json.JsonPath.Parse("$..properties.name");

    private string _json = null!;
    private JsonDocument _jsonDocument = null!;
    private JsonNode _jsonNode = null!;
    private JsonNode _jsonNodeToModify = null!;

    [GlobalSetup]
    public void Setup()
    {
        using (var stream = typeof(DescendantSegmentBenchmark).Assembly.GetManifestResourceStream("analytics-records.json")!)
        using (var reader = new StreamReader(stream))
        {
            _json = reader.ReadToEnd();
        }

        _jsonDocument = JsonDocument.Parse(_json);
        _jsonNode = JsonNode.Parse(_json)!;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _jsonDocument.Dispose();
    }

    [IterationSetup(Target = nameof(JsonNode_RemoveUnion32))]
    public void SetupRemove()
    {
        _jsonNodeToModify = JsonNode.Parse(_json)!;
    }

    [Benchmark]
    public int JsonElement_Name1() => Name1.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_Union8() => Union8.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_Union32() => Union32.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_Wildcard() => Wildcard.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_DescendantChild() => DescendantChild.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonNode_Name1() => Name1.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_Union8() => Union8.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_Union32() => Union32.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_Wildcard() => Wildcard.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_DescendantChild() => DescendantChild.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_RemoveUnion32() => Union32.Remove(_jsonNodeToModify);
}
