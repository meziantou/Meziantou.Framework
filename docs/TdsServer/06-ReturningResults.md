---
title: Returning results
description: Return result sets with typed columns, several result sets, informational messages and SQL Server-style errors.
---

The query handler returns a `TdsQueryResult`, which holds the result sets, the informational messages, or an error.
An empty `TdsQueryResult` is a valid answer: the command succeeds and returns no rows.

## Return rows

A `TdsResultSet` is a list of columns and a list of rows. Each row has one value per column, in the order of the
columns:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_ResultSets;format=md-fenced-code-block;dedent=true -->
```csharp
TdsQueryDelegate query = (context, cancellationToken) =>
{
    var products = new TdsResultSet();
    products.Columns.Add(new TdsColumn("Id", TdsColumnType.Int32, isNullable: false));
    products.Columns.Add(new TdsColumn("Name", TdsColumnType.NVarChar));
    products.Columns.Add(new TdsColumn("Price", TdsColumnType.Decimal));
    products.Columns.Add(new TdsColumn("ReleaseDate", TdsColumnType.Date));
    products.Rows.Add([1, "Keyboard", 49.90m, new DateOnly(2024, 3, 1)]);
    products.Rows.Add([2, "Mouse", 19.50m, null]); // null is sent as SQL NULL

    var count = new TdsResultSet();
    count.Columns.Add(new TdsColumn("Count", TdsColumnType.Int32));
    count.Rows.Add([2]);

    var result = new TdsQueryResult();
    result.InfoMessages.Add("2 products found");
    result.ResultSets.Add(products);
    result.ResultSets.Add(count);
    return ValueTask.FromResult(result);
};
```
<!-- endref -->

- A `TdsColumn` has a name, a type and a nullability (`isNullable`, `true` by default). Declare a column as not
  nullable only when none of its values is `null`.
- A `null` value is a SQL `NULL`. A row shorter than the list of columns gets `NULL` for the missing values.
- Each result set in `ResultSets` is a result set for the client, in order.
- `InfoMessages` are sent before the result sets, as informational messages (severity 10), like the output of `PRINT`
  in SQL Server. `SqlClient` raises them with the `SqlConnection.InfoMessage` event.

The client reads them as it reads the results of SQL Server. Here, `connection` is a `SqlConnection` to the server:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_ReadResultSets;format=md-fenced-code-block;dedent=true -->
```csharp
var messages = new List<string>();
connection.InfoMessage += (sender, e) => messages.Add(e.Message);
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM products";
await using var reader = await command.ExecuteReaderAsync();

var names = new List<string>();
while (await reader.ReadAsync())
{
    names.Add(reader.GetString(reader.GetOrdinal("Name")));
}

Assert.True(await reader.NextResultAsync()); // move to the second result set
Assert.True(await reader.ReadAsync());
Assert.Equal(2, reader.GetInt32(0));

Assert.Equal(["Keyboard", "Mouse"], names);
Assert.Equal(["2 products found"], messages);
```
<!-- endref -->

## Column types

The type of a column sets the SQL Server type the client receives, and how the values of the column are converted:

| `TdsColumnType` | SQL Server type | Values |
| --- | --- | --- |
| `TinyInt`, `SmallInt`, `Int32`, `Int64` | `tinyint`, `smallint`, `int`, `bigint` | Any value `Convert` can turn into the integer type |
| `Boolean` | `bit` | A `bool`, or a value `Convert.ToBoolean` accepts |
| `Real`, `Double` | `real`, `float` | `float`, `double`, or any convertible number |
| `Decimal` | `decimal(38, s)` | A `decimal`, or any convertible number |
| `Money`, `SmallMoney` | `money`, `smallmoney` | A `decimal`, or any convertible number |
| `NVarChar` | `nvarchar` | A `string`; other values are converted with the invariant culture |
| `Binary` | `varbinary` | A `byte[]`; other values are sent as the UTF-8 bytes of their text |
| `Guid` | `uniqueidentifier` | A `Guid`, or its text |
| `Date` | `date` | A `DateOnly`, a `DateTime` or a `DateTimeOffset` (date part) |
| `Time` | `time(7)` | A `TimeOnly`, a `TimeSpan`, or a `DateTime` (time of day) |
| `DateTime` | `datetime` | A `DateTime`, a `DateOnly` or a `DateTimeOffset` |
| `DateTime2` | `datetime2(7)` | A `DateTime`, a `DateOnly` or a `DateTimeOffset` |
| `DateTimeOffset` | `datetimeoffset(7)` | A `DateTimeOffset`; a `DateTime` gets the offset 0 |
| `Xml` | `xml` | The text of the value (`ToString()`), for instance a `string` or an `XDocument` |
| `Json` | `json` | A JSON `string`, a `JsonNode`, or any object, serialized with `System.Text.Json` |
| `Variant`, `UserDefined`, `Table` | `nvarchar` | The text of the value (`ToString()`) |

- The scale of a `Decimal` column is the largest scale of its values, so `49.90m` and `19.5m` are sent with two
  decimals. A value that does not fit in 38 digits at that scale makes the whole response an error (50005).
- Text and binary values of any length are sent in full, including values longer than 8,000 bytes.
- `json` is a recent SQL Server type: the client must support it to read a `Json` column.

A value that cannot be converted to the type of its column (a text in an `Int32` column, for instance) makes the
server return the error 50005 "Failed to build the query response" instead of the result sets. The connection is not
dropped by the server.

## Return an error

Return `TdsQueryResult.FromError` to fail the command with a SQL Server-style error:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_Errors;format=md-fenced-code-block;dedent=true -->
```csharp
TdsQueryDelegate query = (context, cancellationToken) =>
{
    if (context.CommandText?.Contains("orders", StringComparison.OrdinalIgnoreCase) == true)
    {
        return ValueTask.FromResult(TdsQueryResult.FromError(new TdsQueryError
        {
            Number = 208,
            Class = 16,
            Message = "Invalid object name 'orders'.",
        }));
    }

    return ValueTask.FromResult(new TdsQueryResult());
};
```
<!-- endref -->

The client throws a `SqlException` with the `Number`, `Class` (severity) and `State` of the error; they default to
50000, 16 and 1. Use the numbers of SQL Server (208 for an unknown object, 2812 for an unknown stored procedure, …)
when the client handles specific errors. When `Error` is set, the result sets and informational messages are not
sent. Messages longer than the protocol allows are truncated.

The server sends errors with the "server error" status of the protocol. `SqlClient` treats them as severe: it adds
"A severe error occurred on the current command. The results, if any, should be discarded." to the message of the
exception and closes the connection. Open a new connection to send the next command; with connection pooling, `Open`
does it for you.

## Errors sent by the server

| Number | Cause |
| --- | --- |
| 18456 | A rejected login, unless the authentication handler sets another number. |
| 50001 | A request type that the server does not support, such as a transaction request. |
| 50002 | The query handler threw an exception. |
| 50003 | No query handler is registered (ASP.NET Core hosting). |
| 50005 | The result could not be serialized: a value does not match its column type. |
