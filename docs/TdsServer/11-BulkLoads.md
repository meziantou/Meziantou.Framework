---
title: Bulk loads
description: Receive the rows that SqlBulkCopy sends, describe the destination tables, and reject a batch with an error.
---

`SqlBulkCopy` copies rows in bulk, without one `INSERT` per row. To receive them, set
[TdsServerOptions.BulkLoadHandler](cref:Meziantou.Framework.Tds.TdsServerOptions.BulkLoadHandler) to a [TdsBulkLoadHandler](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadHandler), which has two callbacks:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_BulkLoad;format=md-fenced-code-block;dedent=true -->
```csharp
var orders = new List<IReadOnlyList<object?>>();
var options = new TdsServerOptions();
options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);
options.BulkLoadHandler = new TdsBulkLoadHandler
{
    // SqlBulkCopy asks for the columns of the destination table first
    GetTableColumns = (context, cancellationToken) =>
    {
        IReadOnlyList<TdsColumn>? columns = context.TableName == "Orders"
            ? [new TdsColumn("Id", TdsColumnType.Int32, isNullable: false), new TdsColumn("Customer", TdsColumnType.NVarChar), new TdsColumn("Amount", TdsColumnType.Decimal)]
            : null; // the table does not exist
        return ValueTask.FromResult(columns);
    },

    // Then it sends the rows, one call per batch
    LoadRows = (context, cancellationToken) =>
    {
        lock (orders)
        {
            orders.AddRange(context.Rows);
        }

        return ValueTask.FromResult(TdsBulkLoadResult.Success());
    },
};

using var server = new TdsServer(
    options,
    authenticationHandler: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
    queryHandler: (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));
await server.StartAsync();

using var table = new DataTable();
table.Columns.Add("Id", typeof(int));
table.Columns.Add("Customer", typeof(string));
table.Columns.Add("Amount", typeof(decimal));
table.Rows.Add(1, "Alice", 12.5m);
table.Rows.Add(2, "Bob", 7m);

await using var connection = new SqlConnection($"Server=127.0.0.1,{server.Ports[0]};User ID=app;Password=Password123!;Encrypt=Optional");
await connection.OpenAsync();

using var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = "dbo.Orders" };
await bulkCopy.WriteToServerAsync(table);

Assert.HasCount(2, orders);
Assert.Equal(1, orders[0][0]);
Assert.Equal("Alice", orders[0][1]);
Assert.Equal(12.5m, orders[0][2]);
```
<!-- endref -->

In an ASP.NET Core application, `app.MapTdsBulkLoadHandler(handler)` sets the same option. See
[Hosting the server](03-HostingTheServer.md).

## Describe the destination table

`SqlBulkCopy` starts by asking the server for the columns of the destination table, so it can convert the values of
the source to their types and match its column mappings. The server calls
[GetTableColumns](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadHandler.GetTableColumns) with a [TdsBulkLoadTableContext](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadTableContext):

- [TableName](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadTableContext.TableName), `SchemaName` and `DatabaseName` are the parts of
  `SqlBulkCopy.DestinationTableName`, without their quotes: `[sales].[Order Lines]` is the table `Order Lines` of the
  schema `sales`. A part the client does not give is `null`.
- `UserContext` is the `ClaimsPrincipal` of the [authentication handler](04-Authentication.md).

Return the columns of the table, in order. `SqlBulkCopy` maps the source columns to them by name or by ordinal, as it
does with SQL Server. Return `null` when the table does not exist: `WriteToServer` then throws an
`InvalidOperationException` whose inner `SqlException` is the error 208 "Invalid object name".

The columns are declared with the widest SQL type of their [TdsColumnType](cref:Meziantou.Framework.Tds.Handler.TdsColumnType): text is `nvarchar(max)`, binary data
`varbinary(max)`, and decimals `decimal(38, 10)`, so `SqlBulkCopy` rounds a decimal value to 10 decimal places.
A column with `isNullable: false` makes `SqlBulkCopy` throw on a `null` value before it sends the rows.

## Receive the rows

[LoadRows](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadHandler.LoadRows) is called once per batch with a [TdsBulkLoadContext](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadContext). `SqlBulkCopy` sends all the
rows in one batch, unless `SqlBulkCopy.BatchSize` is set.

| Property | Value |
| --- | --- |
| [Columns](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadContext.Columns) | The columns the client sends: the columns of the table that its column mappings target |
| [Rows](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadContext.Rows) | One value per column, typed as the parameters of a query (`int`, `string`, `decimal`, `byte[]`, `DateTime`…), and `null` for SQL `NULL` |
| [Options](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadContext.Options) | The options of the bulk load, such as `TABLOCK`, `KEEP_NULLS`, `CHECK_CONSTRAINTS`, `FIRE_TRIGGERS` or `ORDER([Id] ASC)`, from `SqlBulkCopyOptions` and `SqlBulkCopy.ColumnOrderHints` |
| `TableName`, `SchemaName`, `DatabaseName`, `UserContext` | As for [GetTableColumns](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadHandler.GetTableColumns) |

The server does not apply the options: they describe what the client asked for. [TdsBulkLoadResult.Success](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadResult.Success)
accepts the batch, and the server reports its row count to the client.

## Reject a batch

Return [TdsBulkLoadResult.FromError](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadResult.FromError) to reject the rows, for instance on a duplicate key: `WriteToServer` throws a
`SqlException` with the number and the message of the [TdsQueryError](cref:Meziantou.Framework.Tds.Handler.TdsQueryError), and the connection stays usable. The batches
that came before are not undone: there are no transactions.

An exception thrown by a callback returns the error 50002 "Unhandled bulk load handler exception", as for the query
handler.

## Without a bulk load handler

When [BulkLoadHandler](cref:Meziantou.Framework.Tds.TdsServerOptions.BulkLoadHandler) is `null`, the requests that `SqlBulkCopy` sends to read the destination table
reach the query handler like any other SQL batch, and the rows are rejected with the error 50001.

## Large copies

The server receives a batch as a whole before it calls [LoadRows](cref:Meziantou.Framework.Tds.Handler.TdsBulkLoadHandler.LoadRows), so a batch cannot be larger than
[MaxMessageSize](cref:Meziantou.Framework.Tds.TdsServerOptions.MaxMessageSize), 16 MiB by default: a larger batch closes the connection. Set `SqlBulkCopy.BatchSize` to copy
more data, or raise [MaxMessageSize](cref:Meziantou.Framework.Tds.TdsServerOptions.MaxMessageSize).
