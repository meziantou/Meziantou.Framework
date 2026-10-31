---
title: Functions
description: Measure values with length() and count(), test strings with match() and search(), and compare with value().
---

Filters can call the five functions of RFC 9535:

| Function | Argument | Result |
| --- | --- | --- |
| `length(value)` | A value | The number of characters of a string, of elements of an array, of members of an object |
| `count(query)` | A query | The number of nodes the query selects |
| `match(value, pattern)` | A string and a regular expression | True when the whole string matches |
| `search(value, pattern)` | A string and a regular expression | True when a part of the string matches |
| `value(query)` | A query | The value of the only node the query selects |

Functions are only available in filters. There is no other function and no way to add one: [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) rejects an unknown
function.

## Measure strings, arrays and objects

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FunctionLengthAndCount;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    [
      { "name": "Al", "tags": ["a", "b", "c"], "address": { "city": "Paris", "zip": "75001" } },
      { "name": "Alice", "tags": ["a"], "address": { "city": "Lyon" } }
    ]
    """);
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// length(): the number of characters of a string, of elements of an array, of members of an object
Assert.Equal("""["Alice"]""", Select("$[?length(@.name) > 3].name"));
Assert.Equal("""["Al"]""", Select("$[?length(@.tags) >= 2].name"));
Assert.Equal("""["Al"]""", Select("$[?length(@.address) == 2].name"));

// count(): the number of nodes a query selects
Assert.Equal("""["Al"]""", Select("$[?count(@.tags[?@ == 'a' || @ == 'b']) == 2].name"));
```
<!-- endref -->

- `length()` takes a single value: a literal, a singular query such as `@.name`, or another function. On a number, a
  Boolean or `null`, and on a query that selects nothing, it has no value, and a comparison with it is false.
- `length()` counts the characters of a string as Unicode code points: an emoji outside the Basic Multilingual Plane
  counts as one character, not two as in `string.Length`.
- `count()` takes any query, including wildcards, filters and descendant segments.
- `count()` counts final nodes without retaining a final match list and counts duplicate selections separately.
  A root-only query such as `count($[*])` is reused within one evaluation, not across documents or calls.
  Predicates inside that absolute query have their own candidate: `count($.items[?@.enabled])` is still root-only.
  Candidate-dependent queries such as `count(@.items[*])` are evaluated for each candidate.
  Built-in navigators permit this reuse; custom navigators must opt in with
  [IsPure](cref:Meziantou.Framework.Json.JsonPathNavigator{TValue}.IsPure), promising side-effect-free navigation and
  values that remain stable throughout the evaluation.

## Test strings with a regular expression

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FunctionMatchAndSearch;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""[{ "code": "AB-12" }, { "code": "AB-123" }, { "code": "XAB-1" }]""");
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// match(): the whole string matches the pattern
Assert.Equal("""["AB-12"]""", Select("$[?match(@.code, 'AB-[0-9]{2}')].code"));

// search(): the pattern matches a part of the string
Assert.Equal("""["AB-12","AB-123","XAB-1"]""", Select("$[?search(@.code, 'AB-[0-9]')].code"));
```
<!-- endref -->

- `match()` is true when the pattern matches the whole string; `search()` when it matches a part of it.
- When the first argument is not a string, both functions are false.
- The pattern can come from the document: `match(@.code, $.codePattern)`.

### Write the patterns

The patterns are [I-Regexp (RFC 9485)](https://www.rfc-editor.org/rfc/rfc9485) regular expressions, a small subset that
behaves the same in every language. They differ from .NET regular expressions:

- Supported: literal characters, `.`, character classes (`[a-z]`, `[^0-9]`), Unicode categories (`\p{Lu}`, `\P{L}`),
  escapes of special characters (`\.`, `\-`) and of `\n`, `\r` and `\t`, alternation (`|`), groups (`(...)`) and the
  quantifiers `*`, `+`, `?`, `{n}`, `{n,}` and `{n,m}`.
- Not supported: `\d`, `\w`, `\s` and the other shorthand classes (write `[0-9]`), Unicode blocks (`\p{IsBasicLatin}`),
  character class subtraction, non-capturing groups (`(?:...)`), lazy quantifiers (`*?`), backreferences, lookarounds
  and inline options.
- `^` and `$` are ordinary characters, not anchors: `match()` is already anchored, and `search()` is not.
- `.` matches any character except a line feed and a carriage return.

A pattern that is not a valid I-Regexp does not throw: the function is false for every value.

## Use the value of a query

Comparisons only accept queries that select at most one node by construction. `value()` lifts that restriction: it
takes any query and returns the value of the node when the query selects exactly one:

<!-- ref:../../tests/Meziantou.Framework.JsonPath.Tests/JsonPathEvaluateTests.cs;region=JsonPath_FunctionValue;format=md-fenced-code-block;dedent=true -->
```csharp
var document = JsonNode.Parse("""
    [
      { "name": "A", "labels": [{ "key": "env", "value": "prod" }] },
      { "name": "B", "labels": [{ "key": "env", "value": "test" }, { "key": "team", "value": "web" }] }
    ]
    """);
string Select(string query) => JsonPath.Parse(query).Evaluate(document).ToJsonArray().ToJsonString();

// value() turns a query that selects one node into a value that can be compared
Assert.Equal("""["A"]""", Select("$[?value(@.labels[?@.key == 'env'].value) == 'prod'].name"));

// When the query selects no node or several nodes, value() has no value: B has two labels
Assert.Equal("""["A"]""", Select("$[?value(@.labels[*].key) == 'env'].name"));
```
<!-- endref -->

When the query selects no node or more than one, `value()` has no value and the comparison is false.

## Type rules

RFC 9535 gives each function argument and result a type, and [Parse](cref:Meziantou.Framework.Json.JsonPath.Parse) checks them:

- `length()`, `match()` and `search()` take values: a literal, a singular query or a function that returns a value.
  `length(@.*)` is invalid; write `count(@.*)` or `length(value(@.*))`.
- `count()` and `value()` take a query.
- `length()`, `count()` and `value()` return a value: compare it (`count(@.tags[*]) > 1`); it cannot be a test on its
  own.
- `match()` and `search()` return a test: use them on their own or with `&&`, `||` and `!`; they cannot be compared.

See [Invalid queries](07-ErrorsAndEvaluationModes.md#invalid-queries) for examples.
