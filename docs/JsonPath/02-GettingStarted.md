---
title: Getting started
description: Install the package, parse a query and evaluate it on a JSON document.
---

## Install the package

Add the `Meziantou.Framework.JsonPath` package to a project:

```bash
dotnet add package Meziantou.Framework.JsonPath
```

The examples of this documentation omit the `using` directives. They need `using Meziantou.Framework.Json;`,
`using System.Text.Json;` and `using System.Text.Json.Nodes;`.

## Run your first query

Parse the query once with `JsonPath.Parse`, then evaluate it on a document:

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

- A query starts with `$`, the root of the document. `.store` selects the `store` member, `.book` its `book` member,
  `[*]` every element of the array and `.title` the `title` member of each element.
- `Evaluate` returns every match, in document order. Each match has its `Value` and its `Path`, the normalized path
  that leads to it.
- `JsonPath` is immutable and thread-safe: store it in a field and reuse it.

## Get a single value

When you expect one value, `EvaluateValue` returns the first match, or `null` when nothing matches:

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

## Filter the values

A filter selector, `[?...]`, keeps the elements for which a test is true. `@` is the element being tested:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_EvaluateJsonDocument;format=md-fenced-code-block;dedent=true -->
```csharp
using var document = JsonDocument.Parse("""{"store":{"book":[{"title":"A","price":8},{"title":"B","price":12}]}}""");
var path = JsonPath.Parse("$.store.book[?@.price < 10].title");

// Evaluate works on a JsonDocument and on any JsonElement, as an instance or an extension method
JsonPathResult<JsonElement> result = path.Evaluate(document);
Assert.Single(result);
Assert.Equal("A", result[0].Value.GetString());
Assert.Equal("$['store']['book'][0]['title']", result[0].Path);

JsonElement? title = document.RootElement.EvaluateValue(path);
Assert.Equal("A", title?.GetString());
```
<!-- endref -->

This example also shows that `JsonDocument` and `JsonElement` work like `JsonNode`. The extension methods
(`document.Evaluate(path)`, `element.EvaluateValue(path)`) do the same as the methods of `JsonPath`.

## Next steps

- [Query syntax](03-QuerySyntax.md): names, indexes, slices, wildcards and descendants.
- [Filters](04-Filters.md) and [Functions](05-Functions.md): select values by their content.
- [Evaluating queries](06-EvaluatingQueries.md): the results, the paths and the JSON types.
