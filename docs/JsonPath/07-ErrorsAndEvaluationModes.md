---
title: Errors and evaluation modes
description: Handle invalid queries, choose between lax and strict evaluation, and know what each exception means.
---

A query can fail at two moments: when it is parsed, because it is not valid JSONPath, and when it is evaluated, because
the document does not have the expected shape.

## Invalid queries

[JsonPath.Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) throws a `FormatException`, and [JsonPath.TryParse](cref:Meziantou.Framework.Json.JsonPath.TryParse) returns `false`, for every query that is not
well-formed and valid, as RFC 9535 requires. Some of them look plausible:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_InvalidQueries;format=md-fenced-code-block;dedent=true -->
```csharp
// Well-formed and valid queries only: these look plausible but are rejected
Assert.False(JsonPath.TryParse("$.store.book[(@.length-1)]", out _)); // script expressions are not JSONPath
Assert.False(JsonPath.TryParse("$[?@.price = 10]", out _));            // the equality operator is ==
Assert.False(JsonPath.TryParse("$[?!@.a == 1]", out _));               // write !(@.a == 1)
Assert.False(JsonPath.TryParse("$[?length(@.*) > 1]", out _));         // length() takes a single value
Assert.False(JsonPath.TryParse("$[?count(@.*)]", out _));              // count() returns a number, not a test
Assert.False(JsonPath.TryParse("$[?upper(@.name) == 'A']", out _));    // unknown function
Assert.False(JsonPath.TryParse("$[?@.name =~ /^A/]", out _));          // use match() or search()
```
<!-- endref -->

The message of the `FormatException` describes the problem. Use [TryParse](cref:Meziantou.Framework.Json.JsonPath.TryParse) for queries that users type or that come
from configuration files, and [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) for queries written in your code. The rules for comparisons and functions are in
[Filters](04-Filters.md#rules-for-the-queries-in-a-test) and [Functions](05-Functions.md#type-rules).

## Lax and strict evaluation

[Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) and [EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) take an optional [JsonPathEvaluationMode](cref:Meziantou.Framework.Json.JsonPathEvaluationMode):

- [JsonPathEvaluationMode.Lax](cref:Meziantou.Framework.Json.JsonPathEvaluationMode.Lax), the default, follows RFC 9535: a selector that does not apply selects nothing. A missing
  member, an index outside the array or a name on an array is not an error.
- [JsonPathEvaluationMode.Strict](cref:Meziantou.Framework.Json.JsonPathEvaluationMode.Strict) throws a [JsonPathEvaluationException](cref:Meziantou.Framework.Json.JsonPathEvaluationException) for these cases instead. Use it when the
  document must have the shape the query expects, for example to validate a configuration file.

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluationModes;format=md-fenced-code-block;dedent=true -->
```csharp
var doc = JsonNode.Parse("""{"a": 1}""");
var path = JsonPath.Parse("$.name");

var laxValue = path.EvaluateValue(doc, JsonPathEvaluationMode.Lax);
Assert.Null(laxValue);

Assert.Throws<JsonPathEvaluationException>(() => path.EvaluateValue(doc, JsonPathEvaluationMode.Strict));
```
<!-- endref -->

## What strict mode checks

Strict mode checks the selectors of the child segments of the query (`.name`, `[0]`, `[*]`, `[1:3]`, `[?...]`), on
every node they apply to. A filter test or a descendant segment never throws, because looking for something that may
not be there is what they are for:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_StrictModeScope;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

In strict mode, a child segment throws when:

- a name selector applies to a value that is not an object, or the object has no such member;
- an index selector applies to a value that is not an array, or the index is outside the array;
- a wildcard or a filter selector applies to a value that is neither an object nor an array;
- a slice selector applies to a value that is not an array.

The message gives the normalized path of the node where the selector failed. The whole evaluation fails: no partial
result is returned.

## Exceptions

| Exception | Thrown by | When |
| --- | --- | --- |
| `FormatException` | [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) | The query is not valid JSONPath, or nests filters and function calls more than 64 levels deep |
| `ArgumentNullException` | [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse), [Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate), [EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) | The query or the navigator is `null` |
| [JsonPathEvaluationException](cref:Meziantou.Framework.Json.JsonPathEvaluationException) | [Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate), [EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) | A path error in strict mode; in both modes, a descendant segment or a comparison that goes more than 256 levels deep |

[JsonPathEvaluationException](cref:Meziantou.Framework.Json.JsonPathEvaluationException) derives from `InvalidOperationException`. The depth limits are described in
[Limitations](09-Limitations.md#limits).
