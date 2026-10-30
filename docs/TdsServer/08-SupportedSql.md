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
  subqueries, which can reference the outer query), and `EXISTS` and `NOT EXISTS` (simple subqueries, which can
  reference the outer query).
- `GROUP BY` on one or more columns, `HAVING`, and the aggregates `COUNT(*)`, `SUM`, `MIN`, `MAX` and `AVG`.
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

One difference: `JOIN … ON` matches `NULL` keys with each other, where SQL Server does not.

## XML methods

The XML methods take XPath 1.0 expressions, not XQuery. Common paths such as `/root/item[@id="2"]/text()` behave the
same, but FLWOR expressions (`for $x in … return`), `sql:variable()`, `sql:column()` and the XQuery functions return an
error.

Typed XML casts validate against schema collections registered with `AddXmlSchemaCollection(name, xsd)` on the query
engine options.

## Not supported

- `LIKE`.
- Recursive common table expressions.
- Many subquery forms, such as scalar subqueries in the selected columns.
- `INSERT`, `UPDATE`, `DELETE`, `MERGE` and other statements that change data, and any statement other than `SELECT`.
- Several statements in one request, and several result sets.
