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
        Assert.Equal("$", result[0].Path);
    }

    [Fact]
    public void Evaluate_NameSelector_DotNotation()
    {
        var doc = JsonNode.Parse("""{"a": "hello"}""");
        var path = JsonPath.Parse("$.a");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("hello", result[0].Value!.GetValue<string>());
        Assert.Equal("$['a']", result[0].Path);
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
    public void Evaluate_NameSelector_MissingMember_LaxMode()
    {
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$.b");
        var result = path.Evaluate(doc, JsonPathEvaluationMode.Lax);
        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_NameSelector_MissingMember_StrictMode()
    {
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$.b");
        var exception = Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(doc, JsonPathEvaluationMode.Strict));
        Assert.Contains("$", exception.Message);
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
        Assert.Equal("$[1]", result[0].Path);
    }

    [Fact]
    public void Evaluate_IndexSelector_Negative()
    {
        var doc = JsonNode.Parse("""["a", "b", "c"]""");
        var path = JsonPath.Parse("$[-1]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("c", result[0].Value!.GetValue<string>());
        Assert.Equal("$[2]", result[0].Path);
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
    public void Evaluate_IndexSelector_OutOfRange_StrictMode()
    {
        var doc = JsonNode.Parse("""["a"]""");
        var path = JsonPath.Parse("$[5]");
        Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(doc, JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void Evaluate_NameSelector_RequiresObject_StrictMode()
    {
        var doc = JsonNode.Parse("""["a"]""");
        var path = JsonPath.Parse("$.a");
        Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(doc, JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void EvaluateValue_NameSelector_MissingMember_LaxMode()
    {
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$.b");
        var result = path.EvaluateValue(doc, JsonPathEvaluationMode.Lax);
        Assert.Null(result);
    }

    [Fact]
    public void EvaluateValue_NameSelector_MissingMember_StrictMode()
    {
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$.b");
        Assert.Throws<JsonPathEvaluationException>(() => path.EvaluateValue(doc, JsonPathEvaluationMode.Strict));
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
        Assert.Equal("$", result[0].Path);
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
    public void Evaluate_Path_Object()
    {
        var doc = JsonNode.Parse("""{"a": {"b": 1}}""");
        var path = JsonPath.Parse("$.a.b");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$['a']['b']", result[0].Path);
    }

    [Fact]
    public void Evaluate_Path_Array()
    {
        var doc = JsonNode.Parse("""[0, 1, 2]""");
        var path = JsonPath.Parse("$[1]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$[1]", result[0].Path);
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
    public void Evaluate_Path_SpecialChars()
    {
        var doc = JsonNode.Parse("""{"a'b": 1}""");
        var path = JsonPath.Parse("$[\"a'b\"]");
        var result = path.Evaluate(doc);
        Assert.Single(result);
        Assert.Equal("$['a\\'b']", result[0].Path);
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

    [Fact]
    public void Evaluate_CustomNavigator_ReturnsTypedMatches()
    {
        var document = CreateCustomDocument();
        var path = JsonPath.Parse("$.items[1].name");
        var result = path.Evaluate(document, TestValueNavigator.Instance);

        Assert.Single(result);
        Assert.Equal("$['items'][1]['name']", result[0].Path);
        Assert.Equal("bar", result[0].Value!.StringValue);
    }

    [Fact]
    public void EvaluateValue_CustomNavigator_ReturnsTypedValue()
    {
        var document = CreateCustomDocument();
        var path = JsonPath.Parse("$.items[-1].name");
        var result = path.EvaluateValue(document, TestValueNavigator.Instance);

        Assert.NotNull(result);
        Assert.Equal("foobar", result.StringValue);
    }

    [Fact]
    public void Evaluate_CustomNavigator_Selectors()
    {
        var document = CreateCustomDocument();

        AssertNames(JsonPath.Parse("$.items[*]").Evaluate(document, TestValueNavigator.Instance), "foo", "bar", "foobar");
        AssertNames(JsonPath.Parse("$.items[0:3:2]").Evaluate(document, TestValueNavigator.Instance), "foo", "foobar");

        var descendantResult = JsonPath.Parse("$..title").Evaluate(document, TestValueNavigator.Instance);
        Assert.Single(descendantResult);
        Assert.Equal("$['metadata']['title']", descendantResult[0].Path);
        Assert.Equal("Catalog", descendantResult[0].Value!.StringValue);
    }

    [Fact]
    public void Evaluate_CustomNavigator_NullValue()
    {
        var document = CreateCustomDocument();
        var result = JsonPath.Parse("$.none").Evaluate(document, TestValueNavigator.Instance);

        Assert.Single(result);
        Assert.Null(result[0].Value);
        Assert.Equal("$['none']", result[0].Path);
    }

    [Fact]
    public void Evaluate_CustomNavigator_MissingMember_StrictMode()
    {
        var document = CreateCustomDocument();
        var path = JsonPath.Parse("$.missing");

        Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(document, TestValueNavigator.Instance, JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void Evaluate_CustomNavigator_FiltersAndFunctions()
    {
        var document = CreateCustomDocument();
        var navigator = TestValueNavigator.Instance;

        AssertNames(JsonPath.Parse("$.items[?@.price < $.limit]").Evaluate(document, navigator), "foo", "foobar");
        AssertNames(JsonPath.Parse("$.items[?@.available == true]").Evaluate(document, navigator), "foo", "foobar");
        AssertNames(JsonPath.Parse("$.items[?length(@.tags) > 1]").Evaluate(document, navigator), "foo");
        AssertNames(JsonPath.Parse("$.items[?count(@.*) == 6]").Evaluate(document, navigator), "bar");
        AssertNames(JsonPath.Parse("$.items[?value(@.id) == 2]").Evaluate(document, navigator), "bar");
        AssertNames(JsonPath.Parse("""$.items[?match(@.name, "foo")]""").Evaluate(document, navigator), "foo");
        AssertNames(JsonPath.Parse("""$.items[?search(@.name, "foo")]""").Evaluate(document, navigator), "foo", "foobar");
    }

    private static TestValue CreateCustomDocument()
    {
        return Object(
            Property("limit", Number(10)),
            Property("items", Array(
                Object(
                    Property("id", Number(1)),
                    Property("name", String("foo")),
                    Property("price", Number(8)),
                    Property("available", Boolean(value: true)),
                    Property("tags", Array(String("a"), String("b")))),
                Object(
                    Property("id", Number(2)),
                    Property("name", String("bar")),
                    Property("price", Number(12)),
                    Property("available", Boolean(value: false)),
                    Property("tags", Array(String("c"))),
                    Property("isbn", String("123"))),
                Object(
                    Property("id", Number(3)),
                    Property("name", String("foobar")),
                    Property("price", Number(7)),
                    Property("available", Boolean(value: true)),
                    Property("tags", Array(String("d")))))),
            Property("metadata", Object(Property("title", String("Catalog")))),
            Property("none", null));
    }

    private static void AssertNames(JsonPathResult<TestValue> result, params string[] expectedNames)
    {
        Assert.Equal(expectedNames.Length, result.Count);
        for (var i = 0; i < expectedNames.Length; i++)
        {
            Assert.True(TestValueNavigator.Instance.TryGetPropertyValue(result[i].Value, "name", out var name));
            Assert.Equal(JsonPathNodeKind.String, name!.Kind);
            Assert.Equal(expectedNames[i], name.StringValue);
        }
    }

    private static JsonPathProperty<TestValue> Property(string name, TestValue? value) => new(name, value);

    private static TestValue Object(params JsonPathProperty<TestValue>[] properties) => TestValue.Object(properties);

    private static TestValue Array(params TestValue?[] items) => TestValue.Array(items);

    private static TestValue String(string value) => TestValue.String(value);

    private static TestValue Number(double value) => TestValue.Number(value);

    private static TestValue Boolean(bool value) => TestValue.Boolean(value);

    private sealed class TestValue
    {
        private TestValue(
            JsonPathNodeKind kind,
            IReadOnlyList<JsonPathProperty<TestValue>>? properties = null,
            IReadOnlyList<TestValue?>? items = null,
            string? stringValue = null,
            double numberValue = 0,
            bool booleanValue = false)
        {
            Kind = kind;
            Properties = properties ?? [];
            Items = items ?? [];
            StringValue = stringValue;
            NumberValue = numberValue;
            BooleanValue = booleanValue;
        }

        public JsonPathNodeKind Kind { get; }

        public IReadOnlyList<JsonPathProperty<TestValue>> Properties { get; }

        public IReadOnlyList<TestValue?> Items { get; }

        public string? StringValue { get; }

        public double NumberValue { get; }

        public bool BooleanValue { get; }

        public static TestValue Object(params JsonPathProperty<TestValue>[] properties)
        {
            return new TestValue(JsonPathNodeKind.Object, properties: properties);
        }

        public static TestValue Array(params TestValue?[] items)
        {
            return new TestValue(JsonPathNodeKind.Array, items: items);
        }

        public static TestValue String(string value)
        {
            return new TestValue(JsonPathNodeKind.String, stringValue: value);
        }

        public static TestValue Number(double value)
        {
            return new TestValue(JsonPathNodeKind.Number, numberValue: value);
        }

        public static TestValue Boolean(bool value)
        {
            return new TestValue(JsonPathNodeKind.Boolean, booleanValue: value);
        }
    }

    private sealed class TestValueNavigator : JsonPathNavigator<TestValue>
    {
        public static TestValueNavigator Instance { get; } = new();

        private TestValueNavigator()
        {
        }

        public override JsonPathNodeKind GetKind(TestValue? value)
        {
            return value?.Kind ?? JsonPathNodeKind.Null;
        }

        public override bool TryGetPropertyValue(TestValue? value, string name, out TestValue? result)
        {
            if (value?.Kind is JsonPathNodeKind.Object)
            {
                foreach (var property in value.Properties)
                {
                    if (property.Name == name)
                    {
                        result = property.Value;
                        return true;
                    }
                }
            }

            result = null;
            return false;
        }

        public override IEnumerable<JsonPathProperty<TestValue>> GetProperties(TestValue? value)
        {
            return value?.Kind is JsonPathNodeKind.Object ? value.Properties : [];
        }

        public override int GetArrayLength(TestValue? value)
        {
            return value?.Kind is JsonPathNodeKind.Array ? value.Items.Count : 0;
        }

        public override bool TryGetElement(TestValue? value, int index, out TestValue? result)
        {
            if (value?.Kind is JsonPathNodeKind.Array && index >= 0 && index < value.Items.Count)
            {
                result = value.Items[index];
                return true;
            }

            result = null;
            return false;
        }

        public override bool TryGetString(TestValue? value, out string? result)
        {
            if (value?.Kind is JsonPathNodeKind.String)
            {
                result = value.StringValue;
                return true;
            }

            result = null;
            return false;
        }

        public override bool TryGetNumber(TestValue? value, out double result)
        {
            if (value?.Kind is JsonPathNodeKind.Number)
            {
                result = value.NumberValue;
                return true;
            }

            result = 0;
            return false;
        }

        public override bool TryGetBoolean(TestValue? value, out bool result)
        {
            if (value?.Kind is JsonPathNodeKind.Boolean)
            {
                result = value.BooleanValue;
                return true;
            }

            result = false;
            return false;
        }
    }
}
