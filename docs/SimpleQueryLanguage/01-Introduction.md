---
title: Introduction
description: Let users filter data with search-box queries such as "is:open author:alice comments>=2", in memory or in the database.
---

`Meziantou.Framework.SimpleQueryLanguage` parses the queries people type in a search box, such as
`is:open author:alice comments>=2 crash`, and turns them into a filter. You decide what each key means: the library
parses the query, the values and the dates, and combines your handlers with `AND`, `OR` and `NOT`.

## What you can do with it

- **Filter objects in memory** with `QueryBuilder<T>`: a query becomes a predicate you evaluate on each object. See
  [In-memory queries](04-InMemoryQueries.md).
- **Filter database queries** with `ExpressionQueryBuilder<T>`: a query becomes an expression tree that Entity Framework
  Core, or another LINQ provider, translates to SQL. See [Database queries](05-DatabaseQueries.md).
- **Give users a familiar syntax**: free text, key-value terms, comparisons, ranges, `AND`, `OR`, `NOT`, `-` and
  parentheses. See [Query syntax](03-QuerySyntax.md).
- **Search by dates** with `created:this_week` or `created>=@today-2w`. See [Values and dates](06-ValuesAndDates.md).
- **Accept any input**: a malformed query is never an error, and a query too complex to evaluate is reported with a
  dedicated exception. See [Queries from users](04-InMemoryQueries.md#queries-from-users).

## How it works

You register a handler for each key, such as `author` or `created`, and optionally a handler for free text. `Build`
parses the query once and returns an object you reuse:

| Builder | Handlers | Result | Use it with |
| --- | --- | --- | --- |
| `QueryBuilder<T>` | Delegates | `Query<T>`, with `Evaluate(T)` | Objects in memory. |
| `ExpressionQueryBuilder<T>` | Expressions | `ExpressionQuery<T>`, with `Apply(IQueryable<T>)` | Entity Framework Core and other `IQueryable<T>`. |

Both builders read the same syntax.

## Namespace

The builders and the queries are in the [Meziantou.Framework.SimpleQueryLanguage](cref:Meziantou.Framework.SimpleQueryLanguage) namespace. `RangeSyntax<T>`, the
value of a range handler, is in [Meziantou.Framework.SimpleQueryLanguage.Ranges](cref:Meziantou.Framework.SimpleQueryLanguage.Ranges).

## Next steps

- [Getting started](02-GettingStarted.md): install the package and filter a list.
- [Limitations](07-Limitations.md): what the library does not do.
