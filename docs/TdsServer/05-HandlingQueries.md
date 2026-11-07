---
title: Handling queries
description: Read the SQL text, the stored procedure name and the parameters of each request, and handle cancellation.
---

The query handler is called once for each request of a connection, with a [TdsQueryContext](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext) that describes the request,
and returns a [TdsQueryResult](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult). This page covers what a request contains; [Returning results](06-ReturningResults.md)
covers the answer.

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/ReadmeExamples.cs;region=TdsServer_AccessRequest;format=md-fenced-code-block;dedent=true -->
```csharp
app.MapTdsHandlers(
    authenticate: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
    query: (context, cancellationToken) =>
    {
        if (context.RequestType == TdsQueryRequestType.SqlBatch)
        {
            var commandText = context.CommandText; // e.g. "SELECT * FROM Users WHERE Id = @id"
        }
        else if (context.RequestType == TdsQueryRequestType.Rpc)
        {
            var procedureName = context.ProcedureName; // e.g. "sp_executesql" or your procedure name
        }

        foreach (var parameter in context.Parameters)
        {
            var parameterName = parameter.Name;
            var rawValue = parameter.Value; // DBNull.Value for SQL NULL
            var columnType = parameter.Type;
            var stringValue = parameter.AsString();
            var intValue = parameter.AsInt32();
            var json = parameter.AsJson();
            var xml = parameter.AsXml();
        }

        var user = context.UserContext; // ClaimsPrincipal from authentication result, if any

        return ValueTask.FromResult(new TdsQueryResult());
    });
```
<!-- endref -->

## Request types

[RequestType](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.RequestType) tells how the client sent the request:

| [RequestType](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.RequestType) | Sent by the client for | Read |
| --- | --- | --- |
| [SqlBatch](cref:Meziantou.Framework.Tds.Handler.TdsQueryRequestType.SqlBatch) | A command without parameters (`CommandType.Text`) | [CommandText](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.CommandText) |
| [Rpc](cref:Meziantou.Framework.Tds.Handler.TdsQueryRequestType.Rpc) | A stored procedure call (`CommandType.StoredProcedure`) | [ProcedureName](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.ProcedureName) and [Parameters](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.Parameters) |
| [Rpc](cref:Meziantou.Framework.Tds.Handler.TdsQueryRequestType.Rpc) | A command with parameters, as a call to `sp_executesql` | [ProcedureName](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.ProcedureName) is `sp_executesql`; the SQL text is in [Parameters](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.Parameters) |

[CommandText](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.CommandText) is the SQL text as the client wrote it, and is `null` for RPC requests. The server does not parse or run
it: matching it, or parsing it with a SQL parser, is up to the handler, unless you use the
[query engine](07-QueryEngine.md).

## Read the parameters of a query

`SqlClient` sends a command that has parameters as a call to the `sp_executesql` system procedure. Its first
parameter is the SQL text, the second one declares the parameters (`@id int`), and the values of the parameters
follow, with their names:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_ParameterizedQuery;format=md-fenced-code-block;dedent=true -->
```csharp
TdsQueryDelegate query = (context, cancellationToken) =>
{
    if (!context.HasCompleteParameters)
    {
        return ValueTask.FromResult(TdsQueryResult.FromError(new TdsQueryError { Message = "A parameter has an unsupported type." }));
    }

    // A command with parameters is sent as a call to sp_executesql: the first parameter is the SQL text,
    // the second one declares the parameters, and the values follow
    if (context.RequestType == TdsQueryRequestType.Rpc && string.Equals(context.ProcedureName, "sp_executesql", StringComparison.OrdinalIgnoreCase))
    {
        var sql = context.Parameters[0].AsString();
        var id = context.Parameters.FirstOrDefault(parameter => parameter.Name == "@id")?.AsInt32();

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Sql", TdsColumnType.NVarChar));
        resultSet.Columns.Add(new TdsColumn("Id", TdsColumnType.Int32));
        resultSet.Rows.Add([sql, id]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    }

    return ValueTask.FromResult(new TdsQueryResult());
};
```
<!-- endref -->

For the command `SELECT Name FROM customers WHERE Id = @id` with `@id` set to 42, this handler returns the SQL text
and 42.

## Answer a stored procedure call

A stored procedure call has the name of the procedure in [ProcedureName](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.ProcedureName) and its parameters in [Parameters](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.Parameters):

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_StoredProcedureCall;format=md-fenced-code-block;dedent=true -->
```csharp
TdsQueryDelegate query = (context, cancellationToken) =>
{
    if (context.RequestType == TdsQueryRequestType.Rpc && string.Equals(context.ProcedureName, "GetCustomerName", StringComparison.OrdinalIgnoreCase))
    {
        var id = context.Parameters.FirstOrDefault(parameter => parameter.Name == "@id")?.AsInt32();

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Name", TdsColumnType.NVarChar));
        resultSet.Rows.Add([id == 1 ? "Alice" : null]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    }

    return ValueTask.FromResult(TdsQueryResult.FromError(new TdsQueryError
    {
        Number = 2812,
        Message = $"Could not find stored procedure '{context.ProcedureName}'.",
    }));
};
```
<!-- endref -->

The client calls it as it would call a SQL Server procedure. See [Connecting clients](10-ConnectingClients.md).

## Parameters

Each [TdsQueryParameter](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter) has a [Name](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.Name) (with its `@` prefix, as the client sent it), a [Type](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.Type) (a [TdsColumnType](cref:Meziantou.Framework.Tds.Handler.TdsColumnType)) and a
`Value`. A SQL `NULL` is `DBNull.Value`, and [IsNull](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.IsNull) is `true`.

The conversion methods return `null` for a SQL `NULL`, and for a value they cannot convert:

| Method | Returns |
| --- | --- |
| [AsString()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsString) | The text of the value; binary values are Base64-encoded |
| [AsInt32()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsInt32), [AsInt64()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsInt64) | An integer, from any integer type or from text, when it is in range |
| [AsBoolean()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsBoolean) | A Boolean, from `bit`, an integer (non-zero is `true`) or `"true"`/`"false"` |
| [AsDouble()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsDouble), [AsDecimal()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsDecimal) | A number, from any numeric type or from text (invariant culture) |
| [AsBinary()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsBinary) | The bytes of a binary value |
| [AsJson()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsJson) | A `JsonObject`; throws when the value is not a JSON object |
| [AsXml()](cref:Meziantou.Framework.Tds.Handler.TdsQueryParameter.AsXml) | An `XDocument`; throws when the value is not well-formed XML or declares a DTD |

### Incomplete parameters

When the client sends a parameter whose type the server cannot decode, or a malformed request, [Parameters](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.Parameters) stops
before that parameter, and [HasCompleteParameters](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.HasCompleteParameters) is `false`. Reject such requests, as the first example of
[Read the parameters of a query](#read-the-parameters-of-a-query) does, rather than run them with missing values. The
query engine rejects them.

## Cancellation

The `CancellationToken` of the handler is canceled when the client cancels the command (`SqlCommand.Cancel`, or the
command timeout), when the client disconnects, and when the server stops. Pass it to the asynchronous calls of the
handler.

When the client cancels, the server acknowledges the cancellation at once, even when the handler ignores the token,
and the connection stays usable. The result of the abandoned handler is discarded.

## Concurrency

A connection runs one request at a time: the handler is not called again for the same connection until it returns.
Different connections call the handler concurrently.

## Exceptions

When the handler throws, the server logs the exception and returns the error 50002 "Unhandled query handler
exception" to the client: the message of the exception is not sent. To send a meaningful error, return it. See
[Return an error](06-ReturningResults.md#return-an-error).
