---
title: Getting started
description: Install the package, start a server, and query it with Microsoft.Data.SqlClient.
---

## Install the package

Add the `Meziantou.Framework.TdsServer` package to a project:

```bash
dotnet add package Meziantou.Framework.TdsServer
```

The package depends on the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`), which the .NET SDK provides,
also in console and test projects.

The examples connect with `Microsoft.Data.SqlClient`, the SQL Server client for .NET:

```bash
dotnet add package Microsoft.Data.SqlClient
```

The examples of this documentation omit the `using` directives. They use the namespaces listed in
[Namespaces](01-Introduction.md#namespaces), plus `System.Net`, `System.Security.Claims` and `Microsoft.Data.SqlClient`.

## Start a server and query it

This example starts a server on a free port of the local machine, connects to it, and runs a query:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TdsServerOptions();
options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback); // 0: the system picks a free port

using var server = new TdsServer(
    options,
    authenticationHandler: (context, cancellationToken) =>
    {
        var result = context.UserName == "app" && context.Password == "Password123!"
            ? TdsAuthenticationResult.Success()
            : TdsAuthenticationResult.Fail("Login failed.");
        return ValueTask.FromResult(result);
    },
    queryHandler: (context, cancellationToken) =>
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Message", TdsColumnType.NVarChar));
        resultSet.Rows.Add([$"You sent: {context.CommandText}"]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    });
await server.StartAsync();

var port = server.Ports[0];
await using var connection = new SqlConnection($"Server=127.0.0.1,{port};User ID=app;Password=Password123!;Encrypt=Optional");
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "SELECT 1";
var message = await command.ExecuteScalarAsync();
Assert.Equal("You sent: SELECT 1", message);
```
<!-- endref -->

- `AddTcpListener` adds an endpoint. Port `0` lets the system choose a free port, and `Ports` gives the port the server
  bound, which is what tests need. `bindAddress` defaults to the loopback address, so the server only accepts local
  connections.
- The authentication handler accepts or rejects each login. See [Authentication](04-Authentication.md).
- The query handler answers every request. Here it returns one result set with one row, whatever the SQL text. See
  [Handling queries](05-HandlingQueries.md) and [Returning results](06-ReturningResults.md).
- `Encrypt=Optional` in the connection string lets the client connect without TLS, since this server has no
  certificate. See [Encryption](09-Encryption.md).
- Disposing the server stops listening and cancels the requests in progress.

## Host the server in an ASP.NET Core application

In an application, host the server with the generic host, so it starts and stops with the application and logs
through its logging configuration:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/ReadmeExamples.cs;region=TdsServer_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
});

var app = builder.Build();
app.MapTdsHandlers(
    authenticate: (context, cancellationToken) =>
    {
        if (context.UserName == "sa" && context.Password == "Password123!")
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Name, context.UserName ?? "sa"),
            ],
            authenticationType: "password");
            return ValueTask.FromResult(TdsAuthenticationResult.Success(userContext: new ClaimsPrincipal(identity)));
        }

        return ValueTask.FromResult(TdsAuthenticationResult.Fail("Login failed"));
    },
    query: (context, cancellationToken) =>
    {
        var user = context.UserContext;

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Message", TdsColumnType.NVarChar));
        resultSet.Rows.Add(["Hello from TDS server"]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    });

app.Run();
```
<!-- endref -->

Clients connect with `Server=127.0.0.1,14330;User ID=sa;Password=Password123!;Encrypt=Optional`. See
[Hosting the server](03-HostingTheServer.md) for the options.

## Let the query engine answer the queries

Instead of parsing the SQL text yourself, map table names to `IQueryable` sources and let the built-in query engine
run the `SELECT` statements:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsQueryEngineTests.cs;region=TdsServer_QueryEngineQuickStart;format=md-fenced-code-block;dedent=true -->
```csharp
var products = new[]
{
    new { Id = 1, Name = "Keyboard", Price = 49.90m },
    new { Id = 2, Name = "Mouse", Price = 19.50m },
    new { Id = 3, Name = "Monitor", Price = 189.00m },
}.AsQueryable();

var queryEngineOptions = new TdsQueryEngineOptions();
queryEngineOptions.AddQueryRoot("products", context => products);

var options = new TdsServerOptions();
options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);
using var server = new TdsServer(
    options,
    authenticationHandler: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
    queryHandler: TdsQueryEngine.CreateQueryHandler(queryEngineOptions));
await server.StartAsync();

await using var connection = new SqlConnection($"Server=127.0.0.1,{server.Ports[0]};User ID=app;Password=Password123!;Encrypt=Optional");
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "SELECT Name, Price FROM dbo.products WHERE Price < @maxPrice ORDER BY Name";
command.Parameters.Add(new SqlParameter("@maxPrice", SqlDbType.Decimal) { Value = 50m });

var names = new List<string>();
await using (var reader = await command.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        names.Add(reader.GetString(0));
    }
}

Assert.Equal(["Keyboard", "Mouse"], names);
```
<!-- endref -->

See [The query engine](07-QueryEngine.md).

## Next steps

- [Hosting the server](03-HostingTheServer.md): endpoints, packet sizes, logging.
- [Encryption](09-Encryption.md): configure TLS before exposing the server beyond the local machine.
- [Limitations](11-Limitations.md): what the server does not do.
