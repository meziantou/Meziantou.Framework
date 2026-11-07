---
title: Getting started
description: Install the package, start a server, and query it with Npgsql.
---

## Install the package

Add the `Meziantou.Framework.PostgreSqlServer` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.PostgreSqlServer" />

The package depends on the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`), which the .NET SDK provides,
also in console and test projects.

The examples connect with Npgsql, the PostgreSQL driver for .NET:

<InstallNuGetPackage Id="Npgsql" />

The examples of this documentation omit the `using` directives. They use the namespaces listed in
[Namespaces](01-Introduction.md#namespaces), plus `System.Net` and `Npgsql`.

## Start a server and query it

This example starts a server on a free port of the local machine, connects to it, and runs a query:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new PostgreSqlServerOptions();
options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback); // 0: the system picks a free port

await using var server = new PostgreSqlServer(
    options,
    authenticationHandler: (context, cancellationToken) =>
    {
        // SCRAM-SHA-256 by default: ValidatePassword checks the proof the client sent
        var result = context.UserName == "app" && context.ValidatePassword("Password123!")
            ? PostgreSqlAuthenticationResult.Success()
            : PostgreSqlAuthenticationResult.Fail("password authentication failed");
        return ValueTask.FromResult(result);
    },
    queryHandler: (context, cancellationToken) =>
    {
        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn("message", PostgreSqlColumnType.Text));

        // Describe asks for the columns only, before the execution
        if (context.RequestType is not PostgreSqlQueryRequestType.Describe)
        {
            resultSet.Rows.Add([$"You sent: {context.CommandText}"]);
        }

        var result = new PostgreSqlQueryResult();
        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    });
await server.StartAsync();

var port = server.Ports[0];
await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=Password123!;Server Compatibility Mode=NoTypeLoading");
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "SELECT 1";
var message = await command.ExecuteScalarAsync();
Assert.Equal("You sent: SELECT 1", message);
```
<!-- endref -->

- [AddTcpListener](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.AddTcpListener) adds an endpoint. Port `0` lets the system choose a free port, and [Ports](cref:Meziantou.Framework.PostgreSql.PostgreSqlServer.Ports) gives the port the server
  bound, which is what tests need. `bindAddress` defaults to the loopback address, so the server only accepts local
  connections.
- The authentication handler accepts or rejects each login. With SCRAM-SHA-256, the default method, it must call
  [ValidatePassword](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlAuthenticationContext.ValidatePassword). See [Authentication](04-Authentication.md).
- The query handler answers every request. Npgsql sends every command through the extended query protocol, which first
  asks for the columns of the result ([Describe](cref:Meziantou.Framework.PostgreSql.Handler.PostgreSqlQueryRequestType.Describe)), then runs it. See [Handling queries](05-HandlingQueries.md).
- `Server Compatibility Mode=NoTypeLoading` stops Npgsql from querying the PostgreSQL system catalog when it connects,
  which this server cannot answer. See [Connecting clients](08-ConnectingClients.md).
- Disposing the server stops listening and closes the connections.

## Host the server in an ASP.NET Core application

In an application, host the server with the generic host, so it starts and stops with the application and logs
through its logging configuration:

<!-- ref:../../samples/Meziantou.Framework.PostgreSqlServer.Samples/ReadmeExamples.cs;region=PostgreSqlServer_Hosting;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddPostgreSqlServer(options =>
{
    options.AuthenticationMethod = PostgreSqlAuthenticationMethod.ScramSha256;
    options.AddTcpListener(port: 5432);
});

var app = builder.Build();
app.MapPostgreSqlAuthenticationHandler((context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
    ? PostgreSqlAuthenticationResult.Success()
    : PostgreSqlAuthenticationResult.Fail("invalid credentials")));
app.MapPostgreSqlQueryHandler((context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));

app.Run();
```
<!-- endref -->

See [Hosting the server](03-HostingTheServer.md).

## Next steps

- [Returning results](06-ReturningResults.md): column types, command tags, errors.
- [Encryption](07-Encryption.md): configure TLS before exposing the server beyond the local machine.
- [Limitations](09-Limitations.md): what the server does not do.
