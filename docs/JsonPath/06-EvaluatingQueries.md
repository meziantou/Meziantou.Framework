---
title: Evaluating queries
description: Parse a query, evaluate it on JsonNode, JsonDocument, JsonElement or a stream, read the values and paths it matches, and change them.
---

## Parse the query

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_ParseAndTryParse;format=md-fenced-code-block;dedent=true -->
```csharp
// Parse throws a FormatException when the query is not valid
var path = JsonPath.Parse("$.store.book[?@.price < 10].title");
Assert.Equal("$.store.book[?@.price < 10].title", path.ToString());
Assert.Throws<FormatException>(() => JsonPath.Parse("store.book"));

// TryParse returns false instead, for queries that come from users or configuration files
Assert.True(JsonPath.TryParse("$.store.book[0]", out var first));
Assert.NotNull(first);
Assert.False(JsonPath.TryParse("$.store.book[", out _));
```
<!-- endref -->

- [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) throws a `FormatException` when the query is not valid; [TryParse](cref:Meziantou.Framework.Json.JsonPath.TryParse) returns `false`. Both also accept a
  `ReadOnlySpan<char>`.
- [JsonPath](cref:Meziantou.Framework.Json.JsonPath) implements `IParsable<JsonPath>` and `ISpanParsable<JsonPath>`, so generic parsing code accepts it.
- `ToString` returns the query as it was written.
- A [JsonPath](cref:Meziantou.Framework.Json.JsonPath) is immutable and thread-safe. Parse a query once, keep it in a field, and evaluate it on any number of
  documents, from any thread.

## Choose the JSON type

[Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) and [EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) accept the three JSON types of `System.Text.Json`, as methods of [JsonPath](cref:Meziantou.Framework.Json.JsonPath) or as
extension methods of the JSON value:

| Input | [Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) returns | [EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) returns |
| --- | --- | --- |
| `JsonNode?` | [JsonPathResult](cref:Meziantou.Framework.Json.JsonPathResult), a list of [JsonPathMatch](cref:Meziantou.Framework.Json.JsonPathMatch) | `JsonNode?` |
| `JsonDocument?` | [JsonPathResult\<JsonElement\>](cref:Meziantou.Framework.Json.JsonPathResult{TValue}) | `JsonElement?` |
| `JsonElement` | [JsonPathResult\<JsonElement\>](cref:Meziantou.Framework.Json.JsonPathResult{TValue}) | `JsonElement?` |

- Use `JsonDocument` or `JsonElement` to read a document: they are read-only views of the parsed JSON. Dispose the
  `JsonDocument` once you are done with the results, because the `JsonElement` values they hold belong to it.
- Use `JsonNode` when you also want to change the document.
- When a `JsonValue` wraps a CLR object or array, its matches are detached copies of the serialized value; changes to
  the wrapped object are reflected by the next evaluation, but changing a match does not change the wrapped object.
- Use a `Stream` or a `Utf8JsonReader` to read a large document without loading it in memory; see
  [Read a document without loading it](#read-a-document-without-loading-it).
- For other object models, see [Custom object models](08-CustomObjectModels.md).

## Read the matches

[Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) returns a read-only list of the matches, in the order the query selects them. Each match has:

- [Value](cref:Meziantou.Framework.Json.JsonPathMatch.Value): the matched value. With `JsonNode`, JSON `null` is a `null` value.
- [Path](cref:Meziantou.Framework.Json.JsonPathMatch.Path): the normalized path of the match (see below).

When you only need the values, [ToJsonArray](cref:Meziantou.Framework.Json.JsonPathResult.ToJsonArray) copies them into a new `JsonArray`:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_ToJsonArray;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"items":[{"id":1,"tags":["a"]},{"id":2,"tags":["b","c"]}]}""");

// ToJsonArray copies the matched values into a new array
JsonArray tags = JsonPath.Parse("$.items[*].tags[*]").Evaluate(document).ToJsonArray();
Assert.Equal("""["a","b","c"]""", tags.ToJsonString());
```
<!-- endref -->

[ToJsonArray](cref:Meziantou.Framework.Json.JsonPathResult.ToJsonArray) is only available on the result of a `JsonNode` evaluation.

## Get the first match

[EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) returns the value of the first match, or `null` when there is none:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluateValue;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"users":[{"name":"Alice"},{"name":"Bob"}]}""");

// EvaluateValue returns the first match, or null when nothing matches
JsonNode? firstName = document.EvaluateValue(JsonPath.Parse("$.users[*].name"));
Assert.Equal("Alice", firstName?.GetValue<string>());

JsonNode? missing = document.EvaluateValue(JsonPath.Parse("$.groups[0]"));
Assert.Null(missing);
```
<!-- endref -->

With `JsonNode`, `null` is also the value of a member that holds JSON `null`. When the difference matters, use
[Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) and count the matches, or use `JsonElement`, where JSON `null` is an element of kind `Null`:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_MissingVersusNull;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

[EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue) does not retain all final matches or build their normalized paths.
In Lax mode it stops when the first final match is found. Child-only intermediate segments propagate that demand:
`$[*].wanted` still searches later items when earlier ones have no `wanted` member. Long paths and paths with
intermediate descendant segments may retain intermediate nodes before searching the last segment.
Strict mode still validates later branches, so a later path error can throw after an earlier branch matched.

## Cancel or bound evaluation

Pass [JsonPathEvaluationOptions](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions) to the instance or extension
overloads of [Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) or [EvaluateValue](cref:Meziantou.Framework.Json.JsonPath.EvaluateValue).
The same options work with `JsonNode`, `JsonDocument`, `JsonElement` and custom navigators.

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluationOptions;format=md-fenced-code-block;dedent=true -->
```csharp
using var cancellation = new CancellationTokenSource();
var options = new JsonPathEvaluationOptions
{
    CancellationToken = cancellation.Token,
    MaximumWork = 10_000,
    MaximumResults = 2,
    MaximumIntermediateResults = 100,
};
var path = JsonPath.Parse("$[*]");
using var document = JsonDocument.Parse("[1,2]");
Assert.HasCount(2, document.Evaluate(path, options));
using var larger = JsonDocument.Parse("[1,2,3]");
Assert.Throws<JsonPathEvaluationException>(() => larger.Evaluate(path, options));
cancellation.Cancel();
Assert.Throws<OperationCanceledException>(() => document.Evaluate(path, options));
```
<!-- endref -->

The default options leave evaluation unlimited. Set any combination of:

| Option | Behavior |
| --- | --- |
| [CancellationToken](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions.CancellationToken) | Checked during traversal, nested queries, comparisons and function evaluation; cancellation throws `OperationCanceledException`. |
| [MaximumWork](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions.MaximumWork) | Limits evaluator checkpoints and navigator operations, including nested filter work. String scans and bulk array copies charge for their input length. |
| [MaximumResults](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions.MaximumResults) | Limits final matches, including duplicates. |
| [MaximumIntermediateResults](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions.MaximumIntermediateResults) | Limits nodes produced by each intermediate or filter-query segment, including count-only queries. |

Limits are nonnegative; `null` means no limit. A limit exceeded throws
[JsonPathEvaluationException](cref:Meziantou.Framework.Json.JsonPathEvaluationException), never a partial result.
When a final match limit is set, even Lax first-value evaluation scans all final matches to enforce it; it still
retains only the first. Intermediate limits can require materializing intermediate segments rather than
short-circuiting them.

Work counts are implementation-dependent, not elapsed-time or byte limits. Cancellation is cooperative: the evaluator
cannot interrupt a custom navigator operation, a custom function callback or a running regex match.
Custom callbacks are checked before and after invocation, and their navigator accesses share the evaluation budget.
Regex matches retain their per-call timeout.
Use a work budget as well as result limits to bound broad scans that select few results. Do not mutate the document
or options while evaluation is running; options are snapshotted once per call.

## Know where each value comes from

[Path](cref:Meziantou.Framework.Json.JsonPathMatch.Path) is the *normalized path* of the match, as defined by RFC 9535: the root, then one bracketed name or index per
level, such as `$['store']['book'][0]`. It identifies the node whatever query found it:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_NormalizedPaths;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"teams":{"red team":{"members":["Alice","Bob"]}}}""");

// Path is the normalized path of the match: names in brackets and quotes, non-negative indexes
var match = JsonPath.Parse("$.teams.*.members[-1]").Evaluate(document)[0];
Assert.Equal("$['teams']['red team']['members'][1]", match.Path);

// A normalized path is itself a query that selects the same node
Assert.Equal("Bob", JsonPath.Parse(match.Path).EvaluateValue(document)?.GetValue<string>());
```
<!-- endref -->

- Names are always in single quotes, with `'` and `\` escaped by a backslash and control characters escaped as in
  JSON (`\n`, `\u0001`).
- Indexes are never negative: `[-1]` becomes the actual index.
- The path is built the first time you read it, so an evaluation whose paths you never read does not pay for them.

Two matches with the same path are the same node, which happens when several selectors select it.

## Change the matched values

With `JsonNode`, the matched values are the nodes of the document, not copies. Change them to change the document:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_ModifyMatches;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"items":[{"price":8},{"price":12}]}""");

// With JsonNode, the matched values are the nodes of the document, not copies
foreach (var match in JsonPath.Parse("$.items[?@.price > 10]").Evaluate(document))
{
    match.Value!["discounted"] = true;
}

Assert.Equal("""{"items":[{"price":8},{"price":12,"discounted":true}]}""", document!.ToJsonString());
```
<!-- endref -->

The result is computed before [Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate) returns, so changing the document while you enumerate it does not change the
matches.

## Set, add and remove values by path

[Set](cref:Meziantou.Framework.Json.JsonPath.Set), [Add](cref:Meziantou.Framework.Json.JsonPath.Add) and
[Remove](cref:Meziantou.Framework.Json.JsonPath.Remove) change a `JsonNode` at the locations a query selects, and return
how many locations they changed:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_ChangeValuesByPath;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    {
      "logging": { "level": "info" },
      "users": [
        { "name": "Alice", "password": "secret", "roles": [] },
        { "name": "Bob", "password": "hunter2", "roles": ["admin"] }
      ]
    }
    """);

// Set replaces the selected values. The objects missing on a path of names are created.
JsonPath.Parse("$.logging.level").Set(document, "debug");
JsonPath.Parse("$.logging.file.path").Set(document, "app.log");

// A query that ends with a name sets the member on every selected object
JsonPath.Parse("$.users[*].active").Set(document, true);

// Add appends a value to the selected arrays
JsonPath.Parse("$.users[?@.name == 'Alice'].roles").Add(document, "reader");

// Remove deletes the selected values, and returns how many it removed
int removed = JsonPath.Parse("$..password").Remove(document);
Assert.Equal(2, removed);

Assert.Equal(
    """{"logging":{"level":"debug","file":{"path":"app.log"}},"users":[{"name":"Alice","roles":["reader"],"active":true},{"name":"Bob","roles":["admin"],"active":true}]}""",
    document!.ToJsonString());
```
<!-- endref -->

- [Set](cref:Meziantou.Framework.Json.JsonPath.Set) replaces every selected value. When the query ends with a name, such as `$.users[*].active`, it sets the
  member on every object that the rest of the query selects, whether the member exists or not. When the query is a
  path of names and indexes, such as `$.logging.file.path`, it also creates the objects missing on the path. It never
  creates an array element: an index out of range changes nothing.
- [Add](cref:Meziantou.Framework.Json.JsonPath.Add) appends the value to every selected array, and ignores the selected values that are not arrays.
- [Remove](cref:Meziantou.Framework.Json.JsonPath.Remove) removes every selected value from its object or array.
- Each location receives a copy of the value, so you can pass the same value again, and the value never becomes part
  of the document. `JsonNode` converts strings, numbers and Booleans implicitly, so `Set(document, "debug")` works.
- The query is evaluated before anything changes: `$[0,1]` removes the first two elements, not the first and the
  third. The query runs in lax mode, so a missing member selects nothing.
- The root value has no parent: [Set](cref:Meziantou.Framework.Json.JsonPath.Set) and [Remove](cref:Meziantou.Framework.Json.JsonPath.Remove) throw an `InvalidOperationException` for the query `$`. Replace the
  root yourself instead.
- A `JsonValue` that wraps a CLR object or array cannot change: the values inside it are detached copies. Changing
  them throws an `InvalidOperationException`; replacing the whole `JsonValue` works.

## Read a document without loading it

[EvaluateStream](cref:Meziantou.Framework.Json.JsonPath.EvaluateStream) and
[EvaluateStreamAsync](cref:Meziantou.Framework.Json.JsonPath.EvaluateStreamAsync) read a document from a `Stream` once,
from start to end, and return the matches as they find them:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluateStream;format=md-fenced-code-block;dedent=true -->
```csharp
// Any stream, such as a file or the body of an HTTP response
using var stream = new MemoryStream("""{"orders":[{"id":1,"total":8},{"id":2,"total":120},{"id":3,"total":40}]}"""u8.ToArray());

// Only the selected values, and the values a filter tests, are parsed: the document is never loaded in memory
var ids = new List<int>();
foreach (var match in JsonPath.Parse("$.orders[?@.total > 10].id").EvaluateStream(stream))
{
    ids.Add(match.Value!.GetValue<int>());
}

Assert.Equal([2, 3], ids);
```
<!-- endref -->

[Evaluate(ref Utf8JsonReader)](cref:Meziantou.Framework.Json.JsonPath.Evaluate) does the same on a reader that holds the whole value, such as a reader over a
buffer or a value inside a larger document:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluateReader;format=md-fenced-code-block;dedent=true -->
```csharp
var reader = new Utf8JsonReader("""{"store":{"book":[{"title":"A"},{"title":"B"}]}}"""u8);

JsonPathResult result = JsonPath.Parse("$.store.book[*].title").Evaluate(ref reader);
Assert.Equal(["$['store']['book'][0]['title']", "$['store']['book'][1]['title']"], result.Select(match => match.Path));
```
<!-- endref -->

- Only the selected values are parsed into `JsonNode` instances. The other values are read and skipped, so the memory
  the evaluation needs depends on the size of the selected values, not on the size of the document.
- A filter tests each candidate value, which is then parsed: `$.orders[?@.total > 10]` parses the orders one at a
  time. Negative indexes and some slices, such as `[-1]` and `[::-1]`, need the length of the array, which is then
  parsed whole. A filter that uses `$` needs the root value, so the whole document is parsed.
- The matches are the ones of [Evaluate](cref:Meziantou.Framework.Json.JsonPath.Evaluate), in document order rather than in the order of the selectors: `$['b','a']`
  returns the member `a` first when it comes first in the document. A value that several selectors select is
  returned as many times.
- The enumeration reads the stream as it goes: stop it early, with `First()` or `break`, to read only the start of the
  document. Invalid JSON throws a `JsonException` when the enumeration reaches it.
- Pass `JsonReaderOptions` to allow comments or trailing commas, or to raise the maximum depth. The stream is not
  disposed. A UTF-8 byte order mark is skipped. A `Utf8JsonReader` must skip comments rather than return them
  (`JsonCommentHandling.Skip`), as for `JsonNode.Parse`.
- The streaming methods always use lax mode, and do not take [JsonPathEvaluationOptions](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions). Pass a cancellation token to
  [EvaluateStreamAsync](cref:Meziantou.Framework.Json.JsonPath.EvaluateStreamAsync), or stop the enumeration, to end an evaluation early.
