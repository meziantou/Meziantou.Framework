---
title: Supported SQL
description: The SELECT, INSERT, UPDATE and DELETE statements, operators and functions the query engine translates, and how they treat NULL.
---

The [query engine](07-QueryEngine.md) translates the `SELECT` statements of a request to LINQ queries, and runs the
`INSERT`, `UPDATE` and `DELETE` statements with the handlers of their query root. It does not run other statements.
This page lists what it translates; anything else returns the error 50004 with a message that names the problem.

## Statements and clauses

- One or more `SELECT`, `INSERT`, `UPDATE` and `DELETE` statements, separated by `;` or not. Each `SELECT` returns a
  result set, in order: read them with `SqlDataReader.NextResult`, or fill a `DataSet`. All the statements are
  translated before the first one runs, so an invalid statement returns an error, no result set, and changes nothing.
  Parameters are shared by all the statements. See [INSERT, UPDATE and DELETE](#insert-update-and-delete).
- Each statement can start with common table expressions (`WITH … AS (…)`), including column lists. They can be
  recursive: see [Recursive common table expressions](#recursive-common-table-expressions).
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

## Recursive common table expressions

A common table expression that reads itself is recursive, for instance to walk a hierarchy:

```sql
WITH chain AS (
    SELECT Id, Name, 0 AS Level FROM employees WHERE ManagerId IS NULL
    UNION ALL
    SELECT e.Id, e.Name, c.Level + 1 FROM employees e INNER JOIN chain c ON e.ManagerId = c.Id
)
SELECT Id, Name, Level FROM chain ORDER BY Level, Id
```

- The anchor members come first, combined with `UNION` or `UNION ALL`, then the recursive members, each after
  `UNION ALL`. The anchor runs once, then the recursive members run on the rows of the previous level until they return
  no row.
- The anchor names the columns, unless the expression has a column list, so the columns of the recursive members do not
  need names. A column is nullable when the anchor or a recursive member returns a nullable value; its type must
  otherwise be the same, or the error 240 is returned.
- As in SQL Server, a recursive member reads the expression once, in its `FROM` clause and not in a subquery or an outer
  join, and cannot use `DISTINCT`, `TOP`, `GROUP BY`, `HAVING` or aggregates. These statements return the errors 252,
  253, 460 to 467 of SQL Server.
- The recursion stops with the error 530 after 100 levels. `OPTION (MAXRECURSION n)` at the end of the statement sets
  the limit, from 0 (no limit) to 32767.
  [MaxRecursiveCommonTableExpressionRows](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.MaxRecursiveCommonTableExpressionRows) limits the number of rows, for an expression whose rows grow
  exponentially or that has no limit; the recursion also stops when the request is canceled.
- The expression is computed in memory: the anchor and the recursive members are enumerated synchronously, and the
  statement reads the rows with LINQ to objects.

## INSERT, UPDATE and DELETE

These statements need the handlers of the query root they change. See [Modify data](07-QueryEngine.md#modify-data).

- `INSERT INTO t (columns) VALUES (…), (…)`, `INSERT INTO t (columns) SELECT …` and `INSERT INTO t DEFAULT VALUES`.
  Without a column list, the values go to the settable properties of the row type, in their declaration order. `DEFAULT`
  keeps the value set by the constructor. The selected columns of `INSERT … SELECT` match the columns by position.
- `UPDATE t SET column = expression, … WHERE …`, with the compound operators `+=`, `-=`, `*=`, `/=` and `%=`. Every
  expression reads the values of the row before the statement: `SET A = B, B = A` swaps the values.
- `DELETE FROM t WHERE …`, or `DELETE t`.
- `UPDATE` and `DELETE` accept `TOP (n)`, and a `FROM` clause that contains only their target, to give it an alias:
  `UPDATE p SET Price = 0 FROM products AS p WHERE p.Id = 1`.
- The values are converted to the types of the properties. A conversion that fails returns an error, and `NULL` in a
  `NOT NULL` column the error 515.
- The `WHERE` clause and the expressions support what a `SELECT` supports, including subqueries, and the statements can
  start with common table expressions, recursive or not.

## XML methods

The XML methods take XPath 1.0 expressions, not XQuery. Common paths such as `/root/item[@id="2"]/text()` behave the
same, but FLWOR expressions (`for $x in … return`), `sql:variable()`, `sql:column()` and the XQuery functions return an
error.

Typed XML casts validate against schema collections registered with `AddXmlSchemaCollection(name, xsd)` on the query
engine options.

## Not supported

- `SELECT` without `FROM`.
- Scalar subqueries with `GROUP BY`, `HAVING` or `DISTINCT`, and subqueries in `GROUP BY` queries.
- `MERGE`, the `OUTPUT` clause, `INSERT TOP`, joins in `UPDATE` and `DELETE`, and a common table expression as the
  target of a statement.
- Any statement other than `SELECT`, `INSERT`, `UPDATE` and `DELETE`, such as `DECLARE` and `SET`.
- `GO`: it separates batches in client tools such as SQL Server Management Studio, and is not part of T-SQL.
