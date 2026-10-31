# Meziantou.Framework.JsonPath

An implementation of [JSONPath (RFC 9535)](https://datatracker.ietf.org/doc/html/rfc9535) for `System.Text.Json` and custom object models.

## Usage

The types are in the `Meziantou.Framework.Json` namespace; `JsonNode` is in `System.Text.Json.Nodes`.

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Evaluation modes

`Evaluate` supports two modes:

- `JsonPathEvaluationMode.Lax` (default): path evaluation errors produce no match.
- `JsonPathEvaluationMode.Strict`: path evaluation errors throw `JsonPathEvaluationException`.

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluationModes;format=md-fenced-code-block;dedent=true -->
```csharp
var doc = JsonNode.Parse("""{"a": 1}""");
var path = JsonPath.Parse("$.name");

var laxValue = path.EvaluateValue(doc, JsonPathEvaluationMode.Lax);
Assert.Null(laxValue);

Assert.Throws<JsonPathEvaluationException>(() => path.EvaluateValue(doc, JsonPathEvaluationMode.Strict));
```
<!-- endref -->

## Custom filter functions

Derive from `JsonPathFunction` with an RFC 9535 signature and a callback that works with any navigator:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_IsValidFunction;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class IsValidFunction() : JsonPathFunction("isvalid", JsonPathFunctionType.Logical, JsonPathFunctionType.Value)
{
    public override JsonPathFunctionArgument<TValue> Invoke<TValue>(
        IReadOnlyList<JsonPathFunctionArgument<TValue>> arguments, JsonPathNavigator<TValue> navigator)
    {
        var valid = arguments[0].TryGetString(navigator, out var code) && code is "valid";
        return JsonPathFunctionArgument<TValue>.FromLogical(valid);
    }
}
```
<!-- endref -->

Opt in at parse time with an immutable registry. The default five built-ins are unchanged and cannot be overridden:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_CustomFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
var functions = JsonPathFunctionRegistry.Default.WithFunction(new IsValidFunction());
var path = JsonPath.Parse("$[?isvalid(@.code)].code", functions);
var document = JsonNode.Parse("""[{"code":"valid"},{"code":"invalid"},{},{"code":null}]""");
Assert.Equal("""["valid"]""", path.Evaluate(document).ToJsonArray().ToJsonString());
```
<!-- endref -->

Arguments and results distinguish ValueType (including JSON null and Nothing), LogicalType and NodesType.
Native nodes retain their object model and lifetime. Function implementations must be thread-safe, should be pure,
and must not mutate input nodes. Custom results are not cached; short-circuiting may skip calls.
Callback exceptions propagate unchanged. See [custom functions](../../docs/JsonPath/05-Functions.md#register-custom-functions)
for the conversions and callback safety rules.

## Custom object models

Use `JsonPathNavigator<TValue>` to evaluate JSONPath expressions against a custom tree without converting it to `JsonNode`.

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_CustomNavigator;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

Here, `MyNode` is a small object model and `MyNodeNavigator` exposes it to JSONPath:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_CustomNavigatorTypes;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

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

### Number precision

Filter expressions compare numbers, both the literals in the query and the values in the document, as IEEE 754
double-precision values (`JsonPathNavigator<TValue>.TryGetNumber` returns a `double`). Numbers that a `double`
represents exactly compare exactly. This covers every integer from -(2^53)+1 to 2^53-1, the range I-JSON
([RFC 7493 §2.2](https://www.rfc-editor.org/rfc/rfc7493#section-2.2)) recommends for interoperability. Outside it,
distinct numbers can compare as equal:

- Integers beyond ±(2^53)-1 are rounded: `$[?@ == 9007199254740993]` matches `9007199254740992`.
- Decimals are rounded to the nearest `double`: `$[?@ == 0.1]` matches `0.10000000000000001`.
- Magnitudes beyond the `double` range become infinity: `$[?@ == 1e400]` matches `1e500`.
- Magnitudes below the smallest `double` become zero: `$[?@ < 0]` does not match `-1e-400`.

Index and slice selectors are not affected: the parser rejects any value outside the I-JSON integer range, as
RFC 9535 §2.1 requires.
