# Meziantou.Framework.TdsServer

`Meziantou.Framework.TdsServer` is a callback-based server library for accepting TDS (SQL Server protocol) connections.

## Features

- Accepts TDS client connections over TCP
- Callback for authentication (SQL login + token-based extension data)
- Callback for query handling (SQL batch and RPC requests)
- Serializes result sets and protocol tokens back to clients
- Native JSON column type serialization (`TDSType.JSON`, UTF-8 payload)
- ASP.NET Core hosting integration through `IHostApplicationBuilder`

## Usage

```csharp
using System.Net;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
});

var app = builder.Build();
app.MapTdsHandlers(
    authenticate: async (context, cancellationToken) =>
    {
        if (context.UserName == "sa" && context.Password == "Password123!")
            return TdsAuthenticationResult.Success();

        return TdsAuthenticationResult.Fail("Login failed");
    },
    query: async (context, cancellationToken) =>
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Message", TdsColumnType.NVarChar));
        resultSet.Rows.Add(["Hello from TDS server"]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    });

app.Run();
```

## Access command text, stored procedure name, and parameters

The query callback gives you access to both SQL batch text and RPC/stored-procedure metadata:

```csharp
app.MapTdsHandlers(
    authenticate: async (context, cancellationToken) => TdsAuthenticationResult.Success(),
    query: async (context, cancellationToken) =>
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

        return new TdsQueryResult();
    });
```

## TLS configuration

TLS uses SQL Server PRELOGIN encryption negotiation on the same TCP endpoint. Configure either a PFX file or a PEM certificate/private key pair.
For `Microsoft.Data.SqlClient` interoperability, the server negotiates TLS 1.2 for encrypted TDS sessions.

### PFX

```csharp
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
    options.TlsPfxPath = "certificates/server.pfx";
    options.TlsPfxPassword = "Password123!";
});
```

### PEM certificate + private key

```csharp
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
    options.TlsPemCertificatePath = "certificates/server.crt.pem";
    options.TlsPemPrivateKeyPath = "certificates/server.key.pem";
});
```

Use client connection strings with `Encrypt=True` to require TLS, or `Encrypt=Optional` to allow non-TLS on the same endpoint.
