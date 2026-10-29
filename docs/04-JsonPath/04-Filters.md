---
title: Filters
description: Keep the elements and members that pass a test, with comparisons, logical operators and existence tests.
---

A filter selector, `[?test]`, keeps the children of a node for which the test is true: the elements of an array, the
member values of an object. In the test, `@` is the child being tested and `$` is the root of the document.

## Compare values

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FilterComparisons;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    {
      "books": [
        { "title": "A", "price": 8, "category": "fiction" },
        { "title": "B", "price": 12, "category": "science" },
        { "title": "C", "price": 25, "category": "fiction" }
      ]
    }
    """);
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// @ is the node being tested: here, each book
Assert.Equal("""["A","B"]""", Select("$.books[?@.price < 20].title"));
Assert.Equal("""["A","C"]""", Select("$.books[?@.category == 'fiction'].title"));

// && (and), || (or), ! (not) and parentheses combine the tests
Assert.Equal("""["A"]""", Select("$.books[?@.category == 'fiction' && @.price < 20].title"));
Assert.Equal("""["B"]""", Select("$.books[?!(@.category == 'fiction')].title"));

// Parentheses around the whole test are allowed, as in other JSONPath dialects
Assert.Equal("""["A","B"]""", Select("$.books[?(@.price < 20)].title"));
```
<!-- endref -->

- The comparison operators are `==`, `!=`, `<`, `<=`, `>` and `>=`.
- The literals are numbers (`10`, `-1.5`, `1e3`), strings in single or double quotes, `true`, `false` and `null`.
  There are no array or object literals: compare with a query instead.
- The logical operators are `&&`, `||` and `!`. `!` applies to what follows it, so `!@.a == 1` is invalid: write
  `!(@.a == 1)`.

## Test whether a member exists

A query on its own, without a comparison, is true when it selects at least one node:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FilterExistence;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    [
      { "name": "A", "enabled": true },
      { "name": "B", "enabled": false },
      { "name": "C" }
    ]
    """);
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// A query alone tests whether the member exists, whatever its value
Assert.Equal("""["A","B"]""", Select("$[?@.enabled].name"));
Assert.Equal("""["C"]""", Select("$[?!@.enabled].name"));

// Compare with true to test the value
Assert.Equal("""["A"]""", Select("$[?@.enabled == true].name"));
```
<!-- endref -->

`[?@.enabled]` keeps the element whose `enabled` member is `false`: the test checks that the member exists, not that its
value is true. Compare with `true` to test the value.

## Refer to the root, or filter inside a filter

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FilterRootAndNested;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    {
      "maxPrice": 10,
      "orders": [
        { "id": 1, "lines": [{ "price": 4 }, { "price": 15 }] },
        { "id": 2, "lines": [{ "price": 9 }] }
      ]
    }
    """);
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// $ refers to the root of the document from inside a filter
Assert.Equal("[4,9]", Select("$.orders[*].lines[?@.price <= $.maxPrice].price"));

// A filter inside a filter: the orders that have at least one line above 10
Assert.Equal("[1]", Select("$.orders[?@.lines[?@.price > 10]].id"));
```
<!-- endref -->

- `$` in a test is the root of the document, so a query can compare with a value stored elsewhere in the document.
- A query in a test can contain any segment, including another filter or a descendant segment (`@..price`).

## Filter the members of an object

On an object, the filter tests the value of each member and keeps the values that pass:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FilterObjectMembers;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""{"users":{"alice":{"age":31},"bob":{"age":25}}}""");

// On an object, a filter tests the value of every member
var result = JsonPath.Parse("$.users[?@.age > 30]").Evaluate(document);
Assert.Equal(["$['users']['alice']"], result.Select(match => match.Path));
```
<!-- endref -->

The result is the member value (`{"age":31}`), not the member name. Its path, `$['users']['alice']`, gives the name.

## How values compare

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_ComparisonRules;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    [
      { "id": 1, "value": 1 },
      { "id": 2, "value": 1.0 },
      { "id": 3, "value": "1" },
      { "id": 4, "value": [1, 2] },
      { "id": 5 }
    ]
    """);
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// Numbers compare by value; values of different types are never equal
Assert.Equal("[1,2]", Select("$[?@.value == 1].id"));

// Arrays and objects compare deeply: here, with the value of the fourth element
Assert.Equal("[4]", Select("$[?@.value == $[3].value].id"));

// A missing member is only equal to another missing member, and is not less or greater than anything
Assert.Equal("[5]", Select("$[?@.value == @.missing].id"));
Assert.Equal("[]", Select("$[?@.value < @.missing || @.value > @.missing].id"));
```
<!-- endref -->

- **Values of different types are never equal**: the string `"1"` is not equal to the number `1`, and `null` is only
  equal to `null`.
- **Numbers compare by value**: `1`, `1.0` and `1e0` are equal. They are compared as `double` values; see
  [Number precision](09-Limitations.md#number-precision).
- **Strings compare by Unicode code point**, without culture rules: `"B" < "a"` is true, `"a" < "B"` is false.
- **Arrays and objects compare deeply**: equal when they have the same elements in the same order, or the same members
  with equal values in any order.
- **Only numbers and strings are ordered**: `<`, `<=`, `>` and `>=` are false for Booleans, `null`, arrays, objects and
  values of different types. `<=` is "less than or equal", so `null <= null` is true.
- **A query that selects nothing compares only with another query that selects nothing**: both sides are then equal,
  and neither is less than the other.

## Rules for the queries in a test

A query compared with `==` or `<` must be a *singular query*: one that selects at most one node, made of names and
indexes only, such as `@.price`, `@['first name']` or `$.limits[0]`. A wildcard, a slice, a filter or `..` makes it
non-singular, and `Parse` rejects the comparison. To compare one value selected by such a query, use
[`value()`](05-Functions.md#use-the-value-of-a-query).

Inside the brackets of a singular query, write no blank space: `@['a'] == 1` is valid, `@[ 'a' ] == 1` is not.
