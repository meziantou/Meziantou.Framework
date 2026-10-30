---
title: Query syntax
description: Select values by member name, index, slice and wildcard, several at once, or at any depth.
---

A query starts with `$`, the root of the document, followed by segments. Each segment applies its selectors to every
node the previous segment selected:

| Query | Selects |
| --- | --- |
| `$.name` or `$['name']` | The `name` member of the root object |
| `$.*` or `$[*]` | Every member value of an object, every element of an array |
| `$[0]`, `$[-1]` | The first element, the last element |
| `$[1:3]`, `$[::-1]` | Elements 1 and 2, every element in reverse order |
| `$[0, 'name']` | Several selectors: their results one after the other |
| `$..name` | The `name` member of the root and of every node below it |
| `$[?@.price < 10]` | The elements that pass a test: see [Filters](04-Filters.md) |

The examples of this page use a small helper, `Select`, that returns the matches as a JSON array, like the examples
of RFC 9535. A selector that does not apply to a node, such as an index on an object, selects nothing; see
[Errors and evaluation modes](07-ErrorsAndEvaluationModes.md) to throw instead.

## Select a member by name

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_NameSelectors;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"name":"Alice","first name":"Al","address":{"city":"Paris"}}""");
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

Assert.Equal("""["Alice"]""", Select("$.name"));
Assert.Equal("""["Paris"]""", Select("$.address.city"));

// Bracket notation accepts any member name, in single or double quotes
Assert.Equal("""["Al"]""", Select("$['first name']"));
Assert.Equal("""["Paris"]""", Select("""$["address"]["city"]"""));

// A missing member selects nothing
Assert.Equal("[]", Select("$.phone"));
```
<!-- endref -->

- The dot notation, `.name`, accepts names made of letters, digits and `_` that do not start with a digit (non-ASCII
  letters are allowed too).
- The bracket notation, `['name']` or `["name"]`, accepts any name. Escape the quote and the backslash with a
  backslash: `['it\'s']`.
- Names are case-sensitive.

## Select every child

The wildcard, `*`, selects the value of every member of an object and every element of an array:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_WildcardSelector;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"sizes":{"small":1,"large":3},"tags":["a","b"],"count":2}""");
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// The values of the members of an object, or the elements of an array
Assert.Equal("[1,3]", Select("$.sizes.*"));
Assert.Equal("""["a","b"]""", Select("$.tags[*]"));

// A number, a string, a Boolean or null has no children
Assert.Equal("[]", Select("$.count.*"));
```
<!-- endref -->

Members are returned in the order of the object.

## Select array elements by index

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_IndexSelectors;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""["a","b","c","d"]""");
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

Assert.Equal("""["a"]""", Select("$[0]"));

// A negative index counts from the end of the array
Assert.Equal("""["d"]""", Select("$[-1]"));

// An index outside the array selects nothing
Assert.Equal("[]", Select("$[10]"));
```
<!-- endref -->

Indexes start at 0; `-1` is the last element, `-2` the one before it. An index on an object selects nothing.

## Select a range of elements

A slice, `[start:end:step]`, selects the elements from `start` (included) to `end` (excluded), every `step` elements:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_SliceSelectors;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""[0,1,2,3,4,5]""");
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// [start:end:step]; start is included, end is excluded
Assert.Equal("[1,2]", Select("$[1:3]"));
Assert.Equal("[0,1]", Select("$[:2]"));
Assert.Equal("[4,5]", Select("$[-2:]"));
Assert.Equal("[0,2,4]", Select("$[::2]"));

// A negative step walks the array backward
Assert.Equal("[5,4,3,2,1,0]", Select("$[::-1]"));
```
<!-- endref -->

- Each part is optional: `start` defaults to the first element, `end` to the end of the array, `step` to 1. With a
  negative step, the defaults are reversed: `[::-1]` walks the whole array backward.
- Negative `start` and `end` count from the end, like indexes.
- Bounds outside the array are clamped, so a slice never fails. A step of 0 selects nothing.

## Use several selectors at once

Separate selectors with commas inside the brackets. The result has the matches of the first selector, then those of
the second, and so on:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_MultipleSelectors;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"id":7,"name":"Alice","tags":["a","b","c","d"]}""");
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// The results follow the order of the selectors
Assert.Equal("""["Alice",7]""", Select("$['name','id']"));
Assert.Equal("""["d","a","b"]""", Select("$.tags[-1, 0:2]"));

// A node selected twice is returned twice
Assert.Equal("""["a","a"]""", Select("$.tags[0, 0]"));
```
<!-- endref -->

Any selector can be combined: names, indexes, slices, wildcards and filters.

## Search at any depth

The descendant segment, `..`, applies its selector to the node and to every node below it, at any depth:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_DescendantSegment;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

- `$..name`, `$..*`, `$..[0]` and `$..[?...]` are all valid: `..` takes any selector, or several in brackets.
- A parent comes before its children in the result.
- `..` visits the whole document below the node. On a large document, prefer a precise path when you know where the
  values are.

## Blank space

Spaces, tabs and line breaks are allowed between segments and around the operators of a filter
(`$ .store [?@.price < 10]`), but not at the start or the end of the query: [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) rejects `" $.a"`.
