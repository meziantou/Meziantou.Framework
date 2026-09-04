# Meziantou.Framework.PostgreSqlServer

`Meziantou.Framework.PostgreSqlServer` is a callback-based server library for accepting PostgreSQL protocol connections.
Real PostgreSQL clients (Npgsql, `psql`, JDBC) connect to it, and you decide how to authenticate them and how to answer their queries.

It is useful for test doubles, query gateways, proxies, and data virtualization — anything that should speak the PostgreSQL wire protocol without being PostgreSQL.

## Features

- Accepts PostgreSQL client connections over TCP
- Callback for authentication (cleartext, MD5, SCRAM-SHA-256)
- Callback for query handling (simple query and extended query flow)
- TLS negotiation (`SSLRequest`) on the same endpoint
- Query cancellation support (`CancelRequest`)
- ASP.NET Core hosting integration through `IHostApplicationBuilder`

## Security: configure TLS

Without a certificate the server answers `SSLRequest` with "not supported", and clients that allow it (Npgsql's default `SSL Mode=Prefer`) then log in over an unencrypted connection — sending the password in the clear for cleartext and MD5 authentication. Configure `TlsPfxPath` or the PEM options for any deployment that is not loopback-only, and set `RequireEncryption` to reject plaintext connections outright.

```csharp
var options = new PostgreSqlServerOptions
{
    RequireEncryption = true,
    TlsPfxPath = "server.pfx",
    TlsPfxPassword = "…",
};
```

`RequireEncryption` without a certificate throws at startup rather than silently accepting plaintext.

## Quickstart (standalone)

```csharp
using Meziantou.Framework.PostgreSql;
using Meziantou.Framework.PostgreSql.Handler;

var options = new PostgreSqlServerOptions
{
    AuthenticationMethod = PostgreSqlAuthenticationMethod.ScramSha256,
};
options.AddTcpListener(port: 5432);

using var server = new PostgreSqlServer(
    options,
    authenticationHandler: (context, cancellationToken) =>
    {
        // ValidatePassword must be called for SCRAM-SHA-256: it is what computes the server signature.
        var isValid = context.UserName == "app" && context.ValidatePassword("Password123!");
        return ValueTask.FromResult(isValid
            ? PostgreSqlAuthenticationResult.Success()
            : PostgreSqlAuthenticationResult.Fail("invalid credentials"));
    },
    queryHandler: (context, cancellationToken) =>
    {
        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn("id", PostgreSqlColumnType.Int32));
        resultSet.Columns.Add(new PostgreSqlColumn("name", PostgreSqlColumnType.Text));

        // A Describe request only needs the columns; the rows are ignored.
        if (context.RequestType != PostgreSqlQueryRequestType.Describe)
        {
            resultSet.Rows.Add([1, "Meziantou"]);
        }

        var result = new PostgreSqlQueryResult();
        result.ResultSets.Add(resultSet);
        return ValueTask.FromResult(result);
    },
    logger: null);

await server.StartAsync();
Console.WriteLine($"Listening on port {server.Ports[0]}");
Console.ReadLine();

await server.StopAsync();
```

Bind to port `0` to let the OS choose a free port, then read it back from `Ports` — the usual pattern in tests.

## The query callback must answer `Describe`

Every driver that sends parameters uses the extended query protocol, which asks for the shape of the result **before** executing it. The server cannot infer that shape from the SQL text, so the callback is invoked with `PostgreSqlQueryRequestType.Describe` and must return the columns it will produce.

- Return the same columns for `Describe` as for the execution. Rows returned for a `Describe` request are ignored.
- Return a result with no result sets to answer "this command returns no rows".
- If the executed shape does not match what `Describe` announced, the server reports an error rather than sending rows the client cannot read.

`Describe` is not sent for simple queries (`PostgreSqlQueryRequestType.SimpleQuery`), which carry their own row description.

## Reading parameters

`PostgreSqlQueryParameter` exposes typed accessors (`AsInt32`, `AsGuid`, `AsDateTimeOffset`, `AsDecimal`, `AsJson`, …). Clients may send values in text or binary format; both are decoded. For a type the library does not model, `TypeOid`, `FormatCode` and `RawValue` give you the undecoded value.

```csharp
var id = context.Parameters[0].AsInt32();
```

## ASP.NET Core hosting

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddPostgreSqlServer(options =>
{
    options.AuthenticationMethod = PostgreSqlAuthenticationMethod.ScramSha256;
    options.AddTcpListener(port: 5432);
});

var app = builder.Build();
app.MapPostgreSqlAuthenticationHandler((context, cancellationToken) => /* … */);
app.MapPostgreSqlQueryHandler((context, cancellationToken) => /* … */);
app.Run();
```

Both handlers must be mapped. A connection that arrives before they are fails authentication or receives an error, rather than being served.

## Reporting errors, tags and transaction state

```csharp
// An error the client sees as a PostgresException, without terminating the connection.
return ValueTask.FromResult(PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
{
    Code = "42601",
    Message = "syntax error",
}));

// Affected rows for a command that returns no result set.
return ValueTask.FromResult(new PostgreSqlQueryResult
{
    CommandTag = "UPDATE",
    AffectedRowCount = 5,
    TransactionStatus = PostgreSqlTransactionStatus.InTransaction,
});
```

Set `TransactionStatus` when you implement `BEGIN`/`COMMIT`, otherwise clients cannot tell that a transaction block is open.

## Limits

The server bounds what an unauthenticated peer can consume. All of these are configurable on `PostgreSqlServerOptions`:

| Option | Default | Purpose |
| --- | --- | --- |
| `MaxMessageSize` | 16 MB | Caps the buffer a client-declared message length can allocate |
| `MaxConcurrentConnections` | 1000 | Connections beyond this are closed immediately |
| `HandshakeTimeout` | 30 s | Bounds how long an unauthenticated connection may live |
| `IdleTimeout` | 30 min | Closes authenticated connections that stop sending |
| `MaxPreparedStatementsPerConnection` | 1000 | Bounds per-connection statement state |
| `MaxPortalsPerConnection` | 1000 | Bounds per-connection portal state |

The startup packet is capped at 10 000 bytes, the same limit PostgreSQL itself uses.

## Diagnostics

Pass an `ILogger` to the `PostgreSqlServer` constructor. Without one, a connection that fails to negotiate, authenticate or answer a query does so silently. The ASP.NET Core host resolves a logger from the container automatically.
