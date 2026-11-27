using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.JsonPath.Benchmarks;

/// <summary>
/// Evaluates common queries on a catalog of 1,000 books, each an object of 6 members, plus a few other members (about 120 KB).
/// </summary>
[MemoryDiagnoser]
public class QueryBenchmark
{
    private static readonly global::Meziantou.Framework.Json.JsonPath ChildPath = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.bicycle.color");
    private static readonly global::Meziantou.Framework.Json.JsonPath WildcardChild = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.book[*].author");
    private static readonly global::Meziantou.Framework.Json.JsonPath FilterLessThan = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.book[?@.price < 10]");
    private static readonly global::Meziantou.Framework.Json.JsonPath FilterExistence = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.book[?@.isbn]");
    private static readonly global::Meziantou.Framework.Json.JsonPath FilterStringEquals = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.book[?@.category == 'fiction' && @.price > 5]");
    private static readonly global::Meziantou.Framework.Json.JsonPath FilterMatch = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.book[?match(@.author, 'A.*')]");
    private static readonly global::Meziantou.Framework.Json.JsonPath FilterLength = global::Meziantou.Framework.Json.JsonPath.Parse("$.store.book[?length(@.title) > 10]");
    private static readonly global::Meziantou.Framework.Json.JsonPath DescendantName = global::Meziantou.Framework.Json.JsonPath.Parse("$..price");

    private const string Expression = "$.store.book[?@.category == 'fiction' && @.price > 5 || search(@.title, 'abc')]['title', 'author'][0:2]";

    private static readonly string[] InvalidExpressions =
    [
        "$.store.book[?@.price <]",
        "$.store.book[?@.price = 10]",
        "$.store['unterminated",
        "$.store..",
        "$.store.book[?count(@.a, @.b) > 1]",
    ];

    private JsonDocument _jsonDocument = null!;
    private JsonNode _jsonNode = null!;

    [GlobalSetup]
    public void Setup()
    {
        var books = new JsonArray();
        for (var i = 0; i < 1000; i++)
        {
            var book = new JsonObject
            {
                ["category"] = i % 3 is 0 ? "reference" : "fiction",
                ["author"] = (char)('A' + (i % 26)) + "uthor " + i.ToString(CultureInfo.InvariantCulture),
                ["title"] = "Title of the book number " + i.ToString(CultureInfo.InvariantCulture),
                ["price"] = (i % 50) + 0.99,
            };

            if (i % 2 is 0)
            {
                book["isbn"] = "0-553-" + i.ToString("00000", CultureInfo.InvariantCulture) + "-3";
            }

            book["available"] = i % 5 is not 0;
            books.Add(book);
        }

        var root = new JsonObject
        {
            ["store"] = new JsonObject
            {
                ["book"] = books,
                ["bicycle"] = new JsonObject { ["color"] = "red", ["price"] = 399 },
            },
            ["expensive"] = 10,
        };

        var json = root.ToJsonString();
        _jsonDocument = JsonDocument.Parse(json);
        _jsonNode = JsonNode.Parse(json)!;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _jsonDocument.Dispose();
    }

    [Benchmark]
    public global::Meziantou.Framework.Json.JsonPath Parse() => global::Meziantou.Framework.Json.JsonPath.Parse(Expression);

    [Benchmark]
    public bool TryParse_Valid() => global::Meziantou.Framework.Json.JsonPath.TryParse(Expression, out _);

    [Benchmark]
    public int TryParse_Invalid()
    {
        var count = 0;
        foreach (var expression in InvalidExpressions)
        {
            if (!global::Meziantou.Framework.Json.JsonPath.TryParse(expression, out _))
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int JsonNode_ChildPath() => ChildPath.Evaluate(_jsonNode).Count;

    [Benchmark]
    public JsonNode? JsonNode_ChildPathValue() => ChildPath.EvaluateValue(_jsonNode);

    [Benchmark]
    public int JsonNode_WildcardChild() => WildcardChild.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_WildcardChildPaths()
    {
        var length = 0;
        foreach (var match in WildcardChild.Evaluate(_jsonNode))
        {
            length += match.Path.Length;
        }

        return length;
    }

    [Benchmark]
    public int JsonNode_FilterLessThan() => FilterLessThan.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_FilterExistence() => FilterExistence.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_FilterStringEquals() => FilterStringEquals.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_FilterMatch() => FilterMatch.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_FilterLength() => FilterLength.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonNode_DescendantName() => DescendantName.Evaluate(_jsonNode).Count;

    [Benchmark]
    public int JsonElement_ChildPath() => ChildPath.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_WildcardChild() => WildcardChild.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_FilterLessThan() => FilterLessThan.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_FilterExistence() => FilterExistence.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_FilterStringEquals() => FilterStringEquals.Evaluate(_jsonDocument).Count;

    [Benchmark]
    public int JsonElement_DescendantName() => DescendantName.Evaluate(_jsonDocument).Count;
}
