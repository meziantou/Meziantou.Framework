---
title: Hosting the server
description: Run the server in an ASP.NET Core application or on its own, choose its endpoints, and set its limits and logging.
---

The server runs in one of two ways:

- **In an ASP.NET Core application** (`AddTdsServer`): Kestrel accepts the connections, and the server starts, stops
  and logs with the application. Use it for a service.
- **On its own** (`TdsServer`): you start and dispose it yourself. Use it in tests and small tools.

Both take the same `TdsServerOptions` and the same two callbacks.

## Host the server in ASP.NET Core

Call `AddTdsServer` on the application builder to add the endpoints, then register the callbacks on the built
application:

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

The callbacks are registered with:

| Method | Registers |
| --- | --- |
| `MapTdsHandlers(authenticate, query)` | Both callbacks |
| `MapTdsAuthenticationHandler(handler)` | The authentication callback |
| `MapTdsQueryHandler(handler)` | The query callback |
| `MapTdsQueryEngine(options)` | The [query engine](07-QueryEngine.md) as the query callback |

Without an authentication callback, every login fails with "No authentication handler configured"; without a query
callback, every request returns the error 50003 "No query handler configured".

The callbacks are registered after `Build`, so they can use the services of the application through `app.Services`.
They are shared by all the connections and can run concurrently: anything they capture must be thread-safe.

### Serve HTTP from the same application

`AddTdsServer` declares its endpoints to Kestrel in code. Kestrel then ignores the URLs configured with
`ASPNETCORE_URLS`, `--urls` or `UseUrls`, and logs "Overriding address(es)". If the application also serves HTTP,
declare the HTTP endpoints in code too:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/Examples.cs;region=TdsServer_ServeHttpToo;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddTdsServer(options => options.AddTcpListener(port: 14330));

// AddTdsServer declares Kestrel endpoints in code, so Kestrel no longer binds the default HTTP URLs
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(5000));

var app = builder.Build();
app.MapGet("/health", () => "OK");
app.MapTdsHandlers(
    authenticate: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
    query: (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));
app.Run();
```
<!-- endref -->

At startup, Kestrel logs the TDS endpoints as `Now listening on: http://…`: the scheme is the one Kestrel uses for all
its endpoints, the protocol on that port is TDS.

## Run the server on its own

Create a `TdsServer` with the options and the two callbacks, call `StartAsync`, and dispose it to stop:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/Examples.cs;region=TdsServer_StandaloneWithLogging;format=md-fenced-code-block;dedent=true -->
```csharp
using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug));

var options = new TdsServerOptions();
options.AddTcpListener(port: 14330);

using var server = new TdsServer(
    options,
    authenticationHandler: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
    queryHandler: (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()),
    logger: loggerFactory.CreateLogger<TdsServer>());
await server.StartAsync();

Console.WriteLine("Listening on port " + server.Ports[0] + ". Press Enter to stop.");
Console.ReadLine();
```
<!-- endref -->

- `StartAsync` binds every endpoint, then returns: connections are accepted in the background. If one endpoint cannot
  be bound, the others are released and the exception is thrown.
- `Ports` lists the bound ports. Use port `0` to get a free port, for instance in tests run in parallel.
- `Dispose` stops listening and cancels the token of the requests in progress. The server cannot be started again.
- The logger is optional. Without it, connections that fail to negotiate, to log in or to answer a query fail silently.
  With it, exceptions thrown by the query handler are logged as errors, and failed negotiations and connections closed
  by an exception at the `Debug` level.

## Endpoints

`AddTcpListener(port, bindAddress)` adds an endpoint. The port defaults to `1433`, the SQL Server port, and the address
to the loopback address (`127.0.0.1`), which only accepts connections from the local machine. Without any call, the
server listens on `127.0.0.1:1433`.

To accept connections from other machines, bind to `IPAddress.Any` (IPv4), `IPAddress.IPv6Any`, or the address of one
network interface, and [configure TLS](09-Encryption.md) first:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/Examples.cs;region=TdsServer_ServerOptions;format=md-fenced-code-block;dedent=true -->
```csharp
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 1433, bindAddress: IPAddress.Any); // every IPv4 interface
    options.AddTcpListener(port: 1433, bindAddress: IPAddress.IPv6Any);
    options.PacketSize = 8192; // size of the packets the server writes
    options.MaxMessageSize = 64 * 1024 * 1024; // largest request a client can send
    options.TlsPfxPath = "certificates/server.pfx";
    options.TlsPfxPassword = builder.Configuration["Tds:CertificatePassword"];
});
```
<!-- endref -->

## Options

| Option | Default | Description |
| --- | --- | --- |
| `PacketSize` | 4096 | The size of the packets the server writes, from `MinimumPacketSize` (512) to `MaximumPacketSize` (65535). |
| `MaxMessageSize` | 16 MB (`DefaultMaxMessageSize`) | The largest request a client can send, at least `MaximumPacketSize`. |
| `RequireEncryption` | `false` | Rejects the clients that do not encrypt. See [Encryption](09-Encryption.md). |
| `TlsPfxPath`, `TlsPfxPassword` | none | The TLS certificate, as a PFX file. |
| `TlsPemCertificatePath`, `TlsPemPrivateKeyPath` | none | The TLS certificate, as PEM files. |

The server buffers a whole request before it parses it, so `MaxMessageSize` bounds the memory a client can make the
server allocate, including before it logs in. A larger request closes the connection. Raise the limit when clients
send large SQL batches or parameters.

The TLS certificate is loaded when the server starts (`AddTdsServer` or `StartAsync`): a missing file or an invalid
combination of options throws there, not on the first connection.
