---
title: Limitations
description: What the library does not support, the depth limits, and how numbers are compared.
---

The library implements RFC 9535 and nothing else. This page lists what that leaves out, the limits that protect your
application from hostile input, and the precision of number comparisons.

## Other JSONPath dialects

Before RFC 9535, JSONPath was an informal syntax with many variants. Their extensions are not supported, and [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse)
rejects most of them:

- **No script expressions**: `$.book[(@.length-1)]` is invalid. Write `$.book[-1]`.
- **No `.length` property**: `$.books.length` is valid but selects a member named `length`. Count the matches instead,
  or use `length()` in a filter.
- **No other operators**: no `=~`, `in`, `nin`, `contains` or regular expression literals (`/.../`). Use
  [`match()` and `search()`](05-Functions.md#test-strings-with-a-regular-expression).
- **No parent or property name operators** (`^`, `~`), and no filters evaluated as JavaScript or C# code: filters use
  the standard operators and functions only.
- **No other functions** than the five of RFC 9535, and no way to register one.

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_LengthIsAMemberName;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"books":[{"title":"A"},{"title":"B"}]}""");

// .length selects a member named "length", not the length of the array
Assert.Empty(JsonPath.Parse("$.books.length").Evaluate(document));

// Count the matches instead
Assert.HasCount(2, JsonPath.Parse("$.books[*]").Evaluate(document));
```
<!-- endref -->

Filters written as `[?(@.price < 10)]`, with parentheses, are valid RFC 9535 and work unchanged.

## Other features

- **No changes through paths.** There is no method to set, add or remove values by path. With `JsonNode`, change the
  matched nodes yourself; see [Change the matched values](06-EvaluatingQueries.md#change-the-matched-values).
- **No streaming.** The query runs on a parsed document (`JsonNode`, `JsonDocument`, `JsonElement`) or on your own
  model, not on a `Utf8JsonReader` or a stream.
- **Regular expressions are I-Regexp only.** `match()` and `search()` do not accept .NET-specific syntax, and `^` and
  `$` are not anchors. See [Write the patterns](05-Functions.md#write-the-patterns).

## Limits

- **Queries**: filter expressions, nested filter selectors and function arguments nest up to 64 levels deep. Beyond
  that, [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) throws a `FormatException` and [TryParse](cref:Meziantou.Framework.Json.JsonPath.TryParse) returns `false`, so a hostile or generated query cannot
  exhaust the stack.
- **Documents**: descendant segments (`..`) and comparisons of arrays and objects visit at most 256 levels of nesting.
  Beyond that, evaluation throws a [JsonPathEvaluationException](cref:Meziantou.Framework.Json.JsonPathEvaluationException), in both evaluation modes. Documents parsed by
  `System.Text.Json` with its default `MaxDepth` of 64 never reach this limit; it only affects values built in code,
  parsed with a higher `MaxDepth`, or exposed by a [custom navigator](08-CustomObjectModels.md).
- **Indexes and slices**: the numbers of index and slice selectors must be between -(2^53)+1 and 2^53-1, the
  [I-JSON](https://www.rfc-editor.org/rfc/rfc7493#section-2.2) range; [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) rejects larger ones.

## Number precision

Filters compare numbers, the literals of the query and the values of the document, as `double` values. Every integer
between -(2^53)+1 and 2^53-1 and every number that a `double` represents exactly compare exactly. Outside that range,
distinct numbers can compare as equal:

- Integers beyond ±(2^53)-1 are rounded: `$[?@ == 9007199254740993]` matches `9007199254740992`.
- Decimals are rounded to the nearest `double`: `$[?@ == 0.1]` matches `0.10000000000000001`.
- Numbers too large for a `double` become infinity: `$[?@ == 1e400]` matches `1e500`.
- Numbers too small for a `double` become zero: `$[?@ < 0]` does not match `-1e-400`.

When exact values matter, such as identifiers or amounts of money, store them as strings in the document, or compare
them in your code after the query.
