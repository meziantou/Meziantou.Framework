---
title: Handling queries
description: Answer simple and extended queries, return the shape of a result for Describe, read the parameters, and handle cancellation.
---

The query handler is called for each request of a connection, with a [PostgreSqlQueryContext](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext), and returns a
[PostgreSqlQueryResult](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryResult). This page covers what a request contains; [Returning results](06-ReturningResults.md) covers
the answer.

## Request types

[RequestType](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.RequestType) tells which part of the protocol the request comes from:

| [RequestType](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.RequestType) | Sent by the client | The handler returns |
| --- | --- | --- |
| [SimpleQuery](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.SimpleQuery) | A query without parameters, through the simple query protocol, as `psql` does | The result |
| [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe) | The extended query protocol, before the execution, to know the columns of the result | The columns of the result, without rows |
| [ExtendedQuery](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.ExtendedQuery) | The extended query protocol, to execute a statement with its parameters | The result |

[CommandText](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.CommandText) is the SQL text as the client wrote it, with its `$1`, `$2` placeholders in an extended query. The server
does not parse or split it: a simple query with several statements (`SELECT 1; SELECT 2`) reaches the handler once, as
one text. An empty query is answered without calling the handler.

## Answer Describe

Drivers that use the extended protocol, such as Npgsql for every command, ask for the shape of the result before they
execute it. The server cannot infer it from the SQL text, so the handler must return the columns for [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe):

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_Parameters;format=md-fenced-code-block;dedent=true -->
```csharp
var customers = new Dictionary<int, string> { [1] = "Alice", [2] = "Bob" };

PostgreSqlQueryDelegate query = (context, cancellationToken) =>
{
    var resultSet = new PostgreSqlResultSet();
    resultSet.Columns.Add(new PostgreSqlColumn("id", PostgreSqlColumnType.Int32));
    resultSet.Columns.Add(new PostgreSqlColumn("name", PostgreSqlColumnType.Text));

    var result = new PostgreSqlQueryResult();
    result.ResultSets.Add(resultSet);

    // The same columns for Describe and for the execution; Describe gets no rows
    if (context.RequestType is PostgreSqlQueryRequestType.Describe)
    {
        return ValueTask.FromResult(result);
    }

    // Parameters are positional: $1, $2, ...
    var id = context.Parameters.Count > 0 ? context.Parameters[0].AsInt32() : null;
    if (id is not null && customers.TryGetValue(id.Value, out var name))
    {
        resultSet.Rows.Add([id.Value, name]);
    }

    return ValueTask.FromResult(result);
};
```
<!-- endref -->

- Return the same columns for [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe) as for the execution. Rows returned for [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe) are ignored.
- Return a result without result sets for a command that returns no rows.
- When the execution returns another number of columns than [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe) announced, the server sends the error `XX000`
  instead of rows the client cannot read.
- An error returned (or thrown) for [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe) does not reach the client: the server answers "no rows". Report errors
  from the execution.
- [Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe) is called for the statement and for the portal (the statement bound to its parameters), so the handler can
  be called twice before the execution. [Parameters](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.Parameters) is empty for the statement, and set for the portal.

## Read the parameters

[Parameters](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.Parameters) holds the parameters of an extended query, in order: the first one is `$1`, and its [Name](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.Name) is `"$1"`.
A client that sends this command:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_ParametersClient;format=md-fenced-code-block;dedent=true -->
```csharp
await using var command = connection.CreateCommand();
command.CommandText = "SELECT id, name FROM customers WHERE id = $1";
command.Parameters.Add(new NpgsqlParameter { Value = 2 });

await using var reader = await command.ExecuteReaderAsync();
Assert.True(await reader.ReadAsync());
Assert.Equal(2, reader.GetInt32(0));
Assert.Equal("Bob", reader.GetString(1));
```
<!-- endref -->

gets the row of customer 2 from the handler above. Each [PostgreSqlQueryParameter](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter) has a [Type](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.Type) (a
[PostgreSqlColumnType](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlColumnType), from the type the client declared), a decoded [Value](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.Value) (`DBNull.Value` for SQL `NULL`, and
[IsNull](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.IsNull) is `true`), and conversion methods that return `null` for `NULL`:

| Method | Returns |
| --- | --- |
| [AsString()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsString) | The text of the value |
| [AsInt32()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsInt32), [AsInt64()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsInt64) | An integer |
| [AsBoolean()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsBoolean) | A Boolean |
| [AsDouble()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsDouble), [AsDecimal()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsDecimal) | A number |
| [AsGuid()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsGuid) | A `Guid` |
| [AsDateTime()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsDateTime), [AsDateTimeOffset()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsDateTimeOffset) | A date and time |
| [AsBinary()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsBinary) | The bytes of a `bytea` value |
| [AsJson()](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.AsJson) | A `JsonObject` |

Clients send values in text or binary format; both are decoded. For a type the library does not model, [TypeOid](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.TypeOid) (the
PostgreSQL type OID), [FormatCode](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.FormatCode) (0 for text, 1 for binary) and [RawValue](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryParameter.RawValue) give the value as it was sent. A parameter
whose type the client did not declare is decoded as text.

## Prepared statements

[StatementName](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.StatementName) and [PortalName](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryContext.PortalName) are the names the client gave to the prepared statement and to the portal, empty for
the unnamed ones. The server keeps the prepared statements of each connection, up to
[MaxPreparedStatementsPerConnection](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.MaxPreparedStatementsPerConnection); the handler is called with their SQL text at each execution, so it does not need
to track them.

## Cancellation

The `CancellationToken` of the handler is canceled when the client cancels the command (a PostgreSQL `CancelRequest`,
sent by `NpgsqlCommand.Cancel`, a canceled token or a command timeout), when the client disconnects (ASP.NET Core
hosting), and when the server stops:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
PostgreSqlQueryDelegate query = async (context, cancellationToken) =>
{
    if (context.RequestType is not PostgreSqlQueryRequestType.Describe && context.CommandText == "SELECT pg_sleep(3600)")
    {
        handlerStarted.TrySetResult();

        // Canceled when the client cancels the command, or when the server stops
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    return new PostgreSqlQueryResult();
};
```
<!-- endref -->

When the handler throws `OperationCanceledException` after a cancellation, the client gets the error `57014`
"canceling statement due to user request", and the connection stays usable. A handler that ignores the token runs to
the end, and its result is sent.

## Concurrency

A connection runs one request at a time: the handler is not called again for the same connection until it returns.
Different connections call the handler concurrently.

## Exceptions

When the handler throws, the server logs the exception and returns the error `XX000` "Unhandled query handler
exception" to the client: the message of the exception is not sent, and the connection stays open. To send a
meaningful error, return it. See [Return an error](06-ReturningResults.md#return-an-error).
