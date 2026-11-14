using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.JsonPath.Benchmarks;

/// <summary>
/// Evaluates descendant segments (<c>$..</c>) on a document shaped like a batch of analytics records: 100 records,
/// objects nested up to 6 levels deep, arrays of objects, and 20 to 40 properties per object (about 1.5 MB and 100,000 nodes).
/// The document is generated from a fixed seed, so every run evaluates the same values.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
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

    private static readonly string[] ScalarNames =
    [
        "referrer", "width", "height", "segment", "amount", "source", "content", "title", "rank", "browser", "price", "tag", "label", "term", "owner",
        "duration", "created", "enabled", "country", "session", "medium", "locale", "color", "channel", "flag", "region", "updated", "category", "cohort",
        "quantity", "currency", "language", "zip", "name", "score", "platform", "timestamp", "campaign", "size", "deleted", "status", "weight", "note", "os",
        "type", "version", "path", "count", "url", "latitude", "city", "experiment", "variant", "description", "longitude", "visible", "hash", "token", "group",
    ];

    private static readonly string[] ContainerNames = ["items", "properties", "children", "location", "metadata", "device", "context", "attributes"];

    private string _json = null!;
    private JsonDocument _jsonDocument = null!;
    private JsonNode _jsonNode = null!;
    private JsonNode _jsonNodeToModify = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        var records = new JsonArray();
        for (var i = 0; i < 100; i++)
        {
            records.Add(CreateObject(random, level: 1));
        }

        _json = records.ToJsonString();
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

    private static JsonObject CreateObject(Random random, int level)
    {
        var result = new JsonObject { ["id"] = random.Next(1_000_000) };
        var propertyCount = random.Next(20, 40);
        var names = ScalarNames.ToArray();
        random.Shuffle(names);
        foreach (var name in names.AsSpan(0, propertyCount))
        {
            result[name] = CreateScalar(random);
        }

        if (level < 6)
        {
            var containerCount = random.Next(0, 3);
            var containerNames = ContainerNames.ToArray();
            random.Shuffle(containerNames);
            foreach (var name in containerNames.AsSpan(0, containerCount))
            {
                if (random.Next(3) is 0)
                {
                    result[name] = CreateObject(random, level + 1);
                }
                else
                {
                    var array = new JsonArray();
                    var length = random.Next(1, 4);
                    for (var i = 0; i < length; i++)
                    {
                        array.Add(CreateObject(random, level + 1));
                    }

                    result[name] = array;
                }
            }
        }

        return result;
    }

    private static JsonNode? CreateScalar(Random random)
    {
        return random.Next(5) switch
        {
            0 => null,
            1 => random.Next(2) is 0,
            2 => Math.Round(random.NextDouble() * 10_000, 2, MidpointRounding.AwayFromZero),
            _ => new string(Enumerable.Range(0, random.Next(4, 24)).Select(_ => (char)('a' + random.Next(26))).ToArray()),
        };
    }
}
