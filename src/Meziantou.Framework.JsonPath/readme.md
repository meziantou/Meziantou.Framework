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

`Parse` rejects every query that is not well-formed and valid, as RFC 9535 §2.1 requires, including queries that
only look valid: `!@.a == 1` (negate the comparison with `!(@.a == 1)`) or `@[ 'a' ] == 1` (blank space inside the
brackets makes a query non-singular, so it cannot be compared). The library is tested against the
[JSONPath Compliance Test Suite](https://github.com/jsonpath-standard/jsonpath-compliance-test-suite).

### Regular expressions

`match()` and `search()` take an [I-Regexp (RFC 9485)](https://www.rfc-editor.org/rfc/rfc9485) pattern. The
implementation is a checking one (RFC 9485 §3.1): a pattern that is not an I-Regexp, such as one using `\d`, `(?:...)`,
a lazy quantifier or a backreference, makes the function return `false` instead of being handed to .NET's own regular
expression engine. Patterns follow the XSD semantics RFC 9485 §4 prescribes: `^` and `$` are ordinary characters, `.`
matches any character except line feed and carriage return, and a character outside the Basic Multilingual Plane
counts as one character. Two cases of the compliance test suite expect `^` and `$` to be anchors, following the
non-normative mapping of RFC 9485 §5.3; this library follows §4 instead.

## Limits

### Parsing

Filter expressions, nested filter selectors, and function arguments may nest up to 64 levels deep. Beyond that,
`Parse` throws a `FormatException` and `TryParse` returns `false`. The parser is recursive, so this bound is what
keeps a hostile or machine-generated expression from exhausting the stack; 64 matches the default `MaxDepth` of
`System.Text.Json` and is far above any practical query.

### Evaluation

Descendant segments (`..`) and deep equality comparisons recurse, so evaluation visits at most 256 levels of
nesting; beyond that it throws `JsonPathEvaluationException` in both evaluation modes. The limit is higher than the
parser's because documents can legitimately be deeper than expressions: values produced by `System.Text.Json`'s own
parsers cannot exceed their default `MaxDepth` of 64, so this only affects values built programmatically, parsed
with a raised `MaxDepth`, or exposed by a custom navigator.

A custom `JsonPathNavigator<TValue>` should expose an acyclic view of its object model. A cycle — a parent
back-reference, for example — is reported as this same depth error rather than recursing forever.
