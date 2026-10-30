---
title: Hosting the server
description: Run the server on its own or in an ASP.NET Core application, choose its endpoints, and set its limits and logging.
---

The server runs in one of two ways:

- **On its own** (`PostgreSqlServer`): you start and dispose it yourself. Use it in tests and small tools.
- **In an ASP.NET Core application** (`AddPostgreSqlServer`): Kestrel accepts the connections, and the server starts,
  stops and logs with the application. Use it for a service.

Both take the same `PostgreSqlServerOptions` and the same two callbacks.

## Run the server on its own

Pass the options and the two callbacks to the constructor, then start the server, as in
[Getting started](02-GettingStarted.md#start-a-server-and-query-it):

- `StartAsync` binds every listener, then accepts connections in the background. When one listener cannot bind, the
  others are closed and `StartAsync` throws. A second call does nothing.
- `Ports` lists the bound ports, in the order of the listeners, once `StartAsync` returns: read it when you listen on
  port 0.
- `StopAsync` stops accepting connections, closes the open ones, and waits for them to end; its `CancellationToken`
  bounds that wait. `DisposeAsync` stops the server the same way. A stopped server cannot be started again: create a
  new one.
- Canceling the token passed to `StartAsync` also stops the server.
- The last constructor argument is an optional `ILogger`. Without one, a connection that fails to negotiate,
  authenticate or answer does so silently: pass one while you develop.

## Host the server in ASP.NET Core

Call `AddPostgreSqlServer` on the application builder to add the endpoints, then register the callbacks on the built
application:

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

- `MapPostgreSqlHandlers(authenticate, query)` registers both callbacks in one call. Both must be registered: until
  then, logins fail ("No authentication handler configured") and queries get an error ("No query handler configured").
  A later call replaces the callbacks for the new connections.
- The listeners are Kestrel endpoints, configured in code. When Kestrel has endpoints configured in code, it ignores
  the URLs of the configuration (`ASPNETCORE_URLS`, `--urls`): add the HTTP endpoints of the application with
  `ConfigureKestrel` too, if it has any.
- The logger comes from the dependency injection container.
- The limits of Kestrel (`KestrelServerOptions.Limits.MaxConcurrentConnections`) apply instead of
  `MaxConcurrentConnections`.

## Endpoints

`AddTcpListener(port, bindAddress)` adds an endpoint. `port` defaults to 5432, the PostgreSQL port, and `bindAddress`
to `IPAddress.Loopback`, so the server only accepts local connections. Without any `AddTcpListener` call, the server
listens on `127.0.0.1:5432`. Pass `IPAddress.Any` or a specific address to accept remote clients, after configuring TLS
([Encryption](07-Encryption.md)). Call `AddTcpListener` several times to listen on several addresses or ports.

## Options

| Option | Default | Use |
| --- | --- | --- |
| `AuthenticationMethod` | `ScramSha256` | The password method requested from clients. See [Authentication](04-Authentication.md). |
| `ServerVersion` | `16.0` | The version reported to clients in `server_version`. It changes nothing else. |
| `RequireEncryption`, `TlsPfxPath`, `TlsPfxPassword`, `TlsPemCertificatePath`, `TlsPemPrivateKeyPath` | (none) | See [Encryption](07-Encryption.md). |
| `MaxConcurrentConnections` | 1000 | Connections beyond this are closed at once, with a warning in the log. Standalone server only. |
| `HandshakeTimeout` | 30 seconds | How long a connection may take to negotiate TLS and authenticate. |
| `IdleTimeout` | 30 minutes | Closes an authenticated connection that sends nothing for that long. |
| `MaxMessageSize` | 16 MB | The largest message a client may send; a larger one gets a protocol error. Raise it for large parameters or SQL texts. |
| `MaxPreparedStatementsPerConnection` | 1000 | The prepared statements one connection may keep. |
| `MaxPortalsPerConnection` | 1000 | The portals (bound statements) one connection may keep. |

`Timeout.InfiniteTimeSpan` disables `HandshakeTimeout` and `IdleTimeout`; leaving `HandshakeTimeout` disabled lets an
unauthenticated client keep a connection open forever. The startup packet of a connection is limited to 10000 bytes,
as in PostgreSQL.
