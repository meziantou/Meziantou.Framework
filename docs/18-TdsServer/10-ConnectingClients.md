---
title: Connecting clients
description: The connection strings and client APIs that work with the server, from logins to stored procedure calls.
---

The server speaks the protocol of SQL Server, so clients connect with their usual SQL Server API. The library is tested
with `Microsoft.Data.SqlClient`; this page uses it.

## Connection strings

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_ConnectionString;format=md-fenced-code-block;dedent=true -->
```csharp
var login = new TaskCompletionSource<TdsAuthenticationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
var options = new TdsServerOptions();
options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);
using var server = new TdsServer(
    options,
    authenticationHandler: (context, cancellationToken) =>
    {
        login.TrySetResult(context);
        return ValueTask.FromResult(TdsAuthenticationResult.Success());
    },
    queryHandler: (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));
await server.StartAsync();

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = $"127.0.0.1,{server.Ports[0]}", // host,port
    UserID = "app",
    Password = "Password123!",
    InitialCatalog = "Sales",
    ApplicationName = "Reporting",
    Encrypt = SqlConnectionEncryptOption.Optional,
}.ConnectionString;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

var context = await login.Task;
Assert.Equal("app", context.UserName);
Assert.Equal("Password123!", context.Password);
Assert.Equal("Sales", context.Database);
Assert.Equal("Reporting", context.ApplicationName);
```
<!-- endref -->

| Keyword | Use |
| --- | --- |
| `Server` / `Data Source` | `host,port`, with a comma. Without a port, the client uses 1433. Named instances (`host\name`) need the SQL Server Browser service, which the server does not provide. |
| `User ID`, `Password` | Sent to the authentication handler (`UserName`, `Password`). |
| `Initial Catalog` / `Database` | Sent to the authentication handler (`Database`). The database the client sees after the login is the one the handler returns. |
| `Application Name` | Sent to the authentication handler (`ApplicationName`). |
| `Encrypt` | `Optional` to connect to a server without a certificate. See [Encryption](09-Encryption.md). |
| `TrustServerCertificate` | `True` to accept a self-signed certificate, in tests. |
| `Connect Timeout`, `Command Timeout` | As with SQL Server. A command timeout cancels the request on the server. |

Do not set `Integrated Security` or an `Authentication` mode other than SQL Password: the server only supports SQL
logins. Do not set `MultipleActiveResultSets=True`: the server does not support it.

## Run queries

A command without parameters reaches the query handler as a SQL batch, a command with parameters as a call to
`sp_executesql`. See [Handling queries](05-HandlingQueries.md#request-types). The results are read as usual, including
several result sets and informational messages:

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

## Call stored procedures

Set `CommandType.StoredProcedure`, and the name of the procedure as the command text (`connection` is an open
`SqlConnection`):

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_CallStoredProcedure;format=md-fenced-code-block;dedent=true -->
```csharp
await using var command = connection.CreateCommand();
command.CommandType = CommandType.StoredProcedure;
command.CommandText = "GetCustomerName";
command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = 1 });

var name = await command.ExecuteScalarAsync();
Assert.Equal("Alice", name);
```
<!-- endref -->

The query handler receives the name in `ProcedureName` and the parameters in `Parameters`. With the
[query engine](07-QueryEngine.md#stored-procedures), the call runs the delegate registered under that name.

## What clients see

- `ExecuteNonQuery` returns -1: the server does not report the number of affected rows.
- Output parameters and return values are not set: the server does not return them.
- After an error returned by the server, `SqlClient` closes the connection. See
  [Return an error](06-ReturningResults.md#return-an-error).
- `BeginTransaction` fails with "Unsupported packet type": the server has no transactions.
