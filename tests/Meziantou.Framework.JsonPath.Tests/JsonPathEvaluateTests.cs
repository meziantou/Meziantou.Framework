using System.Text.Json.Nodes;
using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPathTests;

public sealed class JsonPathEvaluateTests
{
    [Fact]
    public void Evaluate_RootOnly()
    {
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_NameSelector_DotNotation()
    {
        var doc = JsonNode.Parse("""{"a": "hello"}""");
        var path = JsonPath.Parse("$.a");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("hello", result[0].Value!.GetValue<string>());
        Assert.Equal("$['a']", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_NameSelector_BracketNotation()
    {
        var doc = JsonNode.Parse("""{"a": {"b": 42}}""");
        var path = JsonPath.Parse("$['a']['b']");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal(42, result[0].Value!.GetValue<int>());
    }

    [Fact]
    public void Evaluate_NameSelector_MissingMember()
    {
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$.b");
        var result = path.Evaluate(doc);
        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_WildcardSelector_Array()
    {
        var doc = JsonNode.Parse("""[1, 2, 3]""");
        var path = JsonPath.Parse("$[*]");
        var result = path.Evaluate(doc);
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Value!.GetValue<int>());
        Assert.Equal(2, result[1].Value!.GetValue<int>());
        Assert.Equal(3, result[2].Value!.GetValue<int>());
    }

    [Fact]
    public void Evaluate_IndexSelector_Positive()
    {
        var doc = JsonNode.Parse("""["a", "b", "c"]""");
        var path = JsonPath.Parse("$[1]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("b", result[0].Value!.GetValue<string>());
        Assert.Equal("$[1]", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_IndexSelector_Negative()
    {
        var doc = JsonNode.Parse("""["a", "b", "c"]""");
        var path = JsonPath.Parse("$[-1]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("c", result[0].Value!.GetValue<string>());
        Assert.Equal("$[2]", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_IndexSelector_OutOfRange()
    {
        var doc = JsonNode.Parse("""["a"]""");
        var path = JsonPath.Parse("$[5]");
        var result = path.Evaluate(doc);
        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_SliceSelector_Default()
    {
        var doc = JsonNode.Parse("""["a", "b", "c", "d", "e"]""");
        var path = JsonPath.Parse("$[1:3]");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
        Assert.Equal("b", result[0].Value!.GetValue<string>());
        Assert.Equal("c", result[1].Value!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_SliceSelector_WithStep()
    {
        var doc = JsonNode.Parse("""["a", "b", "c", "d", "e"]""");
        var path = JsonPath.Parse("$[0:5:2]");
        var result = path.Evaluate(doc);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0].Value!.GetValue<string>());
        Assert.Equal("c", result[1].Value!.GetValue<string>());
        Assert.Equal("e", result[2].Value!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_SliceSelector_Reverse()
    {
        var doc = JsonNode.Parse("""["a", "b", "c"]""");
        var path = JsonPath.Parse("$[::-1]");
        var result = path.Evaluate(doc);
        Assert.Equal(3, result.Count);
        Assert.Equal("c", result[0].Value!.GetValue<string>());
        Assert.Equal("b", result[1].Value!.GetValue<string>());
        Assert.Equal("a", result[2].Value!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_FilterSelector_Existence()
    {
        var doc = JsonNode.Parse("""[{"a": 1}, {"b": 2}, {"a": 3}]""");
        var path = JsonPath.Parse("$[?@.a]");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Evaluate_FilterSelector_Comparison()
    {
        var doc = JsonNode.Parse("""[{"a": 1}, {"a": 5}, {"a": 3}]""");
        var path = JsonPath.Parse("$[?@.a > 2]");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Evaluate_FilterSelector_LogicalAnd()
    {
        var doc = JsonNode.Parse("""[{"a": 1, "b": 2}, {"a": 5}, {"a": 3, "b": 4}]""");
        var path = JsonPath.Parse("$[?@.a > 2 && @.b]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_FilterSelector_LogicalOr()
    {
        var doc = JsonNode.Parse("""[{"a": 1}, {"b": 2}, {"c": 3}]""");
        var path = JsonPath.Parse("$[?@.a || @.b]");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Evaluate_FilterSelector_Not()
    {
        var doc = JsonNode.Parse("""[{"a": 1}, {"b": 2}]""");
        var path = JsonPath.Parse("$[?!@.a]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_DescendantSegment_Name()
    {
        var doc = JsonNode.Parse("""{"a": {"b": {"c": 1}}, "c": 2}""");
        var path = JsonPath.Parse("$..c");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Evaluate_DescendantSegment_Wildcard()
    {
        var doc = JsonNode.Parse("""{"a": [1, 2]}""");
        var path = JsonPath.Parse("$..*");
        var result = path.Evaluate(doc);
        Assert.Equal(3, result.Count); // [1,2], 1, 2
    }

    [Fact]
    public void Evaluate_MultipleSelectors()
    {
        var doc = JsonNode.Parse("""[0, 1, 2, 3, 4]""");
        var path = JsonPath.Parse("$[0, 2, 4]");
        var result = path.Evaluate(doc);
        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].Value!.GetValue<int>());
        Assert.Equal(2, result[1].Value!.GetValue<int>());
        Assert.Equal(4, result[2].Value!.GetValue<int>());
    }

    [Fact]
    public void Evaluate_NullDocument()
    {
        var path = JsonPath.Parse("$");
        var result = path.Evaluate(default(JsonNode));
        Assert.Single(result);
        Assert.Null(result[0].Value);
        Assert.Equal("$", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_NullMemberValue()
    {
        var doc = JsonNode.Parse("""{"a": null}""");
        var path = JsonPath.Parse("$.a");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Null(result[0].Value);
    }

    [Fact]
    public void Evaluate_FilterSelector_NullComparison()
    {
        var doc = JsonNode.Parse("""[{"a": null}, {"a": 1}]""");
        var path = JsonPath.Parse("$[?@.a == null]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_Function_Length()
    {
        var doc = JsonNode.Parse("""["a", "bb", "ccc"]""");
        var path = JsonPath.Parse("$[?length(@) > 1]");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Evaluate_Function_Count()
    {
        var doc = JsonNode.Parse("""[{"a": 1, "b": 2}, {"a": 1}]""");
        var path = JsonPath.Parse("$[?count(@.*) == 2]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_Function_Match()
    {
        var doc = JsonNode.Parse("""[{"a": "foo"}, {"a": "bar"}, {"a": "foobar"}]""");
        var path = JsonPath.Parse("""$[?match(@.a, "foo")]""");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("foo", result[0].Value!.AsObject()["a"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_Function_Search()
    {
        var doc = JsonNode.Parse("""[{"a": "foo"}, {"a": "bar"}, {"a": "foobar"}]""");
        var path = JsonPath.Parse("""$[?search(@.a, "foo")]""");
        var result = path.Evaluate(doc);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Evaluate_Function_Value()
    {
        var doc = JsonNode.Parse("""[{"a": {"b": 1}}, {"a": {"b": 2}}]""");
        var path = JsonPath.Parse("$[?value(@.a.b) == 2]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_NormalizedPath_Object()
    {
        var doc = JsonNode.Parse("""{"a": {"b": 1}}""");
        var path = JsonPath.Parse("$.a.b");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$['a']['b']", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_NormalizedPath_Array()
    {
        var doc = JsonNode.Parse("""[0, 1, 2]""");
        var path = JsonPath.Parse("$[1]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$[1]", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_ComparisonTypeMismatch()
    {
        var doc = JsonNode.Parse("""[{"a": "1"}, {"a": 1}]""");
        var path = JsonPath.Parse("$[?@.a == 1]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal(1, result[0].Value!.AsObject()["a"]!.GetValue<int>());
    }

    [Fact]
    public void Evaluate_AbsoluteQueryInFilter()
    {
        var doc = JsonNode.Parse("""{"threshold": 10, "items": [{"value": 5}, {"value": 15}]}""");
        var path = JsonPath.Parse("$.items[?@.value > $.threshold]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal(15, result[0].Value!.AsObject()["value"]!.GetValue<int>());
    }

    [Fact]
    public void Evaluate_BookstoreExample()
    {
        var doc = JsonNode.Parse("""
        {
            "store": {
                "book": [
                    {"category": "reference", "author": "Nigel Rees", "title": "Sayings of the Century", "price": 8.95},
                    {"category": "fiction", "author": "Evelyn Waugh", "title": "Sword of Honour", "price": 12.99},
                    {"category": "fiction", "author": "Herman Melville", "title": "Moby Dick", "isbn": "0-553-21311-3", "price": 8.99},
                    {"category": "fiction", "author": "J. R. R. Tolkien", "title": "The Lord of the Rings", "isbn": "0-395-19395-8", "price": 22.99}
                ],
                "bicycle": {"color": "red", "price": 399}
            }
        }
        """);

        // All authors
        var result = JsonPath.Parse("$.store.book[*].author").Evaluate(doc);
        Assert.Equal(4, result.Count);

        // Books cheaper than 10
        result = JsonPath.Parse("$.store.book[?@.price < 10]").Evaluate(doc);
        Assert.Equal(2, result.Count);

        // Books with ISBN
        result = JsonPath.Parse("$.store.book[?@.isbn]").Evaluate(doc);
        Assert.Equal(2, result.Count);

        // Third book
        result = JsonPath.Parse("$..book[2]").Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("Moby Dick", result[0].Value!.AsObject()["title"]!.GetValue<string>());

        // Last book
        result = JsonPath.Parse("$..book[-1]").Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("The Lord of the Rings", result[0].Value!.AsObject()["title"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_SliceSelector_StepZero()
    {
        var doc = JsonNode.Parse("""[1, 2, 3]""");
        var path = JsonPath.Parse("$[0:3:0]");
        var result = path.Evaluate(doc);
        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_NormalizedPath_SpecialChars()
    {
        var doc = JsonNode.Parse("""{"a'b": 1}""");
        var path = JsonPath.Parse("$[\"a'b\"]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$['a\\'b']", result[0].NormalizedPath);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Byte()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create((byte)42) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_SByte()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create((sbyte)42) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Short()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create((short)42) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_UShort()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create((ushort)42) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Int()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_UInt()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42u) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Long()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42L) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_ULong()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42UL) });
        var result = JsonPath.Parse("$[?@.a > 41]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Float()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42.5f) });
        var result = JsonPath.Parse("$[?@.a > 42]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Double()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42.5d) });
        var result = JsonPath.Parse("$[?@.a > 42]").Evaluate(json);
        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_CompareNumericTypes_Decimal()
    {
        var json = new JsonArray(new JsonObject { ["a"] = JsonValue.Create(42.5m) });
        var result = JsonPath.Parse("$[?@.a > 42]").Evaluate(json);
        Assert.Single(result);
    }
}
