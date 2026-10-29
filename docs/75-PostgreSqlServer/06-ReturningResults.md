---
title: Returning results
description: Return rows of typed columns, command tags and affected rows, notices, PostgreSQL errors, and the transaction state.
---

A `PostgreSqlQueryResult` holds the answer to a request: result sets, a command tag, notices, or an error.

## Return rows

A `PostgreSqlResultSet` has `Columns` and `Rows`. Each row is a list of values, in the order of the columns:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_ColumnTypes;format=md-fenced-code-block;dedent=true -->
```csharp
PostgreSqlQueryDelegate query = (context, cancellationToken) =>
{
    var resultSet = new PostgreSqlResultSet();
    resultSet.Columns.Add(new PostgreSqlColumn("id", PostgreSqlColumnType.Uuid));
    resultSet.Columns.Add(new PostgreSqlColumn("quantity", PostgreSqlColumnType.Int64));
    resultSet.Columns.Add(new PostgreSqlColumn("price", PostgreSqlColumnType.Numeric));
    resultSet.Columns.Add(new PostgreSqlColumn("in_stock", PostgreSqlColumnType.Boolean));
    resultSet.Columns.Add(new PostgreSqlColumn("released", PostgreSqlColumnType.Date));
    resultSet.Columns.Add(new PostgreSqlColumn("updated_at", PostgreSqlColumnType.TimestampTz));
    resultSet.Columns.Add(new PostgreSqlColumn("comment", PostgreSqlColumnType.Text));

    if (context.RequestType is not PostgreSqlQueryRequestType.Describe)
    {
        resultSet.Rows.Add(
        [
            Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2"),
            12L,
            19.99m,
            true,
            new DateOnly(2026, 3, 1),
            new DateTimeOffset(2026, 10, 29, 8, 30, 0, TimeSpan.Zero),
            null, // SQL NULL
        ]);
    }

    var result = new PostgreSqlQueryResult();
    result.ResultSets.Add(resultSet);
    return ValueTask.FromResult(result);
};
```
<!-- endref -->

The client reads the values with their .NET types:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_ColumnTypesClient;format=md-fenced-code-block;dedent=true -->
```csharp
await using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM products";
await using var reader = await command.ExecuteReaderAsync();
Assert.True(await reader.ReadAsync());

Assert.Equal(Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2"), reader.GetGuid(0));
Assert.Equal(12L, reader.GetInt64(1));
Assert.Equal(19.99m, reader.GetDecimal(2));
Assert.True(reader.GetBoolean(3));
Assert.Equal(new DateOnly(2026, 3, 1), reader.GetFieldValue<DateOnly>(4));
Assert.Equal(new DateTime(2026, 10, 29, 8, 30, 0, DateTimeKind.Utc), reader.GetDateTime(5));
Assert.True(reader.IsDBNull(6));
```
<!-- endref -->

- `null` or `DBNull.Value` is SQL `NULL`.
- Every row must have one value per column.
- The server sends the values in text or in binary format, as the client asks; the values must be convertible to the
  type of their column in both.
- `PostgreSqlColumn` takes an optional `isNullable`; it is information for your own code, and is not sent to the
  client.

### Column types

| `PostgreSqlColumnType` | PostgreSQL type | Values |
| --- | --- | --- |
| `Boolean` | `bool` | `bool` |
| `Int16`, `Int32`, `Int64` | `int2`, `int4`, `int8` | Integers |
| `Single`, `Double` | `float4`, `float8` | `float`, `double` |
| `Numeric` | `numeric` | `decimal` |
| `Text`, `VarChar` | `text`, `varchar` | `string` |
| `Bytea` | `bytea` | `byte[]` |
| `Uuid` | `uuid` | `Guid` |
| `Date` | `date` | `DateOnly`, `DateTime` |
| `Timestamp` | `timestamp` | `DateTime` |
| `TimestampTz` | `timestamptz` | `DateTimeOffset` |
| `Json`, `Jsonb` | `json`, `jsonb` | `string` (the JSON text) |

Numbers are converted with the invariant culture, so a numeric value of another .NET type, or a numeric string, works
too. Prefer the types of the table: a value that cannot be converted to its column type closes the connection, after
the rows already sent, and is only reported in the log. For `TimestampTz`, pass `DateTimeOffset` values rather than
`DateTime`.

### Several result sets

A result can hold several result sets, for a simple query with several statements. Each one is sent with its own
columns. Clients that use the extended protocol, such as Npgsql, expect one result set per command, described by
`Describe`: return several only for simple queries.

## Command tags and affected rows

The command tag tells the client what the command did. Clients use its number as the count of affected rows (the
result of `ExecuteNonQuery`):

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_ErrorsAndCommandTags;format=md-fenced-code-block;dedent=true -->
```csharp
if (context.CommandText?.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) != true)
{
    // An error the client sees as a PostgresException, without terminating the connection.
    return ValueTask.FromResult(PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
    {
        Code = "42601",
        Message = "syntax error",
    }));
}

// Affected rows for a command that returns no result set.
return ValueTask.FromResult(new PostgreSqlQueryResult
{
    CommandTag = "UPDATE",
    AffectedRowCount = 5,
    TransactionStatus = PostgreSqlTransactionStatus.InTransaction,
});
```
<!-- endref -->

| `CommandTag` | `AffectedRowCount` | Tag sent |
| --- | --- | --- |
| `null`, with result sets | `null` | `SELECT n`, `n` being the number of rows |
| `null`, without result sets | `null` | `OK 0` |
| `"UPDATE"` | `5` | `UPDATE 5` |
| `"INSERT 0 1"` (ends with a number) | any | `INSERT 0 1`, as is |

A tag without a number gets `AffectedRowCount`, or 0, appended: `"BEGIN"` is sent as `BEGIN 0`. Npgsql reports -1 for
a tag it does not know, such as `OK 0`: set `CommandTag` to the PostgreSQL tag of the command (`INSERT 0 n`, `UPDATE n`,
`DELETE n`) for `ExecuteNonQuery` to return the count.

## Send notices

`Notices` are informational messages, sent before the result, with the severity `NOTICE`:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_Notices;format=md-fenced-code-block;dedent=true -->
```csharp
PostgreSqlQueryDelegate query = (context, cancellationToken) =>
{
    var result = new PostgreSqlQueryResult
    {
        CommandTag = "DELETE",
        AffectedRowCount = 3,
    };
    result.Notices.Add("3 rows deleted, 2 rows kept");
    return ValueTask.FromResult(result);
};
```
<!-- endref -->

With Npgsql, they raise the `Notice` event of the connection:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_NoticesClient;format=md-fenced-code-block;dedent=true -->
```csharp
var notices = new List<string>();
connection.Notice += (sender, e) => notices.Add(e.Notice.MessageText);

await using var command = connection.CreateCommand();
command.CommandText = "DELETE FROM sessions WHERE expired";
var affectedRows = await command.ExecuteNonQueryAsync();

Assert.Equal(3, affectedRows);
Assert.Equal(["3 rows deleted, 2 rows kept"], notices);
```
<!-- endref -->

## Return an error

`PostgreSqlQueryResult.FromError` returns an error instead of a result, as in the command tag example above. The client
raises it as an exception (`PostgresException` with Npgsql, whose `SqlState` is the code), and the connection stays
usable.

| `PostgreSqlQueryError` property | Default | Use |
| --- | --- | --- |
| `Message` | (required) | The message the client shows |
| `Code` | `XX000` (internal error) | The SQLSTATE, such as `42601` (syntax error), `42P01` (undefined table) or `23505` (unique violation) |
| `Severity` | `ERROR` | The severity label. Another label, even `FATAL`, does not close the connection. |

When `Error` is set, the result sets and the notices are not sent. The message, the code and the severity must not be
empty.

## Report the transaction state

After each command, the server tells the client whether a transaction is open. Set `TransactionStatus` when you
implement `BEGIN`, `COMMIT` and `ROLLBACK`:

| `PostgreSqlTransactionStatus` | Meaning |
| --- | --- |
| `Idle` (default) | Not in a transaction |
| `InTransaction` | In a transaction block |
| `Failed` | In a failed transaction block, until `ROLLBACK` |

- The status of each result replaces the previous one. Since the default is `Idle`, return `InTransaction` with every
  result inside a transaction, not only for `BEGIN`.
- An error returned with `InTransaction` is reported as `Failed`.
- The server does not track transactions: it passes `BEGIN`, `COMMIT` and the commands of a failed transaction to the
  handler like any other command. The handler has no connection identifier to keep the state; use
  `RemoteEndPoint` (one per connection) as the key, and remove the state when the transaction ends.
