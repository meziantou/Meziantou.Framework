using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Json;
using Meziantou.Framework.Json.Internals;

namespace Meziantou.Framework.JsonPathTests;

public sealed class JsonPathEvaluateTests
{
    [Fact]
    public void ReadmeExample_Usage()
    {
        #region JsonPath_Usage
        var document = JsonNode.Parse("""{"store":{"book":[{"title":"A"},{"title":"B"}]}}""");

        // Parse a JSONPath expression (can be reused)
        var path = JsonPath.Parse("$.store.book[*].title");

        // Evaluate against a document
        var result = path.Evaluate(document);
        var matches = new List<string>();
        foreach (var match in result)
        {
            matches.Add($"{match.Path}: {match.Value}");
        }

        Assert.Equal(["$['store']['book'][0]['title']: A", "$['store']['book'][1]['title']: B"], matches);
        #endregion
    }

    [Fact]
    public void ReadmeExample_EvaluationModes()
    {
        #region JsonPath_EvaluationModes
        var doc = JsonNode.Parse("""{"a": 1}""");
        var path = JsonPath.Parse("$.name");

        var laxValue = path.EvaluateValue(doc, JsonPathEvaluationMode.Lax);
        Assert.Null(laxValue);

        Assert.Throws<JsonPathEvaluationException>(() => path.EvaluateValue(doc, JsonPathEvaluationMode.Strict));
        #endregion
    }

    [Fact]
    public void ReadmeExample_CustomNavigator()
    {
        #region JsonPath_CustomNavigator
        var myRoot = MyNode.Object(new()
        {
            ["items"] = MyNode.Array(
                MyNode.Object(new() { ["name"] = MyNode.String("a"), ["enabled"] = MyNode.Boolean(true) }),
                MyNode.Object(new() { ["name"] = MyNode.String("b"), ["enabled"] = MyNode.Boolean(false) })),
        });

        var path = JsonPath.Parse("$.items[?@.enabled == true]");
        var result = path.Evaluate(root: myRoot, navigator: MyNodeNavigator.Instance);

        var matches = new List<string>();
        foreach (var match in result)
        {
            MyNode? node = match.Value;
            matches.Add($"{match.Path}: {node?.Properties["name"]?.Value}");
        }

        Assert.Equal(["$['items'][0]: a"], matches);
        #endregion
    }

    [Fact]
    public void DocumentationExample_ParseAndTryParse()
    {
        #region JsonPath_ParseAndTryParse
        // Parse throws a FormatException when the query is not valid
        var path = JsonPath.Parse("$.store.book[?@.price < 10].title");
        Assert.Equal("$.store.book[?@.price < 10].title", path.ToString());
        Assert.Throws<FormatException>(() => JsonPath.Parse("store.book"));

        // TryParse returns false instead, for queries that come from users or configuration files
        Assert.True(JsonPath.TryParse("$.store.book[0]", out var first));
        Assert.NotNull(first);
        Assert.False(JsonPath.TryParse("$.store.book[", out _));
        #endregion
    }

    [Fact]
    public void DocumentationExample_EvaluateJsonDocument()
    {
        #region JsonPath_EvaluateJsonDocument
        using var document = JsonDocument.Parse("""{"store":{"book":[{"title":"A","price":8},{"title":"B","price":12}]}}""");
        var path = JsonPath.Parse("$.store.book[?@.price < 10].title");

        // Evaluate works on a JsonDocument and on any JsonElement, as an instance or an extension method
        JsonPathResult<JsonElement> result = path.Evaluate(document);
        Assert.Single(result);
        Assert.Equal("A", result[0].Value.GetString());
        Assert.Equal("$['store']['book'][0]['title']", result[0].Path);

        JsonElement? title = document.RootElement.EvaluateValue(path);
        Assert.Equal("A", title?.GetString());
        #endregion
    }

    [Fact]
    public void DocumentationExample_EvaluateValue()
    {
        #region JsonPath_EvaluateValue
        var document = JsonNode.Parse("""{"users":[{"name":"Alice"},{"name":"Bob"}]}""");

        // EvaluateValue returns the first match, or null when nothing matches
        JsonNode? firstName = document.EvaluateValue(JsonPath.Parse("$.users[*].name"));
        Assert.Equal("Alice", firstName?.GetValue<string>());

        JsonNode? missing = document.EvaluateValue(JsonPath.Parse("$.groups[0]"));
        Assert.Null(missing);
        #endregion
    }

    [Fact]
    public void DocumentationExample_MissingVersusNull()
    {
        #region JsonPath_MissingVersusNull
        var path = JsonPath.Parse("$.manager");

        // With JsonNode, JSON null is a null value: count the matches to tell it apart from a missing member
        var node = JsonNode.Parse("""{"manager":null}""");
        Assert.Null(node.EvaluateValue(path));
        Assert.Single(node.Evaluate(path));
        Assert.Empty(JsonNode.Parse("{}").Evaluate(path));

        // With JsonElement, JSON null is an element of kind Null, and a missing member is null
        using var document = JsonDocument.Parse("""{"manager":null}""");
        Assert.Equal(JsonValueKind.Null, document.EvaluateValue(path)?.ValueKind);

        using var emptyDocument = JsonDocument.Parse("{}");
        Assert.Null(emptyDocument.EvaluateValue(path));
        #endregion
    }

    [Fact]
    public void DocumentationExample_ToJsonArray()
    {
        #region JsonPath_ToJsonArray
        var document = JsonNode.Parse("""{"items":[{"id":1,"tags":["a"]},{"id":2,"tags":["b","c"]}]}""");

        // ToJsonArray copies the matched values into a new array
        JsonArray tags = JsonPath.Parse("$.items[*].tags[*]").Evaluate(document).ToJsonArray();
        Assert.Equal("""["a","b","c"]""", tags.ToJsonString());
        #endregion
    }

    [Fact]
    public void DocumentationExample_ModifyMatches()
    {
        #region JsonPath_ModifyMatches
        var document = JsonNode.Parse("""{"items":[{"price":8},{"price":12}]}""");

        // With JsonNode, the matched values are the nodes of the document, not copies
        foreach (var match in JsonPath.Parse("$.items[?@.price > 10]").Evaluate(document))
        {
            match.Value!["discounted"] = true;
        }

        Assert.Equal("""{"items":[{"price":8},{"price":12,"discounted":true}]}""", document!.ToJsonString());
        #endregion
    }

    [Fact]
    public void DocumentationExample_NormalizedPaths()
    {
        #region JsonPath_NormalizedPaths
        var document = JsonNode.Parse("""{"teams":{"red team":{"members":["Alice","Bob"]}}}""");

        // Path is the normalized path of the match: names in brackets and quotes, non-negative indexes
        var match = JsonPath.Parse("$.teams.*.members[-1]").Evaluate(document)[0];
        Assert.Equal("$['teams']['red team']['members'][1]", match.Path);

        // A normalized path is itself a query that selects the same node
        Assert.Equal("Bob", JsonPath.Parse(match.Path).EvaluateValue(document)?.GetValue<string>());
        #endregion
    }

    [Fact]
    public void DocumentationExample_NameSelectors()
    {
        #region JsonPath_NameSelectors
        var document = JsonNode.Parse("""{"name":"Alice","first name":"Al","address":{"city":"Paris"}}""");
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        Assert.Equal("""["Alice"]""", Select("$.name"));
        Assert.Equal("""["Paris"]""", Select("$.address.city"));

        // Bracket notation accepts any member name, in single or double quotes
        Assert.Equal("""["Al"]""", Select("$['first name']"));
        Assert.Equal("""["Paris"]""", Select("""$["address"]["city"]"""));

        // A missing member selects nothing
        Assert.Equal("[]", Select("$.phone"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_WildcardSelector()
    {
        #region JsonPath_WildcardSelector
        var document = JsonNode.Parse("""{"sizes":{"small":1,"large":3},"tags":["a","b"],"count":2}""");
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // The values of the members of an object, or the elements of an array
        Assert.Equal("[1,3]", Select("$.sizes.*"));
        Assert.Equal("""["a","b"]""", Select("$.tags[*]"));

        // A number, a string, a Boolean or null has no children
        Assert.Equal("[]", Select("$.count.*"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_IndexSelectors()
    {
        #region JsonPath_IndexSelectors
        var document = JsonNode.Parse("""["a","b","c","d"]""");
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        Assert.Equal("""["a"]""", Select("$[0]"));

        // A negative index counts from the end of the array
        Assert.Equal("""["d"]""", Select("$[-1]"));

        // An index outside the array selects nothing
        Assert.Equal("[]", Select("$[10]"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_SliceSelectors()
    {
        #region JsonPath_SliceSelectors
        var document = JsonNode.Parse("""[0,1,2,3,4,5]""");
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // [start:end:step]; start is included, end is excluded
        Assert.Equal("[1,2]", Select("$[1:3]"));
        Assert.Equal("[0,1]", Select("$[:2]"));
        Assert.Equal("[4,5]", Select("$[-2:]"));
        Assert.Equal("[0,2,4]", Select("$[::2]"));

        // A negative step walks the array backward
        Assert.Equal("[5,4,3,2,1,0]", Select("$[::-1]"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_MultipleSelectors()
    {
        #region JsonPath_MultipleSelectors
        var document = JsonNode.Parse("""{"id":7,"name":"Alice","tags":["a","b","c","d"]}""");
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // The results follow the order of the selectors
        Assert.Equal("""["Alice",7]""", Select("$['name','id']"));
        Assert.Equal("""["d","a","b"]""", Select("$.tags[-1, 0:2]"));

        // A node selected twice is returned twice
        Assert.Equal("""["a","a"]""", Select("$.tags[0, 0]"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_DescendantSegment()
    {
        #region JsonPath_DescendantSegment
        var document = JsonNode.Parse("""
            {
              "price": 1,
              "books": [{ "price": 8 }, { "price": 12, "parts": [{ "price": 5 }] }]
            }
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // The member, at any depth
        Assert.Equal("[1,8,12,5]", Select("$..price"));

        // Any selector works after .., here the first element of every array
        Assert.Equal("""[{"price":8},{"price":5}]""", Select("$..[0]"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FilterComparisons()
    {
        #region JsonPath_FilterComparisons
        var document = JsonNode.Parse("""
            {
              "books": [
                { "title": "A", "price": 8, "category": "fiction" },
                { "title": "B", "price": 12, "category": "science" },
                { "title": "C", "price": 25, "category": "fiction" }
              ]
            }
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // @ is the node being tested: here, each book
        Assert.Equal("""["A","B"]""", Select("$.books[?@.price < 20].title"));
        Assert.Equal("""["A","C"]""", Select("$.books[?@.category == 'fiction'].title"));

        // && (and), || (or), ! (not) and parentheses combine the tests
        Assert.Equal("""["A"]""", Select("$.books[?@.category == 'fiction' && @.price < 20].title"));
        Assert.Equal("""["B"]""", Select("$.books[?!(@.category == 'fiction')].title"));

        // Parentheses around the whole test are allowed, as in other JSONPath dialects
        Assert.Equal("""["A","B"]""", Select("$.books[?(@.price < 20)].title"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FilterExistence()
    {
        #region JsonPath_FilterExistence
        var document = JsonNode.Parse("""
            [
              { "name": "A", "enabled": true },
              { "name": "B", "enabled": false },
              { "name": "C" }
            ]
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // A query alone tests whether the member exists, whatever its value
        Assert.Equal("""["A","B"]""", Select("$[?@.enabled].name"));
        Assert.Equal("""["C"]""", Select("$[?!@.enabled].name"));

        // Compare with true to test the value
        Assert.Equal("""["A"]""", Select("$[?@.enabled == true].name"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FilterRootAndNested()
    {
        #region JsonPath_FilterRootAndNested
        var document = JsonNode.Parse("""
            {
              "maxPrice": 10,
              "orders": [
                { "id": 1, "lines": [{ "price": 4 }, { "price": 15 }] },
                { "id": 2, "lines": [{ "price": 9 }] }
              ]
            }
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // $ refers to the root of the document from inside a filter
        Assert.Equal("[4,9]", Select("$.orders[*].lines[?@.price <= $.maxPrice].price"));

        // A filter inside a filter: the orders that have at least one line above 10
        Assert.Equal("[1]", Select("$.orders[?@.lines[?@.price > 10]].id"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FilterObjectMembers()
    {
        #region JsonPath_FilterObjectMembers
        var document = JsonNode.Parse("""{"users":{"alice":{"age":31},"bob":{"age":25}}}""");

        // On an object, a filter tests the value of every member
        var result = JsonPath.Parse("$.users[?@.age > 30]").Evaluate(document);
        Assert.Equal(["$['users']['alice']"], result.Select(match => match.Path));
        #endregion
    }

    [Fact]
    public void DocumentationExample_ComparisonRules()
    {
        #region JsonPath_ComparisonRules
        var document = JsonNode.Parse("""
            [
              { "id": 1, "value": 1 },
              { "id": 2, "value": 1.0 },
              { "id": 3, "value": "1" },
              { "id": 4, "value": [1, 2] },
              { "id": 5 }
            ]
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // Numbers compare by value; values of different types are never equal
        Assert.Equal("[1,2]", Select("$[?@.value == 1].id"));

        // Arrays and objects compare deeply: here, with the value of the fourth element
        Assert.Equal("[4]", Select("$[?@.value == $[3].value].id"));

        // A missing member is only equal to another missing member, and is not less or greater than anything
        Assert.Equal("[5]", Select("$[?@.value == @.missing].id"));
        Assert.Equal("[]", Select("$[?@.value < @.missing || @.value > @.missing].id"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FunctionLengthAndCount()
    {
        #region JsonPath_FunctionLengthAndCount
        var document = JsonNode.Parse("""
            [
              { "name": "Al", "tags": ["a", "b", "c"], "address": { "city": "Paris", "zip": "75001" } },
              { "name": "Alice", "tags": ["a"], "address": { "city": "Lyon" } }
            ]
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // length(): the number of characters of a string, of elements of an array, of members of an object
        Assert.Equal("""["Alice"]""", Select("$[?length(@.name) > 3].name"));
        Assert.Equal("""["Al"]""", Select("$[?length(@.tags) >= 2].name"));
        Assert.Equal("""["Al"]""", Select("$[?length(@.address) == 2].name"));

        // count(): the number of nodes a query selects
        Assert.Equal("""["Al"]""", Select("$[?count(@.tags[?@ == 'a' || @ == 'b']) == 2].name"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FunctionMatchAndSearch()
    {
        #region JsonPath_FunctionMatchAndSearch
        var document = JsonNode.Parse("""[{ "code": "AB-12" }, { "code": "AB-123" }, { "code": "XAB-1" }]""");
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // match(): the whole string matches the pattern
        Assert.Equal("""["AB-12"]""", Select("$[?match(@.code, 'AB-[0-9]{2}')].code"));

        // search(): the pattern matches a part of the string
        Assert.Equal("""["AB-12","AB-123","XAB-1"]""", Select("$[?search(@.code, 'AB-[0-9]')].code"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_FunctionValue()
    {
        #region JsonPath_FunctionValue
        var document = JsonNode.Parse("""
            [
              { "name": "A", "labels": [{ "key": "env", "value": "prod" }] },
              { "name": "B", "labels": [{ "key": "env", "value": "test" }, { "key": "team", "value": "web" }] }
            ]
            """);
        string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

        // value() turns a query that selects one node into a value that can be compared
        Assert.Equal("""["A"]""", Select("$[?value(@.labels[?@.key == 'env'].value) == 'prod'].name"));

        // When the query selects no node or several nodes, value() has no value: B has two labels
        Assert.Equal("""["A"]""", Select("$[?value(@.labels[*].key) == 'env'].name"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_InvalidQueries()
    {
        #region JsonPath_InvalidQueries
        // Well-formed and valid queries only: these look plausible but are rejected
        Assert.False(JsonPath.TryParse("$.store.book[(@.length-1)]", out _)); // script expressions are not JSONPath
        Assert.False(JsonPath.TryParse("$[?@.price = 10]", out _));            // the equality operator is ==
        Assert.False(JsonPath.TryParse("$[?!@.a == 1]", out _));               // write !(@.a == 1)
        Assert.False(JsonPath.TryParse("$[?length(@.*) > 1]", out _));         // length() takes a single value
        Assert.False(JsonPath.TryParse("$[?count(@.*)]", out _));              // count() returns a number, not a test
        Assert.False(JsonPath.TryParse("$[?upper(@.name) == 'A']", out _));    // unknown function
        Assert.False(JsonPath.TryParse("$[?@.name =~ /^A/]", out _));          // use match() or search()
        #endregion
    }

    [Fact]
    public void DocumentationExample_LengthIsAMemberName()
    {
        #region JsonPath_LengthIsAMemberName
        var document = JsonNode.Parse("""{"books":[{"title":"A"},{"title":"B"}]}""");

        // .length selects a member named "length", not the length of the array
        Assert.Empty(JsonPath.Parse("$.books.length").Evaluate(document));

        // Count the matches instead
        Assert.HasCount(2, JsonPath.Parse("$.books[*]").Evaluate(document));
        #endregion
    }

    [Fact]
    public void DocumentationExample_StrictModeScope()
    {
        #region JsonPath_StrictModeScope
        var document = JsonNode.Parse("""{"items":[{"id":1},{"id":2,"tag":"new"}]}""");

        // Filters and descendant segments test nodes: a missing member is not an error, even in strict mode
        var tagged = JsonPath.Parse("$.items[?@.tag == 'new'].id").Evaluate(document, JsonPathEvaluationMode.Strict);
        Assert.Single(tagged);
        var tags = JsonPath.Parse("$..tag").Evaluate(document, JsonPathEvaluationMode.Strict);
        Assert.Single(tags);

        // A name, index, wildcard, slice or filter selector that does not apply to the node throws
        var exception = Assert.Throws<JsonPathEvaluationException>(
            () => JsonPath.Parse("$.items[*].tag").Evaluate(document, JsonPathEvaluationMode.Strict));
        Assert.Equal("Path error at $['items'][0]: Object member 'tag' does not exist", exception.Message);
        #endregion
    }

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
    public void Evaluate_JsonNode_CanEvaluateFromRoot()
    {
        var doc = JsonNode.Parse("""{"a": {"b": "hello"}}""");
        var path = JsonPath.Parse("$.a.b");

        var result = doc.Evaluate(path);
        var value = doc.EvaluateValue(path);

        Assert.Single(result);
        Assert.Equal("$['a']['b']", result[0].Path);
        Assert.Equal("hello", result[0].Value!.GetValue<string>());
        Assert.Equal("hello", value!.GetValue<string>());
        Assert.Throws<JsonPathEvaluationException>(() => doc.Evaluate(JsonPath.Parse("$.missing"), JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void Evaluate_JsonDocument_CanEvaluateFromRoot()
    {
        using var doc = JsonDocument.Parse("""{"a": ["x", "y"]}""");
        var path = JsonPath.Parse("$.a[1]");

        var result = doc.Evaluate(path);
        var value = doc.EvaluateValue(path);

        Assert.Single(result);
        Assert.Equal("$['a'][1]", result[0].Path);
        Assert.Equal("y", result[0].Value.GetString());
        Assert.Equal("y", value?.GetString());
    }

    [Fact]
    public void Evaluate_JsonElement_CanEvaluateFromPathAndRoot()
    {
        using var doc = JsonDocument.Parse("""{"a": [{"b": "x"}, {"b": "y"}]}""");
        var root = doc.RootElement;
        var path = JsonPath.Parse("$.a[1].b");

        var pathResult = path.Evaluate(root);
        var rootResult = root.Evaluate(path);
        var pathValue = path.EvaluateValue(root);
        var rootValue = root.EvaluateValue(path);

        Assert.Single(pathResult);
        Assert.Single(rootResult);
        Assert.Equal("$['a'][1]['b']", pathResult[0].Path);
        Assert.Equal("$['a'][1]['b']", rootResult[0].Path);
        Assert.Equal("y", pathResult[0].Value.GetString());
        Assert.Equal("y", rootResult[0].Value.GetString());
        Assert.Equal("y", pathValue?.GetString());
        Assert.Equal("y", rootValue?.GetString());
        Assert.Throws<JsonPathEvaluationException>(() => root.Evaluate(JsonPath.Parse("$.missing"), JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void Evaluate_JsonElement_NullValueReturnsJsonNullElement()
    {
        using var doc = JsonDocument.Parse("""{"a": null}""");
        var path = JsonPath.Parse("$.a");

        var value = doc.RootElement.EvaluateValue(path);

        Assert.NotNull(value);
        Assert.Equal(JsonValueKind.Null, value.Value.ValueKind);
    }

    [Fact]
    public void Evaluate_JsonElement_SelectorsFiltersAndFunctions()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                { "name": "foo", "price": 8, "tags": ["a", "b"] },
                { "name": "bar", "price": 12, "tags": ["c"] },
                { "name": "foobar", "price": 7, "tags": ["d"] }
              ],
              "metadata": { "title": "Catalog" }
            }
            """);

        var root = doc.RootElement;

        var wildcardResult = JsonPath.Parse("$.items[*].name").Evaluate(root);
        Assert.Equal(3, wildcardResult.Count);
        Assert.Equal("foo", wildcardResult[0].Value.GetString());
        Assert.Equal("bar", wildcardResult[1].Value.GetString());
        Assert.Equal("foobar", wildcardResult[2].Value.GetString());

        var descendantValue = root.EvaluateValue(JsonPath.Parse("$..title"));
        Assert.Equal("Catalog", descendantValue?.GetString());

        var sliceResult = JsonPath.Parse("$.items[0:3:2].name").Evaluate(root);
        Assert.Equal(2, sliceResult.Count);
        Assert.Equal("foo", sliceResult[0].Value.GetString());
        Assert.Equal("foobar", sliceResult[1].Value.GetString());

        var filterResult = JsonPath.Parse("$.items[?@.price < 10].name").Evaluate(root);
        Assert.Equal(2, filterResult.Count);
        Assert.Equal("foo", filterResult[0].Value.GetString());
        Assert.Equal("foobar", filterResult[1].Value.GetString());

        var functionResult = JsonPath.Parse("$.items[?length(@.tags) > 1].name").Evaluate(root);
        Assert.Single(functionResult);
        Assert.Equal("foo", functionResult[0].Value.GetString());
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

    [Theory]
    [InlineData("match")]
    [InlineData("search")]
    public void Evaluate_Function_CatastrophicBacktrackingPattern_DoesNotThrow(string function)
    {
        // '(a|aa)+$' against a non-matching string is the classic exponential-backtracking case. Before
        // NonBacktracking it spun for the full timeout and then threw RegexMatchTimeoutException straight
        // out of Evaluate -- including in Lax mode, which documents that errors produce no match.
        var doc = JsonNode.Parse($$"""[{"s": "{{new string('a', 40)}}b"}]""");
        var path = JsonPath.Parse($"$[?{function}(@.s, '(a|aa)+$')]");

        var lax = path.Evaluate(doc, JsonPathEvaluationMode.Lax);
        var strict = path.Evaluate(doc, JsonPathEvaluationMode.Strict);

        Assert.Empty(lax);
        Assert.Empty(strict);
    }

    [Fact]
    public void Evaluate_Function_CatastrophicBacktrackingPattern_CompletesQuickly()
    {
        var array = new JsonArray();
        for (var i = 0; i < 50; i++)
        {
            array.Add(new JsonObject { ["s"] = new string('a', 40) + "b" });
        }

        var path = JsonPath.Parse("$[?match(@.s, '(a|aa)+$')]");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = path.Evaluate(array);
        stopwatch.Stop();

        Assert.Empty(result);

        // The old backtracking engine took ~5s per element, so 50 elements would have needed ~250s.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Evaluation took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Evaluate_Function_Match_NonLiteralPattern_IsEvaluatedPerNode()
    {
        // The pattern comes from the document and differs per node, so the cached regex of one node must not be
        // used for the next.
        var doc = JsonNode.Parse("""[{"s": "foo", "p": "f.o"}, {"s": "bar", "p": "z.z"}, {"s": "baz", "p": "b.z"}]""");
        var path = JsonPath.Parse("$[?match(@.s, @.p)]");

        var result = path.Evaluate(doc);

        Assert.Equal(2, result.Count);
        Assert.Equal("foo", result[0].Value!.AsObject()["s"]!.GetValue<string>());
        Assert.Equal("baz", result[1].Value!.AsObject()["s"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_Function_Match_NonLiteralPattern_AlternatingAcrossNodes()
    {
        // The cache keeps one pattern, so the nodes below alternate between hits and replacements, including
        // replacing a valid pattern with an invalid one and back.
        var doc = JsonNode.Parse("""
            [
              {"id": 0, "s": "foo", "p": "f.o"},
              {"id": 1, "s": "foo", "p": "f.o"},
              {"id": 2, "s": "bar", "p": "f.o"},
              {"id": 3, "s": "bar", "p": "b.r"},
              {"id": 4, "s": "foo", "p": "("},
              {"id": 5, "s": "foo", "p": "f.o"},
              {"id": 6, "s": "foo", "p": "b.r"},
              {"id": 7, "s": "bar", "p": "b.r"},
              {"id": 8, "s": "(", "p": "("}
            ]
            """);
        var path = JsonPath.Parse("$[?match(@.s, @.p)]");
        int[] expected = [0, 1, 3, 5, 7];

        var ids = path.Evaluate(doc).Select(match => match.Value!["id"]!.GetValue<int>()).ToArray();

        Assert.Equal(expected, ids);
    }

    [Fact]
    public void Evaluate_Function_Match_DocumentPattern_IsStableAcrossDocumentsAndThreads()
    {
        // The pattern is shared by every node of a document but differs between documents, so an entry cached
        // while evaluating one document must not answer for the other, even when both are evaluated at once.
        var items = string.Join(", ", Enumerable.Range(0, 50).Select(i => i % 2 is 0 ? "\"abc\"" : "\"xyz\""));
        var abc = JsonNode.Parse($$"""{"pattern": "a.c", "items": [{{items}}]}""");
        var xyz = JsonNode.Parse($$"""{"pattern": "x.z", "items": [{{items}}]}""");
        var path = JsonPath.Parse("$.items[?match(@, $.pattern)]");

        var results = new JsonPathResult[64];
        results[0] = path.Evaluate(abc);
        results[1] = path.Evaluate(xyz);
        Parallel.For(2, results.Length, i => results[i] = path.Evaluate(i % 2 is 0 ? abc : xyz));

        for (var i = 0; i < results.Length; i++)
        {
            var expected = i % 2 is 0 ? "abc" : "xyz";
            Assert.Equal(25, results[i].Count);
            Assert.All(results[i], match => Assert.Equal(expected, match.Value!.GetValue<string>()));
        }
    }

    [Fact]
    public void Evaluate_Function_Match_RegexCache_ReusesTheEntryForAnEqualPattern()
    {
        // A pattern read from the document is usually a new string instance for every node, so the cache must
        // compare patterns by value to ever hit.
        var func = new FunctionCallExpression("match", [], FunctionExpressionType.LogicalType);
        var pattern = "a.c";
        var equalPattern = new string(pattern.AsSpan());
        var built = new List<string>();
        string[] expectedBuilt = ["a.c", "x.z"];

        var first = func.GetOrCreateRegex(pattern, anchored: true, Factory);
        var second = func.GetOrCreateRegex(equalPattern, anchored: true, Factory);
        var other = func.GetOrCreateRegex("x.z", anchored: true, Factory);

        Assert.NotSame(pattern, equalPattern);
        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal(expectedBuilt, built);

        FunctionCallExpression.RegexCacheEntry Factory(string p, bool _)
        {
            built.Add(p);
            return new FunctionCallExpression.RegexCacheEntry(regex: null);
        }
    }

    [Fact]
    public void Evaluate_Function_Match_RegexCache_KeepsSeveralPatternsAndEvictsTheOldest()
    {
        // Nodes whose patterns alternate between a few values must not recompile on every switch, yet a document
        // with many patterns must not grow the cache without bound.
        var func = new FunctionCallExpression("match", [], FunctionExpressionType.LogicalType);
        var built = new List<string>();
        var patterns = Enumerable.Range(0, FunctionCallExpression.RegexCacheCapacity).Select(i => "p" + i).ToArray();

        for (var round = 0; round < 3; round++)
        {
            foreach (var pattern in patterns)
            {
                func.GetOrCreateRegex(pattern, anchored: true, Factory);
            }
        }

        Assert.Equal(patterns, built);

        func.GetOrCreateRegex("new", anchored: true, Factory);
        func.GetOrCreateRegex(patterns[^1], anchored: true, Factory);
        func.GetOrCreateRegex(patterns[0], anchored: true, Factory);

        Assert.Equal([.. patterns, "new", patterns[0]], built);

        FunctionCallExpression.RegexCacheEntry Factory(string p, bool _)
        {
            built.Add(p);
            return new FunctionCallExpression.RegexCacheEntry(regex: null);
        }
    }

    [Fact]
    public void Evaluate_Function_Match_CachedRegex_IsStableAcrossReuseAndThreads()
    {
        // A parsed JsonPath is documented as reusable and thread-safe, and the regex cache is populated lazily.
        var doc = JsonNode.Parse("""[{"a": "foo"}, {"a": "bar"}, {"a": "foobar"}]""");
        var path = JsonPath.Parse("""$[?search(@.a, "foo")]""");

        Assert.Equal(2, path.Evaluate(doc).Count);
        Assert.Equal(2, path.Evaluate(doc).Count);

        var counts = new int[32];
        Parallel.For(0, counts.Length, i => counts[i] = path.Evaluate(doc).Count);
        Assert.All(counts, count => Assert.Equal(2, count));
    }

    [Fact]
    public void Evaluate_Function_Match_UnusablePatternIsCachedToo()
    {
        var doc = JsonNode.Parse("""[{"s": "aa"}, {"s": "bb"}]""");
        var path = JsonPath.Parse("$[?match(@.s, '(')]");

        Assert.Empty(path.Evaluate(doc));
        Assert.Empty(path.Evaluate(doc));
    }

    [Theory]
    // The pattern is written as it appears inside the JSONPath string literal, so a backslash intended for
    // the regex engine has to be escaped for the JSONPath lexer first.
    [InlineData("(")]                  // not a valid pattern at all
    [InlineData("a{2,1}")]             // invalid quantifier range
    [InlineData(@"(a)\\1")]            // backreference: not part of I-Regexp, unsupported by NonBacktracking
    [InlineData("(?=a)b")]             // lookahead: likewise
    public void Evaluate_Function_UnusablePattern_YieldsNoMatch(string pattern)
    {
        var doc = JsonNode.Parse("""[{"s": "aa"}]""");
        var path = JsonPath.Parse($"$[?match(@.s, '{pattern}')]");

        Assert.Empty(path.Evaluate(doc, JsonPathEvaluationMode.Lax));
        Assert.Empty(path.Evaluate(doc, JsonPathEvaluationMode.Strict));
    }

    [Theory]
    // RFC 9535 §2.4.6 has match() match the whole string. '$' would also match just before a final line feed,
    // so the anchors have to be \A and \z.
    [InlineData("a\n")]
    [InlineData("a\r\n")]
    [InlineData("a\r")]
    public void Evaluate_Function_Match_DoesNotAcceptAnUnmatchedTrailingNewLine(string value)
    {
        var doc = new JsonArray { value };

        Assert.Empty(JsonPath.Parse("$[?match(@, 'a')]").Evaluate(doc));
    }

    [Theory]
    // A supplementary character is one Unicode scalar value, not the two UTF-16 code units .NET stores it as.
    [InlineData(".", true)]
    [InlineData("..", false)]
    [InlineData("[\U0001F600]", true)]
    [InlineData("[\U0001F5FF-\U0001F601]", true)]
    [InlineData("[\U0001F601-\U0001F602]", false)]
    [InlineData("[^a]", true)]
    [InlineData("[^\U0001F600]", false)]
    [InlineData(@"\\p{So}", true)]
    [InlineData(@"\\P{So}", false)]
    [InlineData(@"\\p{Lu}", false)]
    public void Evaluate_Function_Match_TreatsASupplementaryCharacterAsOneScalarValue(string pattern, bool matches)
    {
        var doc = new JsonArray { "\U0001F600" };

        var result = JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc);

        Assert.Equal(matches ? 1 : 0, result.Count);
    }

    [Theory]
    // RFC 9485 §3 lists '^', '$' and the other characters .NET reads as metacharacters among the NormalChars,
    // and §4 gives an I-Regexp the semantics of an XSD regexp, where they stand for themselves.
    [InlineData("^a$", "^a$", true)]
    [InlineData("^a$", "a", false)]
    [InlineData("a$", "a$", true)]
    [InlineData("a$", "a", false)]
    public void Evaluate_Function_Match_TreatsCaretAndDollarAsLiterals(string pattern, string value, bool matches)
    {
        var doc = new JsonArray { value };

        var result = JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc);

        Assert.Equal(matches ? 1 : 0, result.Count);
    }

    [Theory]
    // Constructs .NET understands but that are no part of I-Regexp (RFC 9485 §3). Accepting them would let a
    // pattern mean one thing here and another in every other RFC 9535 implementation.
    [InlineData("(?i)a", "A")]
    [InlineData("(?:a)", "a")]
    [InlineData(@"\\d", "1")]
    [InlineData(@"\\w", "a")]
    [InlineData(@"\\s", " ")]
    [InlineData(@"\\p{IsBasicLatin}", "a")]
    [InlineData(@"\\x41", "A")]
    [InlineData("a**", "aa")]
    [InlineData("*a", "a")]
    [InlineData("[a", "a")]
    [InlineData("a]", "a]")]
    [InlineData("a)", "a")]
    [InlineData("[]", "")]
    [InlineData("[b-a]", "a")]
    [InlineData("a{2,1}", "aa")]
    [InlineData("[!--]", "-")]             // CCchar leaves out "-": unescaped, it cannot end a range
    [InlineData("[!--]", "!")]
    [InlineData("[---]", "-")]
    [InlineData("[a-z-[aeiou]]", "b")]     // character class subtraction
    [InlineData(@"[a-\\p{L}]", "a")]       // a category escape cannot end a range...
    [InlineData(@"[\\p{L}-a]", "a")]       // ...or start one
    [InlineData("[^]", "^")]               // ruled out by RFC 9485 §3 even though the grammar reads it as "[\^]"
    [InlineData(@"\\p{Cs}", "a")]          // not an IsCategory
    [InlineData(@"\\p{Lx}", "a")]
    [InlineData(@"\\p{IsGreek}", "\u03B1")]     // Unicode blocks
    [InlineData(@"\\p{L", "a")]
    [InlineData(@"\\pL", "a")]
    [InlineData(@"\\u0041", "A")]          // no code point escapes
    [InlineData(@"\\$", "$")]              // "$" is a NormalChar, so escaping it is not allowed
    [InlineData(@"\\i", "a")]              // XSD multi-character escapes
    [InlineData(@"[\\d]", "1")]
    [InlineData("a{,2}", "a")]             // the minimum of a range quantifier is mandatory
    [InlineData("a*?", "a")]               // no lazy quantifiers
    [InlineData("a{2}{3}", "aaaaaa")]
    [InlineData("a|*", "a")]
    [InlineData("{", "{")]
    [InlineData("}", "}")]
    [InlineData("a{1", "a")]
    [InlineData(@"\\", @"\")]
    [InlineData(@"[\\]", @"\")]
    public void Evaluate_Function_PatternOutsideIRegexp_YieldsNoMatch(string pattern, string value)
    {
        var doc = new JsonArray { value };

        Assert.Empty(JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc));
        Assert.Empty(JsonPath.Parse($"$[?search(@, '{pattern}')]").Evaluate(doc));
    }

    [Theory]
    // Valid I-Regexps that .NET cannot build an automaton for. RFC 9535 turns a pattern that cannot be
    // evaluated into LogicalFalse, so none of these may escape Evaluate as an exception.
    [InlineData("a{1000000}")]
    [InlineData("(a{100000}){100000}")]
    public void Evaluate_Function_PatternBeyondTheEngineLimits_YieldsNoMatch(string pattern)
    {
        var doc = new JsonArray { "aa" };

        Assert.Empty(JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc, JsonPathEvaluationMode.Lax));
        Assert.Empty(JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc, JsonPathEvaluationMode.Strict));
    }

    [Theory]
    // The translation recurses once per group, so the depth has to be capped rather than left to the stack.
    [InlineData(8, 1)]
    [InlineData(64, 1)]
    [InlineData(65, 0)]
    [InlineData(100_000, 0)]
    public void Evaluate_Function_Match_DeeplyNestedGroups_AreCappedInsteadOfOverflowingTheStack(int depth, int expected)
    {
        var pattern = new string('(', depth) + "a" + new string(')', depth);
        var doc = new JsonArray { "a" };

        var result = JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc);

        Assert.Equal(expected, result.Count);
    }

    [Theory]
    // The rest of the grammar still has to work as it did.
    [InlineData("a|b", "b", true)]
    [InlineData("a|", "", true)]
    [InlineData("(ab)+", "abab", true)]
    [InlineData("a{2,3}", "aa", true)]
    [InlineData("a{2,3}", "a", false)]
    [InlineData("a{2,}", "aaaa", true)]
    [InlineData("a{2}", "aa", true)]
    [InlineData("[-a]", "-", true)]
    [InlineData("[a-]", "-", true)]
    [InlineData("[^a]", "b", true)]
    [InlineData(@"\\n", "\n", true)]
    [InlineData(".", "\n", false)]
    [InlineData(".", "\r", false)]
    [InlineData(@"a\\.c", "a.c", true)]
    [InlineData(@"a\\.c", "abc", false)]
    [InlineData("[--]", "-", true)]                     // "-" first, then "-" last
    [InlineData(@"[#-\\-]", "$", true)]                 // an escaped "-" can end a range
    [InlineData(@"[#-\\-]", "-", true)]
    [InlineData(@"[#-\\-]", ".", false)]
    [InlineData(@"[\\--/]", ".", true)]                 // and start one
    [InlineData(@"[\\p{Lu}-]", "-", true)]
    [InlineData(@"[\\p{Lu}-]", "A", true)]
    [InlineData("[^-a]", "-", false)]
    [InlineData("[^-a]", "b", true)]
    [InlineData("[^^]", "^", false)]
    [InlineData("[a^]", "^", true)]
    [InlineData("[.]", ".", true)]                      // metacharacters stand for themselves in a class
    [InlineData("[.]", "a", false)]
    [InlineData("[$(*+?{|}]", "|", true)]
    [InlineData("a{0}", "", true)]
    [InlineData("a{0,0}b", "b", true)]
    [InlineData("a{01}", "a", true)]
    [InlineData("(a|b)*c", "abbac", true)]
    [InlineData("a||b", "", true)]                      // an empty branch
    [InlineData("()", "", true)]
    [InlineData(@"\\^\\{\\}\\|\\-\\[\\]\\(\\)\\*\\+\\?", "^{}|-[]()*+?", true)]
    [InlineData(@"\\t\\r", "\t\r", true)]
    [InlineData(".", "\u2028", true)]                   // "." leaves out LF and CR only
    [InlineData(".", "\u0085", true)]
    [InlineData("[^a]", "\n", true)]                    // and a negated class leaves out neither
    [InlineData(@"\\p{Nd}", "\u0663", true)]            // ARABIC-INDIC DIGIT THREE
    [InlineData(@"\\p{Nd}", "\U0001D7CE", true)]        // MATHEMATICAL BOLD DIGIT ZERO
    [InlineData(@"\\p{Lu}", "\U0001D400", true)]        // MATHEMATICAL BOLD CAPITAL A
    [InlineData(@"\\p{Zs}", "\u00A0", true)]            // NO-BREAK SPACE
    [InlineData(@"\\p{L}\\p{M}", "e\u0301", true)]      // COMBINING ACUTE ACCENT
    [InlineData(@"\\p{Co}", "\U0010FFFD", true)]
    [InlineData(@"[^\\p{L}]", "1", true)]
    [InlineData(@"[^\\p{L}]", "a", false)]
    [InlineData(@"[\\P{L}a]", "a", true)]
    [InlineData(@"[^\\P{L}]", "a", true)]
    [InlineData("\U0001F600+", "\U0001F600\U0001F600", true)] // a quantifier repeats the whole scalar value
    public void Evaluate_Function_Match_SupportsTheIRegexpGrammar(string pattern, string value, bool matches)
    {
        var doc = new JsonArray { value };

        var result = JsonPath.Parse($"$[?match(@, '{pattern}')]").Evaluate(doc);

        Assert.Equal(matches ? 1 : 0, result.Count);
    }

    [Theory]
    // search() looks for the I-Regexp anywhere in the string (RFC 9535 §2.4.7).
    [InlineData("b", "abc", true)]
    [InlineData("^b", "abc", false)]                    // "^" and "$" are literals, not anchors
    [InlineData("^b", "a^bc", true)]
    [InlineData("c$", "abc", false)]
    [InlineData("c$", "abc$d", true)]
    [InlineData("", "abc", true)]
    [InlineData("x*", "abc", true)]
    [InlineData("[^a]", "a", false)]
    [InlineData(".", "\r\n", false)]
    [InlineData("a.b", "xa\U0001F600by", true)]
    [InlineData("a..b", "xa\U0001F600by", false)]
    public void Evaluate_Function_Search_FindsTheIRegexpAnywhereInTheString(string pattern, string value, bool matches)
    {
        var doc = new JsonArray { value };

        var result = JsonPath.Parse($"$[?search(@, '{pattern}')]").Evaluate(doc);

        Assert.Equal(matches ? 1 : 0, result.Count);
    }

    [Theory]
    [InlineData("||")]
    [InlineData("&&")]
    public void Evaluate_LongBooleanChain_DoesNotOverflowTheStack(string op)
    {
        // A flat chain does not count towards the parser's nesting limit, so the evaluator cannot afford a frame
        // per operand: it used to walk a pairwise tree and take the whole process down with it.
        var expression = "$[?" + string.Join($" {op} ", Enumerable.Repeat("@", 100_000)) + "]";
        var path = JsonPath.Parse(expression);

        Assert.Single(path.Evaluate(JsonNode.Parse("[1]")));
    }

    [Fact]
    public void Evaluate_StringComparison_OrdersByUnicodeScalarValue()
    {
        // RFC 9535 §2.3.5.2.2 orders strings by their scalar values. U+1F600 is above U+FFFF, even though the
        // surrogate pair UTF-16 writes it as sorts below it.
        var doc = new JsonArray { "\uFFFF", "\U0001F600" };

        var below = JsonPath.Parse("$[?@ < '\U0001F600']").Evaluate(doc);
        Assert.Single(below);
        Assert.Equal("\uFFFF", below[0].Value!.GetValue<string>());

        var above = JsonPath.Parse("$[?@ > '\\uFFFF']").Evaluate(doc);
        Assert.Single(above);
        Assert.Equal("\U0001F600", above[0].Value!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_Comparison_ReadsEveryNumericRepresentationAJsonValueCanHold()
    {
        // JsonValue can wrap any CLR numeric type. One the navigator does not know has to compare as no match,
        // not as a stand-in 0 that silently makes the value equal to 0.
        var doc = new JsonArray
        {
            JsonValue.Create((Half)42),
            JsonValue.Create((Int128)42),
            JsonValue.Create((UInt128)42),
        };

        Assert.Empty(JsonPath.Parse("$[?@ == 0]").Evaluate(doc));
        Assert.Equal(3, JsonPath.Parse("$[?@ == 42]").Evaluate(doc).Count);
        Assert.Equal(3, JsonPath.Parse("$[?@ > 41]").Evaluate(doc).Count);
        Assert.Equal(3, JsonPath.Parse("$[?@ < 43]").Evaluate(doc).Count);
    }

    [Fact]
    public void Evaluate_Path_IsCorrectForNodesSelectedByAFilter()
    {
        // Filter subqueries evaluate without tracking paths. The paths of the nodes the filter *selects*
        // are still tracked, so this guards against the untracked placeholder leaking into a result.
        var doc = JsonNode.Parse("""
            {"items": [{"a": {"b": 1}}, {"a": {"c": 2}}, {"a": {"b": 3}}]}
            """);

        var existence = JsonPath.Parse("$.items[?@.a.b]").Evaluate(doc);
        Assert.Equal(2, existence.Count);
        Assert.Equal("$['items'][0]", existence[0].Path);
        Assert.Equal("$['items'][2]", existence[1].Path);

        var comparison = JsonPath.Parse("$.items[?@.a.b > 1].a.b").Evaluate(doc);
        Assert.Single(comparison);
        Assert.Equal("$['items'][2]['a']['b']", comparison[0].Path);

        var nested = JsonPath.Parse("$..items[?@.a.b == 1]").Evaluate(doc);
        Assert.Single(nested);
        Assert.Equal("$['items'][0]", nested[0].Path);

        var absolute = JsonPath.Parse("$.items[?$.items[0].a.b == 1]").Evaluate(doc);
        Assert.Equal(3, absolute.Count);
        Assert.Equal("$['items'][0]", absolute[0].Path);
        Assert.Equal("$['items'][1]", absolute[1].Path);
        Assert.Equal("$['items'][2]", absolute[2].Path);
    }

    [Fact]
    public void Evaluate_Path_IsRenderedLazilyAndCached()
    {
        var doc = JsonNode.Parse("""{"store": {"book": [{"title": "A"}, {"title": "B"}]}}""");
        var result = JsonPath.Parse("$.store.book[*].title").Evaluate(doc);

        Assert.Equal(2, result.Count);
        Assert.Equal("$['store']['book'][0]['title']", result[0].Path);
        Assert.Equal("$['store']['book'][1]['title']", result[1].Path);

        // Reading twice must return the identical instance, not re-render.
        Assert.Same(result[0].Path, result[0].Path);
    }

    [Fact]
    public void Evaluate_Path_IsCorrectForCustomNavigatorAndEscapedNames()
    {
        var doc = JsonNode.Parse("""{"a'b": {"c\\d": [1]}}""");
        var result = JsonPath.Parse("$..*").Evaluate(doc);

        Assert.Contains(result, m => m.Path == @"$['a\'b']");
        Assert.Contains(result, m => m.Path == @"$['a\'b']['c\\d']");
        Assert.Contains(result, m => m.Path == @"$['a\'b']['c\\d'][0]");
    }

    [Fact]
    public void Evaluate_Path_EscapesMemberNamesAsTheNormalizedPathGrammarRequires()
    {
        // RFC 9535 §2.7: BS, HT, LF, FF and CR have a short escape and every other control character a lower case
        // \u00XX one. "'" and "\" are escaped too; anything else, DEL and supplementary characters included, is not.
        var doc = new JsonObject();
        for (var c = 0; c < 0x20; c++)
        {
            doc[((char)c).ToString()] = c;
        }

        doc["'"] = 0x27;
        doc["\\"] = 0x5C;
        doc["\""] = 0x22;
        doc["\u007F"] = 0x7F;
        doc["\u00E9"] = 0xE9;
        doc["\U0001F600"] = 0x1F600;

        var paths = JsonPath.Parse("$.*").Evaluate(doc).ToDictionary(match => match.Value!.GetValue<int>(), match => match.Path);

        for (var c = 0; c < 0x20; c++)
        {
            var escaped = c switch
            {
                0x08 => @"\b",
                0x09 => @"\t",
                0x0A => @"\n",
                0x0C => @"\f",
                0x0D => @"\r",
                _ => $@"\u{c:x4}",
            };

            Assert.Equal($"$['{escaped}']", paths[c]);
        }

        Assert.Equal(@"$['\'']", paths[0x27]);
        Assert.Equal(@"$['\\']", paths[0x5C]);
        Assert.Equal("$['\"']", paths[0x22]);
        Assert.Equal("$['\u007F']", paths[0x7F]);
        Assert.Equal("$['\u00E9']", paths[0xE9]);
        Assert.Equal("$['\U0001F600']", paths[0x1F600]);
    }

    [Fact]
    public void Evaluate_Path_IsAQuerySelectingTheNodeItNames()
    {
        // A normalized path is itself a JSONPath query, one that selects exactly the node it identifies.
        var doc = JsonNode.Parse("""
            {"a": [1, {"b\u0000'\\\"": [true, null, {}]}], "": {"\ud83d\ude00": "x", "c d": [[]]}, "\u007f\n": -0}
            """);

        var matches = JsonPath.Parse("$..*").Evaluate(doc);

        Assert.HasCount(12, matches);
        foreach (var match in matches)
        {
            var selected = JsonPath.Parse(match.Path).Evaluate(doc);

            Assert.Single(selected);
            Assert.Equal(match.Path, selected[0].Path);
            Assert.Same(match.Value, selected[0].Value);
        }
    }

    [Theory]
    // RFC 9535 §2.3.5.2.2. An empty nodelist is only equal to another one, values of different types are never
    // equal, arrays and objects compare deeply, only numbers and strings are ordered, and "<=" and ">=" are
    // "< or ==" and "> or ==": two empty nodelists satisfy both without either being less than the other.
    [InlineData("$[?@.x == @.y]", "$[0]", "$[2]", "$[5]", "$[6]", "$[7]", "$[9]")]
    [InlineData("$[?@.x != @.y]", "$[1]", "$[3]", "$[4]", "$[8]")]
    [InlineData("$[?@.x < @.y]", "$[3]")]
    [InlineData("$[?@.x <= @.y]", "$[0]", "$[2]", "$[3]", "$[5]", "$[6]", "$[7]", "$[9]")]
    [InlineData("$[?@.x > @.y]")]
    [InlineData("$[?@.x >= @.y]", "$[0]", "$[2]", "$[5]", "$[6]", "$[7]", "$[9]")]
    [InlineData("$[?@.x < 'b']", "$[4]")]
    [InlineData("$[?@.x <= null]", "$[5]")]
    [InlineData("$[?@.x < true]")]
    [InlineData("$[?@.x == 1]", "$[1]", "$[2]", "$[3]", "$[9]")]
    public void Evaluate_Comparison_FollowsTheRfc9535Semantics(string query, params string[] expectedPaths)
    {
        var doc = JsonNode.Parse("""
            [
              {},
              {"x": 1},
              {"x": 1, "y": 1.0},
              {"x": 1, "y": 2},
              {"x": "a", "y": 1},
              {"x": null, "y": null},
              {"x": [1, {"a": null}], "y": [1, {"a": null}]},
              {"x": {"a": [1], "b": {}}, "y": {"b": {}, "a": [1]}},
              {"x": [1, 2], "y": [2, 1]},
              {"x": 1e0, "y": 10e-1}
            ]
            """);

        var result = JsonPath.Parse(query).Evaluate(doc);

        Assert.Equal(expectedPaths, result.Select(match => match.Path));
    }

    [Fact]
    public void Evaluate_DescendantSegment_CyclicNavigator_ThrowsInsteadOfOverflowingTheStack()
    {
        var root = CyclicNode.CreateCycle();
        var path = JsonPath.Parse("$..name");

        Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(root, CyclicNavigator.Instance));
        Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(root, CyclicNavigator.Instance, JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void Evaluate_DescendantSegment_VeryDeepDocument_ThrowsInsteadOfOverflowingTheStack()
    {
        JsonNode node = JsonValue.Create(1);
        for (var i = 0; i < 20_000; i++)
        {
            node = new JsonObject { ["a"] = node };
        }

        Assert.Throws<JsonPathEvaluationException>(() => JsonPath.Parse("$..a").Evaluate(node));
    }

    [Fact]
    public void Evaluate_DescendantSegment_DocumentAtTheSystemTextJsonDepthLimit_Succeeds()
    {
        // System.Text.Json's own parsers cap documents at MaxDepth 64, so anything they can produce must
        // still evaluate. The evaluator's own limit is well above that.
        JsonNode node = JsonValue.Create(1);
        for (var i = 0; i < 60; i++)
        {
            node = new JsonObject { ["a"] = node };
        }

        Assert.HasCount(60, JsonPath.Parse("$..a").Evaluate(node));
    }

    [Fact]
    public void Evaluate_DeepEquality_VeryDeepDocument_ThrowsInsteadOfOverflowingTheStack()
    {
        static JsonNode Build()
        {
            JsonNode node = JsonValue.Create(1);
            for (var i = 0; i < 20_000; i++)
            {
                node = new JsonObject { ["a"] = node };
            }

            return node;
        }

        var doc = new JsonObject { ["x"] = Build(), ["arr"] = new JsonArray { Build() } };

        Assert.Throws<JsonPathEvaluationException>(() => JsonPath.Parse("$.arr[?@ == $.x]").Evaluate(doc));
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
    public void Evaluate_JsonValueWrappingNonStringClrType_ReadsTheStringValue()
    {
        // These CLR types serialize as JSON strings, but their JsonValue.ToString() is quoted and escaped JSON text.
        var json = new JsonArray(new JsonObject
        {
            ["date"] = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ["offset"] = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
            ["guid"] = Guid.Empty,
            ["char"] = 'x',
        });

        Assert.Single(JsonPath.Parse("$[?@.date == '2020-01-02T03:04:05Z']").Evaluate(json));
        Assert.Single(JsonPath.Parse("$[?@.offset == '2020-01-02T03:04:05+01:00']").Evaluate(json));
        Assert.Single(JsonPath.Parse("$[?@.guid == '00000000-0000-0000-0000-000000000000']").Evaluate(json));
        Assert.Single(JsonPath.Parse("$[?@.char == 'x']").Evaluate(json));
        Assert.Single(JsonPath.Parse("$[?length(@.char) == 1]").Evaluate(json));
        Assert.Single(JsonPath.Parse("$[?match(@.date, '2020-.*Z')]").Evaluate(json));
        Assert.Single(JsonPath.Parse("$[?@.date < '2021']").Evaluate(json));
    }

    [Fact]
    public void Evaluate_JsonValueWrappingEnum_ReadsTheNumberValue()
    {
        var json = new JsonArray(new JsonObject { ["day"] = JsonValue.Create(DayOfWeek.Monday) });

        Assert.Single(JsonPath.Parse("$[?@.day == 1]").Evaluate(json));
        Assert.Empty(JsonPath.Parse("$[?@.day == 0]").Evaluate(json));
    }

    [Fact]
    public void Evaluate_JsonValueWrappingClrObject_IsNavigatedAsAnObject()
    {
        var json = new JsonObject
        {
            ["dictionary"] = JsonValue.Create(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 }),
            ["list"] = JsonValue.Create(new List<int> { 10, 20, 30 }),
        };

        Assert.Empty(JsonPath.Parse("$[?@ == null]").Evaluate(json));
        Assert.Equal(2, JsonPath.Parse("$.dictionary.b").EvaluateValue(json)!.GetValue<int>());
        Assert.Equal(30, JsonPath.Parse("$.list[-1]").EvaluateValue(json)!.GetValue<int>());
        Assert.Equal(["$['dictionary']['a']", "$['dictionary']['b']"], JsonPath.Parse("$.dictionary.*").Evaluate(json).Select(match => match.Path));
        Assert.Single(JsonPath.Parse("$[?length(@) == 3]").Evaluate(json));
        Assert.Equal(5, JsonPath.Parse("$..*").Evaluate(json).Count(match => match.Value is JsonValue value && value.GetValueKind() is JsonValueKind.Number));
    }

    [Theory]
    // JsonElement's indexer walks an array of objects or arrays from its start, so the evaluator copies such an
    // array before walking it. Every way of walking one must still see each element at its own index.
    [InlineData("$.items[*]")]
    [InlineData("$.items[?@.id > 1]")]
    [InlineData("$.items[1:4]")]
    [InlineData("$.items[::2]")]
    [InlineData("$.items[::-1]")]
    [InlineData("$.items[-2:0:-1]")]
    [InlineData("$.items[3]")]
    [InlineData("$..id")]
    [InlineData("$..[1]")]
    [InlineData("$.items[?@.tags == $.items[2].tags]")]
    [InlineData("$.items[?@ == $.items[4]]")]
    [InlineData("$.nested[?@[1][0] == 'b']")]
    [InlineData("$.scalars[1:]")]
    public void Evaluate_JsonElement_ArrayWalks_MatchJsonNode(string query)
    {
        const string Json = """
            {
              "items": [
                {"id": 0, "tags": ["a"]},
                {"id": 1, "tags": ["a", "b"]},
                {"id": 2, "tags": ["c"]},
                {"id": 3, "tags": ["a", "b"]},
                {"id": 4, "tags": []}
              ],
              "nested": [[["a"], ["b"]], [["c"]], [[], ["b"]]],
              "scalars": [1, 2, 3]
            }
            """;
        using var document = JsonDocument.Parse(Json);
        var path = JsonPath.Parse(query);

        var expected = path.Evaluate(JsonNode.Parse(Json)).Select(match => (match.Path, match.Value!.ToJsonString())).ToArray();
        var actual = path.Evaluate(document).Select(match => (match.Path, match.Value.GetRawText().Replace(" ", "", StringComparison.Ordinal))).ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Evaluate_ExistenceTest_StopsAtTheFirstNode()
    {
        // '@..name' on the child finds its parent's name before it would follow the cycle any further, and an
        // existence test needs no more than that. count() needs every node, so it still hits the depth limit.
        var root = CyclicNode.CreateCycle();

        var result = JsonPath.Parse("$[?@..name]").Evaluate(root, CyclicNavigator.Instance);

        Assert.Single(result);
        Assert.Equal("$['child']", result[0].Path);
        Assert.Throws<JsonPathEvaluationException>(() => JsonPath.Parse("$[?count(@..name) > 0]").Evaluate(root, CyclicNavigator.Instance));
    }

    [Theory]
    // Stopping a subquery early must not change what it answers: existence needs one node, value() exactly one.
    [InlineData("$[?@.*]", "$[1]", "$[2]", "$[3]")]
    [InlineData("$[?@..x]", "$[2]", "$[3]")]
    [InlineData("$[?@.*.x]", "$[2]", "$[3]")]
    [InlineData("$[?!@.*]", "$[0]", "$[4]")]
    [InlineData("$[?value(@.*) == 1]", "$[1]")]
    [InlineData("$[?value(@..x) == 1]", "$[2]")]
    [InlineData("$[?count(@..*) == 4]", "$[3]")]
    public void Evaluate_FilterSubqueries_KeepTheirResultsWhenStoppedEarly(string query, params string[] expectedPaths)
    {
        var doc = JsonNode.Parse("""
            [
              {},
              {"a": 1},
              {"a": 1, "b": {"x": 1}},
              {"a": {"x": 2}, "b": {"x": 3}},
              5
            ]
            """);

        Assert.Equal(expectedPaths, JsonPath.Parse(query).Evaluate(doc).Select(match => match.Path));
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

    private sealed class CyclicNode
    {
        public Dictionary<string, CyclicNode?> Properties { get; } = new(StringComparer.Ordinal);

        public string? Text { get; init; }

        /// <summary>Builds a two-node graph where the child points back at its parent.</summary>
        public static CyclicNode CreateCycle()
        {
            var parent = new CyclicNode();
            var child = new CyclicNode();
            parent.Properties["child"] = child;
            parent.Properties["name"] = new CyclicNode { Text = "root" };
            child.Properties["parent"] = parent;
            return parent;
        }
    }

    private sealed class CyclicNavigator : JsonPathNavigator<CyclicNode>
    {
        public static CyclicNavigator Instance { get; } = new();

        public override JsonPathNodeKind GetKind(CyclicNode? value)
        {
            if (value is null)
                return JsonPathNodeKind.Null;

            return value.Text is null ? JsonPathNodeKind.Object : JsonPathNodeKind.String;
        }

        public override bool TryGetPropertyValue(CyclicNode? value, string name, out CyclicNode? result)
        {
            result = null;
            return value is not null && value.Properties.TryGetValue(name, out result);
        }

        public override IEnumerable<JsonPathProperty<CyclicNode>> GetProperties(CyclicNode? value)
        {
            if (value is null)
                yield break;

            foreach (var property in value.Properties)
            {
                yield return new JsonPathProperty<CyclicNode>(property.Key, property.Value);
            }
        }

        public override int GetArrayLength(CyclicNode? value) => 0;

        public override bool TryGetElement(CyclicNode? value, int index, out CyclicNode? result)
        {
            result = null;
            return false;
        }

        public override bool TryGetString(CyclicNode? value, out string? result)
        {
            result = value?.Text;
            return result is not null;
        }

        public override bool TryGetNumber(CyclicNode? value, out double result)
        {
            result = 0;
            return false;
        }

        public override bool TryGetBoolean(CyclicNode? value, out bool result)
        {
            result = false;
            return false;
        }
    }
}

#pragma warning disable MA0048 // File name must match type name
#region JsonPath_CustomNavigatorTypes
internal sealed class MyNode
{
    private MyNode(JsonPathNodeKind kind) => Kind = kind;

    public JsonPathNodeKind Kind { get; }
    public object? Value { get; private init; } // string, double or bool
    public Dictionary<string, MyNode?> Properties { get; private init; } = [];
    public List<MyNode?> Items { get; private init; } = [];

    public static MyNode Object(Dictionary<string, MyNode?> properties) => new(JsonPathNodeKind.Object) { Properties = properties };
    public static MyNode Array(params MyNode?[] items) => new(JsonPathNodeKind.Array) { Items = [.. items] };
    public static MyNode String(string value) => new(JsonPathNodeKind.String) { Value = value };
    public static MyNode Number(double value) => new(JsonPathNodeKind.Number) { Value = value };
    public static MyNode Boolean(bool value) => new(JsonPathNodeKind.Boolean) { Value = value };
}

internal sealed class MyNodeNavigator : JsonPathNavigator<MyNode>
{
    public static MyNodeNavigator Instance { get; } = new();

    // A null node is JSON null
    public override JsonPathNodeKind GetKind(MyNode? value) => value?.Kind ?? JsonPathNodeKind.Null;

    public override IEnumerable<JsonPathProperty<MyNode>> GetProperties(MyNode? value)
        => value?.Properties.Select(property => new JsonPathProperty<MyNode>(property.Key, property.Value)) ?? [];

    public override bool TryGetPropertyValue(MyNode? value, string name, out MyNode? result)
    {
        result = null;
        return value is not null && value.Properties.TryGetValue(name, out result);
    }

    public override int GetArrayLength(MyNode? value) => value?.Items.Count ?? 0;

    public override bool TryGetElement(MyNode? value, int index, out MyNode? result)
    {
        if (value is not null && index >= 0 && index < value.Items.Count)
        {
            result = value.Items[index];
            return true;
        }

        result = null;
        return false;
    }

    public override bool TryGetString(MyNode? value, out string? result)
    {
        result = value?.Value as string;
        return result is not null;
    }

    public override bool TryGetNumber(MyNode? value, out double result)
    {
        if (value?.Value is double number)
        {
            result = number;
            return true;
        }

        result = 0;
        return false;
    }

    public override bool TryGetBoolean(MyNode? value, out bool result)
    {
        if (value?.Value is bool boolean)
        {
            result = boolean;
            return true;
        }

        result = false;
        return false;
    }
}
#endregion
#pragma warning restore MA0048
