# Meziantou.Framework.JsonPath

An implementation of [JSONPath (RFC 9535)](https://datatracker.ietf.org/doc/html/rfc9535) for `System.Text.Json` and custom object models.

## Usage

```csharp
using System.Text.Json.Nodes;
using Meziantou.Framework;

var document = JsonNode.Parse("""{"store":{"book":[{"title":"A"},{"title":"B"}]}}""");

// Parse a JSONPath expression (can be reused)
var path = JsonPath.Parse("$.store.book[*].title");

// Evaluate against a document
var result = path.Evaluate(document);
foreach (var match in result)
{
    Console.WriteLine($"{match.Path}: {match.Value}");
    // $['store']['book'][0]['title']: A
    // $['store']['book'][1]['title']: B
}
```

## Evaluation modes

`Evaluate` supports two modes:

- `JsonPathEvaluationMode.Lax` (default): path evaluation errors produce no match.
- `JsonPathEvaluationMode.Strict`: path evaluation errors throw `JsonPathEvaluationException`.

```csharp
var doc = JsonNode.Parse("""{"a": 1}""");
var path = JsonPath.Parse("$.name");

var laxValue = path.EvaluateValue(doc, JsonPathEvaluationMode.Lax); // null

var strictValue = path.EvaluateValue(doc, JsonPathEvaluationMode.Strict); // throws JsonPathEvaluationException
```

## Custom object models

Use `JsonPathNavigator<TValue>` to evaluate JSONPath expressions against a custom tree without converting it to `JsonNode`.

```csharp
var path = JsonPath.Parse("$.items[?@.enabled == true]");
var result = path.Evaluate(root: myRoot, navigator: MyNodeNavigator.Instance);

foreach (var match in result)
{
    MyNode? node = match.Value;
    Console.WriteLine(match.Path);
}
```

Navigator implementations expose JSON-like semantics for the custom node type. A `null` node represents JSON `null`; a `false` return value from `TryGetPropertyValue` or `TryGetElement` means the member or element is missing. Arrays are zero-based, and object property order follows the navigator's `GetProperties` enumeration order.

## Supported Features

Full RFC 9535 compliance:

- **Selectors**: name (`.name`, `['name']`), wildcard (`*`), index (`[0]`, `[-1]`), slice (`[0:3:1]`), filter (`[?@.price < 10]`)
- **Segments**: child and descendant (`..`)
- **Filter expressions**: comparisons (`==`, `!=`, `<`, `<=`, `>`, `>=`), logical operators (`&&`, `||`, `!`), existence tests, parenthesized grouping
- **Built-in functions**: `length()`, `count()`, `match()`, `search()`, `value()`
- **Normalized paths**: canonical path output per RFC 9535 §2.7
