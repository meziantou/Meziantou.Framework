---
title: Limitations
description: What the library does not support, the depth limits, and how numbers are compared.
---

The library implements RFC 9535 with opt-in typed function extensions. This page lists what that leaves out, the limits that protect your
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
- **Only five functions by default.** Domain-specific functions require an explicit
  [custom function registry](05-Functions.md#register-custom-functions).

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

- **Changes through paths need a `JsonNode`.** [Set](cref:Meziantou.Framework.Json.JsonPath.Set), [Add](cref:Meziantou.Framework.Json.JsonPath.Add) and [Remove](cref:Meziantou.Framework.Json.JsonPath.Remove) change a `JsonNode`; `JsonDocument`,
  `JsonElement` and custom object models are read-only. See
  [Set, add and remove values by path](06-EvaluatingQueries.md#set-add-and-remove-values-by-path).
- **Streaming is lax and returns the document order.** The evaluation of a stream or a `Utf8JsonReader` has no strict
  mode and no [JsonPathEvaluationOptions](cref:Meziantou.Framework.Json.JsonPathEvaluationOptions), and some queries parse more of the document than others. See
  [Read a document without loading it](06-EvaluatingQueries.md#read-a-document-without-loading-it).
- **Regular expressions are I-Regexp only.** `match()` and `search()` do not accept .NET-specific syntax, and `^` and
  `$` are not anchors. See [Write the patterns](05-Functions.md#write-the-patterns).

## Limits

For application-selected cancellation, work, final-match and intermediate-result limits, see
[Cancel or bound evaluation](06-EvaluatingQueries.md#cancel-or-bound-evaluation). These limits throw instead of
silently truncating results and do not replace the structural limits below.

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

Filters compare numbers, the literals of the query and the values of the document, by their exact value. Integers
beyond 2^53, decimals with more digits than a `double` holds, and numbers beyond the range of a `double` all compare
exactly:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_ExactNumbers;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"ids":[9007199254740992,9007199254740993],"amounts":[0.1,0.10000000000000001]}""");

// Numbers that round to the same double still compare by their exact value
Assert.Equal("[9007199254740993]", JsonPath.Parse("$.ids[?@ == 9007199254740993]").Evaluate(document).ToJsonArray().ToJsonString());
Assert.Equal("[0.10000000000000001]", JsonPath.Parse("$.amounts[?@ > 0.1]").Evaluate(document).ToJsonArray().ToJsonString());
```
<!-- endref -->

- Numbers are compared as `double` values first. Only the numbers that round to the same `double` are compared by
  their text, so the exact comparison costs nothing for most values.
- `JsonNode` values created from CLR numbers compare by their value: a `long` or a `decimal` exactly, a `float` as the
  `double` it converts to.
- The numbers that functions return, such as `length()` and `count()`, are exact. A custom function returns a
  `double`, which is compared exactly as it is.
- A custom navigator gives the exact text of its numbers by overriding
  [TryGetNumberText](cref:Meziantou.Framework.Json.JsonPathNavigator{TValue}.TryGetNumberText). Without it, its numbers
  compare as `double` values, and distinct numbers that round to the same `double` compare as equal. See
  [Custom object models](08-CustomObjectModels.md#rules-for-a-navigator).
