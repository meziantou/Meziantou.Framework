---
title: Connecting clients
description: The connection string settings that work with the server, and what clients see when they use it.
---

The server speaks the protocol of PostgreSQL, so clients connect with their usual PostgreSQL API. The library is tested
with Npgsql; this page uses it.

## Connection strings

A connection string for the server looks like
`Host=127.0.0.1;Port=5432;Username=app;Password=Password123!;Database=sales;Server Compatibility Mode=NoTypeLoading`.

| Keyword | Use |
| --- | --- |
| `Host`, `Port` | The address of a listener. Port 5432 by default. |
| `Username`, `Password` | Sent to the authentication handler ([UserName](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlAuthenticationContext.UserName), and the password checked by [ValidatePassword](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlAuthenticationContext.ValidatePassword)). |
| `Database` | Sent to the authentication handler (`Database`). |
| `Application Name` | Sent as the `application_name` startup parameter. |
| `Server Compatibility Mode=NoTypeLoading` | Required. Without it, Npgsql queries the system catalog of PostgreSQL (`pg_type`) when it opens the connection, and the open fails unless your handler answers that query. |
| `SSL Mode`, `Trust Server Certificate` | See [Encryption](07-Encryption.md). |
| `Timeout`, `Command Timeout` | As with PostgreSQL. A command timeout cancels the request on the server. |

`NoTypeLoading` limits Npgsql to the built-in types, which is what the server returns. See
[Column types](06-ReturningResults.md#column-types).

## Run commands

Npgsql sends every command through the extended query protocol, with or without parameters: the handler gets a
[Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe), then an [ExtendedQuery](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.ExtendedQuery) request. See [Answer Describe](05-HandlingQueries.md#answer-describe). Results are
read as usual, with the typed getters:

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

Npgsql parameters can be positional (`$1`, as in [Read the parameters](05-HandlingQueries.md#read-the-parameters)) or
named (`@id`): Npgsql rewrites named parameters to `$1`, `$2` before it sends the SQL, so the handler always sees
positional placeholders.

## What clients see

- `ExecuteNonQuery` returns the number of the command tag, or -1 for a tag Npgsql does not know, such as the default
  `OK 0`. See [Command tags and affected rows](06-ReturningResults.md#command-tags-and-affected-rows).
- Errors returned by the handler raise `PostgresException`, with the code in `SqlState`, and the connection stays
  usable.
- A canceled command raises `OperationCanceledException`, whose inner exception is a `PostgresException` with the code
  `57014`.
- Transactions are commands the handler answers like the others. See
  [Report the transaction state](06-ReturningResults.md#report-the-transaction-state).
- `COPY` (`BeginBinaryImport`, `BeginTextImport`, ...) is not supported. See [Limitations](09-Limitations.md).
