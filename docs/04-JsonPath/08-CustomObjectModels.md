---
title: Custom object models
description: Evaluate JSONPath queries on your own tree of values by implementing a JsonPathNavigator.
---

To query a tree that is not a `System.Text.Json` type, such as the model of a YAML or TOML parser or your own
configuration objects, implement a `JsonPathNavigator<TValue>`. The query runs on your values directly: there is no
conversion to JSON first.

## Evaluate a query on your model

Pass the root value and the navigator to `Evaluate` or `EvaluateValue`:

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

The result is a `JsonPathResult<TValue>`: each match has the `Value` of your type and its normalized `Path`. Every
part of the syntax works, filters and functions included, and the evaluation modes apply as for JSON.

## Write the navigator

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

A navigator answers the questions the evaluator asks about a value:

| Method | Returns |
| --- | --- |
| `GetKind` | Whether the value is an object, an array, a string, a number, a Boolean or `null` (`JsonPathNodeKind`) |
| `GetProperties` | The members of an object, as `JsonPathProperty<TValue>` (name and value), in their order |
| `TryGetPropertyValue` | The value of one member of an object, and whether the member exists |
| `GetArrayLength` | The number of elements of an array |
| `TryGetElement` | The element at a zero-based index, and whether it exists |
| `TryGetString`, `TryGetNumber`, `TryGetBoolean` | The scalar value, and whether the value has that type |

## Rules for a navigator

- **Represent JSON `null` your way.** `null` or a dedicated value of your model can stand for JSON `null`: `GetKind`
  says which values are `null`. The matches then hold that value.
- **Return `false` for what does not exist.** `TryGetPropertyValue` and `TryGetElement` return `false` for a missing
  member or an index outside the array; this is what lax mode turns into "no match" and strict mode into an exception.
- **Keep the order stable.** The order of `GetProperties` is the order of the wildcard, descendant and filter results.
- **Numbers are `double`.** `TryGetNumber` returns a `double`, so filters compare your numbers as `double` values; see
  [Number precision](09-Limitations.md#number-precision).
- **Expose a tree, not a graph.** A cycle, such as a back-reference to the parent, is reported as a depth error
  (`JsonPathEvaluationException`) when a descendant segment or a comparison walks it, instead of looping forever.
- **Make `TryGetElement` fast.** The evaluator reads elements by index: back arrays with a list or an array.
