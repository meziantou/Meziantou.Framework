---
title: Query syntax
description: The terms, operators, ranges and logical operators users can type, their precedence, and how malformed queries are read.
---

This page describes what users can type. Share it with them, or summarize it next to your search box.

## Terms

| Syntax | Meaning |
| --- | --- |
| `crash` | Free text, passed to the text handler. |
| `"when saving"` | Free text with spaces. Inside quotes, `""` is a quote. |
| `key:value` or `key=value` | The key equals the value. |
| `key<>value` | The key does not equal the value. |
| `key<value`, `key<=value`, `key>value`, `key>=value` | Comparisons. |
| `key:"two words"` | A value with spaces. |
| `key:low..high` | A range. Both bounds are included. |
| `key:low..*` and `key:*..high` | A range open on one side: the same as `key>=low` and `key<=high`. |

What each key means, and which operators it accepts, depends on the handler the application registered for it. A key
the application does not know is searched as free text, `key:value` included, unless the application handles unknown
keys.

Write the key, the operator and the value without spaces: in `author : alice`, the colon is not an operator. A value
without quotes ends at the first space, parenthesis, `=`, `<` or `>`, so dates and ranges such as
`created:2026-01-01..2026-01-31` need no quotes.

Keys are case-insensitive: `Author:alice` is `author:alice`. Whether values are depends on the handler.

## Combining terms

| Syntax | Meaning |
| --- | --- |
| `a b` or `a AND b` | Both terms match. |
| `a OR b` | At least one term matches. |
| `NOT a` or `-a` | The term does not match. |
| `( … )` | Groups terms. |

`NOT` binds tighter than `AND`, which binds tighter than `OR`: `a OR b AND c` is `a OR (b AND c)`. Use parentheses to
group otherwise. The keywords are case-insensitive. Directly after an operator, a keyword is a value: `state:or`
searches for `or`.

## Examples

Each line of this example is a query and the issues it finds:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_Syntax;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Issue>();
queryBuilder.AddHandler("author", (issue, value) => issue.Author.Equals(value, StringComparison.OrdinalIgnoreCase));
queryBuilder.AddHandler("is", "open", issue => issue.IsOpen);
queryBuilder.AddHandler("is", "closed", issue => !issue.IsOpen);
queryBuilder.AddRangeHandler<int>("comments", (issue, range) => range.IsInRange(issue.Comments));
queryBuilder.AddRangeHandler<DateTime>("created", (issue, range) => range.IsInRange(issue.CreatedAt));
queryBuilder.SetTextFilterHandler((issue, text) => issue.Title.Contains(text, StringComparison.OrdinalIgnoreCase));

var issues = new[]
{
    new Issue("Crash on startup", "alice", IsOpen: true, Comments: 5, new DateTime(2026, 1, 10)),
    new Issue("Crash when saving", "bob", IsOpen: true, Comments: 0, new DateTime(2026, 2, 3)),
    new Issue("Typo in the readme", "alice", IsOpen: false, Comments: 1, new DateTime(2026, 3, 15)),
};

string[] Search(string query) => [.. issues.Where(queryBuilder.Build(query).Evaluate).Select(issue => issue.Title)];

// Key-value terms
Assert.Equal(["Crash on startup", "Typo in the readme"], Search("author:alice"));
Assert.Equal(["Crash on startup", "Typo in the readme"], Search("Author=ALICE"));
Assert.Equal(["Crash when saving"], Search("author<>alice"));

// Free text, and quotes for text with spaces
Assert.Equal(["Crash on startup", "Crash when saving"], Search("crash"));
Assert.Equal(["Crash when saving"], Search("\"when saving\""));

// Terms separated by spaces must all match
Assert.Equal(["Crash when saving"], Search("crash author:bob"));

// NOT, -, OR and parentheses
Assert.Equal(["Crash when saving"], Search("NOT author:alice"));
Assert.Equal(["Crash when saving"], Search("-author:alice"));
Assert.Equal(["Crash when saving", "Typo in the readme"], Search("author:bob OR is:closed"));
Assert.Equal(["Crash on startup"], Search("is:open AND (author:alice OR comments>3)"));

// AND binds tighter than OR: this is author:bob OR (is:closed AND comments:0)
Assert.Equal(["Crash when saving"], Search("author:bob OR is:closed AND comments:0"));

// Comparisons and ranges, bounds included
Assert.Equal(["Crash on startup", "Typo in the readme"], Search("comments>=1"));
Assert.Equal(["Crash on startup", "Typo in the readme"], Search("comments:1..5"));
Assert.Equal(["Crash when saving", "Typo in the readme"], Search("comments:*..1"));
Assert.Equal(["Crash when saving", "Typo in the readme"], Search("created:2026-02-01..2026-03-31"));
Assert.Equal(["Crash on startup"], Search("created<2026-02-01"));
```
<!-- endref -->

Dates also accept keywords such as `this_week` and relative dates such as `@today-1w`; see
[Values and dates](06-ValuesAndDates.md).

## Malformed queries

A query is never a syntax error: what cannot be read as an operator is read as text. A `)` without its `(`, a `(` at
the end of the query, or an `AND` or `OR` with nothing after it is searched as free text. An empty query, or one that
holds only spaces, matches everything.

A query can still be too complex to evaluate, such as thousands of nested parentheses; see
[Queries from users](04-InMemoryQueries.md#queries-from-users).
