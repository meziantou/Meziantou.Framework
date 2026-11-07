---
title: In-memory queries
description: Register handlers for keys, values and ranges, search free text, decide what unknown keys do, and accept queries from users.
---

[QueryBuilder\<T\>](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}) turns a query into a predicate on objects in memory. Register a handler for each key, then [Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build)
the query and call [Evaluate](cref:Meziantou.Framework.SimpleQueryLanguage.Query{T}.Evaluate) on each object:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Person>();
queryBuilder.AddHandler<string>("name", (obj, value) => obj.FullName.Contains(value, StringComparison.OrdinalIgnoreCase));
queryBuilder.AddRangeHandler<int>("age", (obj, value) => value.IsInRange((int)(DateTime.UtcNow - obj.DateOfBirth).TotalDays / 365));

var query = queryBuilder.Build("name:john AND age>=18");
Assert.True(query.Evaluate(new Person("John Doe", new DateTime(2000, 1, 1))));
Assert.False(query.Evaluate(new Person("Jane Doe", new DateTime(2000, 1, 1))));
Assert.False(query.Evaluate(new Person("Johnny Doe", DateTime.UtcNow.AddYears(-10))));
```
<!-- endref -->

with the following type:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_Person;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed record Person(string FullName, DateTime DateOfBirth);
```
<!-- endref -->

## Kinds of handlers

Choose the handler from what the key does:

| Registration | Receives | Operators | Example |
| --- | --- | --- | --- |
| `AddHandler(key, (obj, value) => …)` | The value as a string. | `:`, `=`, `<>` | `author:alice` |
| `AddHandler<TValue>(key, (obj, value) => …)` | The value parsed as `TValue`. | `:`, `=`, `<>` | `id:42` |
| `AddHandler(key, value, obj => …)` | Nothing: the handler is for one value of the key. | `:`, `=`, `<>` | `is:open` |
| `AddRangeHandler<TValue>(key, (obj, range) => …)` | A [RangeSyntax\<TValue\>](cref:Meziantou.Framework.SimpleQueryLanguage.Ranges.RangeSyntax{T}). | All, and ranges | `comments>=2`, `comments:1..5` |
| The overloads that take a [KeyValueOperator](cref:Meziantou.Framework.SimpleQueryLanguage.KeyValueOperator) | The operator, and the value. | All | `size>10` |

- `<>` negates the handler, so a handler only says whether the object matches.
- With an operator a handler does not accept, such as `author>alice`, the term matches nothing.
- A value that cannot be parsed as `TValue` matches nothing. See [Values and dates](06-ValuesAndDates.md) for the
  types parsed out of the box, and to parse your own.
- A handler for a value, such as `is:open`, takes precedence over a handler for the whole key. The value is compared
  case-insensitively.
- Keys are case-insensitive. Registering the same key, or the same key and value, twice throws an `ArgumentException`.

A range handler receives a [RangeSyntax\<TValue\>](cref:Meziantou.Framework.SimpleQueryLanguage.Ranges.RangeSyntax{T}), whose [IsInRange](cref:Meziantou.Framework.SimpleQueryLanguage.Ranges.RangeSyntax{T}.IsInRange) tells whether a value is inside the range, for
`comments:1..5` as for `comments>=2`. It accepts an `IComparer<TValue>` to compare values another way.

## Free text

The terms without a key go to the handler set with [SetTextFilterHandler](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.SetTextFilterHandler). Without it, free text matches nothing, and
`NOT crash` matches everything. Quoted text is passed without its quotes, spaces included: decide in the handler
whether `when saving` must appear as a whole.

## Unknown keys

A key without a handler is searched as free text: `label:bug` calls the text handler with `label:bug`. To decide
otherwise, for instance to search a dictionary of custom fields, set a handler for unknown keys. It receives the key,
the operator and the value:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_UnhandledProperty;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Issue>();
queryBuilder.SetTextFilterHandler((issue, text) => issue.Title.Contains(text, StringComparison.OrdinalIgnoreCase));

var item = new Issue("Support label:bug in the search", "alice", IsOpen: true, Comments: 0, new DateTime(2026, 1, 10));

// A key without a handler is searched as free text
Assert.True(queryBuilder.Build("label:bug").Evaluate(item));

// Unless an unhandled property handler decides
queryBuilder.SetUnhandledPropertyHandler((issue, propertyName, op, value) => false);
Assert.False(queryBuilder.Build("label:bug").Evaluate(item));
```
<!-- endref -->

## Building and reusing queries

- [Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) parses the query and its values once. Keep the [Query\<T\>](cref:Meziantou.Framework.SimpleQueryLanguage.Query{T}) to evaluate it on many objects; [Text](cref:Meziantou.Framework.SimpleQueryLanguage.Query{T}.Text) returns the
  query it was built from.
- Handlers registered or changed after [Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) do not change a query already built.
- Date keywords such as `today` are computed by [Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) too: rebuild a query that is kept from one day to the next. See
  [Relative dates](06-ValuesAndDates.md#relative-dates).

## Queries from users

A malformed query is never an error, but a query can be too complex to evaluate: thousands of nested parentheses, or
many `OR` groups combined with `AND`. [Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) then throws a [QueryTooComplexException](cref:Meziantou.Framework.SimpleQueryLanguage.QueryTooComplexException). Catch it when the query comes
from a user:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_UntrustedInput;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Issue>();
queryBuilder.SetTextFilterHandler((issue, text) => issue.Title.Contains(text, StringComparison.OrdinalIgnoreCase));

var userInput = new string('(', 100_000) + "crash";

Query<Issue>? query;
try
{
    query = queryBuilder.Build(userInput);
}
catch (QueryTooComplexException)
{
    query = null; // Tell the user to simplify the query
}

Assert.Null(query);
```
<!-- endref -->

[Build](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.Build) evaluates `(a OR b) AND (c OR d)` as `(a AND c) OR (a AND d) OR (b AND c) OR (b AND d)`, so each group
multiplies the size of the query. It refuses a query that expands to more than 8,192 alternatives or 262,144 terms.
[ExpressionQueryBuilder\<T\>.Build](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}.Build) has the same limits.
