---
title: Supported SQL
description: The SELECT statements, operators and functions the query engine translates, and how they treat NULL.
---

The [query engine](07-QueryEngine.md) translates one `SELECT` statement per request to a LINQ query. It does not run
other statements. This page lists what it translates; anything else returns the error 50004 with a message that names
the problem.

## Statements and clauses

- One `SELECT` statement, optionally preceded by non-recursive common table expressions (`WITH … AS (…)`), including
  column lists.
- `TOP`, `DISTINCT`, `SELECT *` on a single table, and selected columns and expressions with aliases.
- `FROM` a query root or a derived table (`FROM (SELECT …) AS t`).
- `INNER JOIN`, `LEFT JOIN` and `RIGHT JOIN`, on query roots or derived tables.
- `CROSS APPLY` over the `nodes()` method of an XML column.
- `WHERE` with comparisons, `AND`, `OR`, `NOT`, `IS NULL`, `IS NOT NULL`, `IN` and `NOT IN` (value lists and simple
  subqueries, which can reference the outer query), `EXISTS` and `NOT EXISTS` (simple subqueries, which can reference
  the outer query), and `LIKE` and `NOT LIKE` (see [LIKE](#like)).
- `GROUP BY` on one or more columns, `HAVING`, and the aggregates `COUNT(*)`, `SUM`, `MIN`, `MAX` and `AVG`.
- Scalar subqueries, in the selected columns and in `WHERE`, which can reference the outer query. See
  [Scalar subqueries](#scalar-subqueries).
- `ORDER BY`, with `OFFSET … FETCH`.
- `UNION` and `UNION ALL`.
- Parameters (`@name`), sent by the client with the command.

## Expressions and functions

- Arithmetic operators: `+`, `-`, `*`, `/`, `%`.
- String concatenation: `+`, `||` and `CONCAT`.
- String functions: `UPPER`, `LOWER`, `LEN`, `LTRIM`, `RTRIM`, `TRIM`, `LEFT`, `RIGHT`, `SUBSTRING`, `REPLACE`,
  `TRANSLATE`, `STUFF`, `STRING_ESCAPE`, `FORMAT`.
- `NULL` and conditional functions: `ISNULL`, `COALESCE`, `NULLIF`, `IIF` (its condition can use `IN` and `EXISTS`
  subqueries), `CHOOSE`.
- Conversions: `CAST`, `CONVERT`, `TRY_CAST`, `TRY_CONVERT`, including casts to typed and untyped `xml`.
- Dates: `GETDATE`, `SYSDATETIME`, `DATEADD`, `DATEDIFF`, `EOMONTH`, `YEAR`, `MONTH`, `DAY`.
- Mathematics: `ABS`, `ROUND`, `CEILING`, `FLOOR`, `POWER`, `SQRT`, `EXP`, `LOG`, `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`,
  `ATAN`, `ATN2`, `COT`.
- JSON: `ISJSON`, `JSON_VALUE`, `JSON_PATH_EXISTS`, `JSON_QUERY`, with the `lax` and `strict` path modes.
- XML methods: `.query()`, `.value()`, `.exist()` and `.nodes()`.

`UPPER` and `LOWER` use the invariant culture by default. To change the translation of a function, see
[Run the queries with your provider](07-QueryEngine.md#run-the-queries-with-your-provider).

## NULL and comparisons

The engine follows the three-valued logic of SQL:

- A comparison with `NULL` is never true: `<>` and `NOT` do not match rows where the value is `NULL`. For instance,
  `WHERE NOT (Name = 'a')` excludes the rows where `Name` is `NULL`, as SQL Server does.
- `NOT IN` over a list that contains `NULL` matches no row.
- `UPPER`, `LOWER`, `LEN`, `LTRIM`, `RTRIM`, `TRIM`, `LEFT`, `RIGHT` and `REPLACE` return `NULL` for a `NULL` argument.
- `ROUND` rounds halfway values away from zero: `ROUND(2.5, 0)` is 3 and `ROUND(-2.5, 0)` is -3.
- `JOIN … ON` never matches a `NULL` key, not even with another `NULL` key. An outer join keeps the row of its outer
  side with `NULL` columns for the other side.

## LIKE

`LIKE` and `NOT LIKE` support the wildcards of SQL Server: `%` matches any string, `_` any character, `[abc]` and
`[a-c]` one character of the set, and `[^abc]` one character outside of it. `ESCAPE '!'` makes the character after `!`
a literal, as in `LIKE '100!%' ESCAPE '!'`. A value of another type is converted to text, as in `Id LIKE '4%'`.

- Characters are compared ordinally, as `=` is: `LIKE 'a%'` does not match `Alice`. SQL Server compares them with the
  collation of the column, which ignores case by default.
- A `NULL` value, pattern or escape character matches no row, with `LIKE` and with `NOT LIKE`.
- A pattern given as a literal or a parameter, made of text with an optional `%` at its start and its end, is translated
  to `==`, `StartsWith`, `EndsWith` or `Contains`, so a LINQ provider can run it. Other patterns call a .NET method,
  which LINQ to objects runs but other providers may not.

## Scalar subqueries

A subquery in parentheses can be used as a value, for instance
`SELECT c.Id, (SELECT COUNT(*) FROM orders o WHERE o.CustomerId = c.Id) AS OrderCount FROM customers c`.

- It selects one expression, from one `FROM` table expression, with an optional `WHERE`, and `TOP` with an optional
  `ORDER BY`. `GROUP BY`, `HAVING` and `DISTINCT` are not supported.
- The aggregates `COUNT(*)`, `SUM`, `MIN`, `MAX` and `AVG` are supported, without `GROUP BY`. As in SQL Server, `SUM`,
  `MIN`, `MAX` and `AVG` are `NULL` when no row matches, and `COUNT(*)` is 0.
- A subquery that returns no row is `NULL`. One that returns several rows returns the error 512, as SQL Server does.
- An aggregate is translated to the LINQ operator (`Count`, `Sum`, …). A subquery that returns a column calls a .NET
  method to check that there is at most one row, which LINQ to objects runs but other providers may not.

## XML methods

The XML methods take XPath 1.0 expressions, not XQuery. Common paths such as `/root/item[@id="2"]/text()` behave the
same, but FLWOR expressions (`for $x in … return`), `sql:variable()`, `sql:column()` and the XQuery functions return an
error.

Typed XML casts validate against schema collections registered with `AddXmlSchemaCollection(name, xsd)` on the query
engine options.

## Not supported

- Recursive common table expressions.
- Scalar subqueries with `GROUP BY`, `HAVING` or `DISTINCT`, and subqueries in `GROUP BY` queries.
- `INSERT`, `UPDATE`, `DELETE`, `MERGE` and other statements that change data, and any statement other than `SELECT`.
- Several statements in one request, and several result sets.
