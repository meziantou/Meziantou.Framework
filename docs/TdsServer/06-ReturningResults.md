---
title: Returning results
description: Return result sets with typed columns, several result sets, informational messages, row counts, output parameters and SQL Server-style errors.
---

The query handler returns a [TdsQueryResult](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult), which holds the result sets, the informational messages, the number of affected
rows, the output parameters and the return value, or an error.
An empty [TdsQueryResult](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult) is a valid answer: the command succeeds and returns no rows.

## Return rows

A [TdsResultSet](cref:Meziantou.Framework.Tds.Handler.TdsResultSet) is a list of columns and a list of rows. Each row has one value per column, in the order of the
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

- A [TdsColumn](cref:Meziantou.Framework.Tds.Handler.TdsColumn) has a name, a type and a nullability (`isNullable`, `true` by default). Declare a column as not
  nullable only when none of its values is `null`.
- A `null` value is a SQL `NULL`. A row shorter than the list of columns gets `NULL` for the missing values.
- Each result set in [ResultSets](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.ResultSets) is a result set for the client, in order.
- [InfoMessages](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.InfoMessages) are sent before the result sets, as informational messages (severity 10), like the output of `PRINT`
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

| [TdsColumnType](cref:Meziantou.Framework.Tds.Handler.TdsColumnType) | SQL Server type | Values |
| --- | --- | --- |
| [TinyInt](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.TinyInt), [SmallInt](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.SmallInt), [Int32](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Int32), [Int64](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Int64) | `tinyint`, `smallint`, `int`, `bigint` | Any value `Convert` can turn into the integer type |
| [Boolean](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Boolean) | `bit` | A `bool`, or a value `Convert.ToBoolean` accepts |
| [Real](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Real), [Double](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Double) | `real`, `float` | `float`, `double`, or any convertible number |
| [Decimal](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Decimal) | `decimal(38, s)` | A `decimal`, or any convertible number |
| [Money](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Money), [SmallMoney](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.SmallMoney) | `money`, `smallmoney` | A `decimal`, or any convertible number |
| [NVarChar](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.NVarChar) | `nvarchar` | A `string`; other values are converted with the invariant culture |
| [Binary](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Binary) | `varbinary` | A `byte[]`; other values are sent as the UTF-8 bytes of their text |
| `Guid` | `uniqueidentifier` | A `Guid`, or its text |
| [Date](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Date) | `date` | A `DateOnly`, a `DateTime` or a `DateTimeOffset` (date part) |
| [Time](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Time) | `time(7)` | A `TimeOnly`, a `TimeSpan`, or a `DateTime` (time of day) |
| `DateTime` | `datetime` | A `DateTime`, a `DateOnly` or a `DateTimeOffset` |
| [DateTime2](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.DateTime2) | `datetime2(7)` | A `DateTime`, a `DateOnly` or a `DateTimeOffset` |
| `DateTimeOffset` | `datetimeoffset(7)` | A `DateTimeOffset`; a `DateTime` gets the offset 0 |
| [Xml](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Xml) | `xml` | The text of the value (`ToString()`), for instance a `string` or an `XDocument` |
| [Json](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Json) | `json` | A JSON `string`, a `JsonNode`, or any object, serialized with `System.Text.Json` |
| [Variant](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Variant), [UserDefined](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.UserDefined), `Table` | `nvarchar` | The text of the value (`ToString()`) |

- The scale of a [Decimal](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Decimal) column is the largest scale of its values, so `49.90m` and `19.5m` are sent with two
  decimals. A value that does not fit in 38 digits at that scale makes the whole response an error (50005).
- Text and binary values of any length are sent in full, including values longer than 8,000 bytes.
- `json` is a recent SQL Server type: the client must support it to read a [Json](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Json) column.

A value that cannot be converted to the type of its column (a text in an [Int32](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Int32) column, for instance) makes the
server return the error 50005 "Failed to build the query response" instead of the result sets. The connection is not
dropped by the server.

## Return a row count

Set [RecordsAffected](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.RecordsAffected) to the number of rows an `INSERT`, `UPDATE` or `DELETE` changed:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_RecordsAffected;format=md-fenced-code-block;dedent=true -->
```csharp
TdsQueryDelegate query = (context, cancellationToken) =>
{
    if (context.CommandText?.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) == true)
    {
        return ValueTask.FromResult(new TdsQueryResult { RecordsAffected = 3 });
    }

    return ValueTask.FromResult(new TdsQueryResult());
};
```
<!-- endref -->

- `ExecuteNonQuery` returns it, and so does `SqlDataReader.RecordsAffected` once the reader is closed. Data access
  code that checks the count, like the optimistic concurrency checks of EF Core, relies on it.
- When it is `null`, no count is sent and `ExecuteNonQuery` returns -1, as with `SET NOCOUNT ON`.
- The rows of the result sets are not counted: SQL Server does not count the rows of a `SELECT` either. `SqlClient`
  still reports them with the `SqlCommand.StatementCompleted` event.

## Return output parameters and a return value

A parameter that the client declares with the direction `Output` or `InputOutput` has
[IsOutput](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.IsOutput) set. Send its value back with [OutputParameters](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.OutputParameters), and the return status of a stored procedure with
[ReturnValue](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.ReturnValue):

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_OutputParameters;format=md-fenced-code-block;dedent=true -->
```csharp
TdsQueryDelegate query = (context, cancellationToken) =>
{
    if (context.RequestType == TdsQueryRequestType.Rpc && string.Equals(context.ProcedureName, "CreateCustomer", StringComparison.OrdinalIgnoreCase))
    {
        var result = new TdsQueryResult
        {
            RecordsAffected = 1,
            ReturnValue = 0,
        };

        result.OutputParameters.Add(new TdsOutputParameter("@id", TdsColumnType.Int32, 42));
        return ValueTask.FromResult(result);
    }

    return ValueTask.FromResult(new TdsQueryResult());
};
```
<!-- endref -->

The client reads them from its parameters once the command completes; with `ExecuteReader`, once the reader is closed:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_ReadOutputParameters;format=md-fenced-code-block;dedent=true -->
```csharp
await using var command = connection.CreateCommand();
command.CommandType = CommandType.StoredProcedure;
command.CommandText = "CreateCustomer";
command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 100) { Value = "Alice" });
var id = command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Direction = ParameterDirection.Output });
var returnValue = command.Parameters.Add(new SqlParameter("@return", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });

var recordsAffected = await command.ExecuteNonQueryAsync();
Assert.Equal(1, recordsAffected);
Assert.Equal(42, id.Value);
Assert.Equal(0, returnValue.Value);
```
<!-- endref -->

- A [TdsOutputParameter](cref:Meziantou.Framework.Tds.Handler.TdsOutputParameter) has a name, with or without its `@` prefix, a type and a value; `null` and `DBNull.Value` are
  SQL `NULL`. The values are converted as the values of a column of the same type, see [Column types](#column-types).
- The client matches the values to its parameters by name, and ignores a value whose name has no `Output` or
  `InputOutput` parameter.
- [ReturnValue](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.ReturnValue) goes to the parameter whose direction is `ReturnValue`. When it is `null`, that parameter keeps its value.
- Output parameters also work with the commands sent through `sp_executesql`, such as
  `INSERT … ; SET @id = SCOPE_IDENTITY()`.

## Return an error

Return [TdsQueryResult.FromError](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.FromError) to fail the command with a SQL Server-style error:

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

The client throws a `SqlException` with the [Number](cref:Meziantou.Framework.Tds.Handler.TdsQueryError.Number), [Class](cref:Meziantou.Framework.Tds.Handler.TdsQueryError.Class) (severity) and [State](cref:Meziantou.Framework.Tds.Handler.TdsQueryError.State) of the error; they default to
50000, 16 and 1. Use the numbers of SQL Server (208 for an unknown object, 2812 for an unknown stored procedure, …)
when the client handles specific errors. When [Error](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult.Error) is set, the result sets and informational messages are not
sent. Messages longer than the protocol allows are truncated.

The connection stays open after an error, as with SQL Server: the next command runs on the same connection. Like
SQL Server, `SqlClient` closes the connection when the severity is 20 or more, so keep the default severity, 16, for
the errors of a command.

## Errors sent by the server

| Number | Cause |
| --- | --- |
| 18456 | A rejected login, unless the authentication handler sets another number. |
| 50001 | A request type that the server does not support, such as a distributed transaction request. |
| 50002 | The query handler threw an exception. |
| 50003 | No query handler is registered (ASP.NET Core hosting). |
| 50005 | The result could not be serialized: a value does not match its column type. |
