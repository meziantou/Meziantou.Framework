# Meziantou.Framework.TdsServer

`Meziantou.Framework.TdsServer` is a callback-based server library for accepting TDS (SQL Server protocol) connections.

## Features

- Accepts TDS client connections over TCP
- Callback for authentication (SQL login + token-based extension data)
- Callback for query handling (SQL batch and RPC requests)
- Optional query engine for stored procedures and a small SQL `SELECT` subset over `IQueryable` roots
- Serializes result sets and protocol tokens back to clients
- Native JSON column type serialization (`TDSType.JSON`, UTF-8 payload)
- ASP.NET Core hosting integration through `IHostApplicationBuilder`

## Usage

```csharp
using System.Net;
using System.Security.Claims;
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
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Name, context.UserName ?? "sa"),
            ],
            authenticationType: "password");
            return TdsAuthenticationResult.Success(userContext: new ClaimsPrincipal(identity));
        }

        return TdsAuthenticationResult.Fail("Login failed");
    },
    query: async (context, cancellationToken) =>
    {
        var user = context.UserContext;

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Message", TdsColumnType.NVarChar));
        resultSet.Rows.Add(["Hello from TDS server"]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    });

app.Run();
```

## Built-in query engine

The low-level query callback can be backed by the built-in query engine. It maps RPC requests to configured delegates and translates simple SQL text queries to typed `IQueryable` expressions.

```csharp
using Meziantou.Framework.Tds.QueryEngine;

var customers = new[]
{
    new Customer(1, "Alice"),
    new Customer(2, "Bob"),
}.AsQueryable();

var queryEngineOptions = new TdsQueryEngineOptions();
queryEngineOptions.AddQueryRoot(
    "customers",
    context =>
    {
        if (int.TryParse(context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return customers.Where(customer => customer.Id == userId);
        }

        return customers;
    });
queryEngineOptions.StoredProcedures.Add("GetCustomer", (int id) => customers.Where(customer => customer.Id == id));

app.MapTdsAuthenticationHandler((context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()));
app.MapTdsQueryEngine(queryEngineOptions);
```

Query roots are resolved per request and receive the full query `context`, so you can prefilter data for the authenticated user (or use other request metadata) before SQL translation happens.

You can also deny access to a specific stored procedure or query root:

```csharp
queryEngineOptions.IsAuthorized = (context, resourceKind, resourceName) =>
{
    var userId = context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier);

    if (resourceKind == TdsQueryEngineResourceKind.StoredProcedure &&
        string.Equals(resourceName, "AdminOnlyProc", StringComparison.OrdinalIgnoreCase))
    {
        return userId == "1";
    }

    if (resourceKind == TdsQueryEngineResourceKind.QueryRoot &&
        string.Equals(resourceName, "admin_customers", StringComparison.OrdinalIgnoreCase))
    {
        return userId == "1";
    }

    return true;
};
```

When authorization is denied, the built-in query engine returns a permission-denied error (SQL-style error `229`, class `14`). Unknown roots or stored procedures remain distinct errors.

By default, the query engine materializes translated queries by enumerating the `IQueryable`. You can replace `MaterializeAsync` to use an async provider-specific materializer such as Entity Framework Core's `ToListAsync`.

The initial SQL text support is intentionally small: one `SELECT` statement with optional non-recursive CTEs (`WITH ... AS (...)`) including CTE column lists, `FROM` (including derived tables), `INNER JOIN` (including derived tables), and `.NET 10+` `LEFT JOIN` / `RIGHT JOIN` (including derived tables), `CROSS APPLY` for `xml.nodes(...)`, `WHERE` comparisons combined with `AND`/`OR`/`NOT`, `IS NULL`/`IS NOT NULL`, `IN`/`NOT IN` (value lists and simple subqueries, including correlated references), `EXISTS`/`NOT EXISTS` (simple subqueries, including correlated references), SQL parameters, `TOP`, `DISTINCT`, `SELECT *` for single-table queries, selected columns with aliases, `ORDER BY` (including `OFFSET/FETCH`), `UNION`/`UNION ALL`, multi-column `GROUP BY`, `HAVING`, grouped aggregates (`COUNT(*)`, `SUM`, `MIN`, `MAX`, `AVG`), scalar arithmetic operators (`+`, `-`, `*`, `/`, `%`), string concatenation (`+`, `||`, `CONCAT`), and scalar functions (`UPPER`, `LOWER`, `LEN`, `LTRIM`, `RTRIM`, `TRIM`, `LEFT`, `RIGHT`, `SUBSTRING`, `REPLACE`, `TRANSLATE`, `STUFF`, `STRING_ESCAPE`, `FORMAT`, `ISNULL`, `COALESCE`, `NULLIF`, `IIF`, `CHOOSE`, `CAST`, `CONVERT`, `TRY_CAST`, `TRY_CONVERT`, `GETDATE`, `SYSDATETIME`, `DATEADD`, `DATEDIFF`, `EOMONTH`, `YEAR`, `MONTH`, `DAY`, `ABS`, `ROUND`, `CEILING`, `FLOOR`, `POWER`, `SQRT`, `EXP`, `LOG`, `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`, `ATN2`, `COT`, `ISJSON`, `JSON_VALUE`, `JSON_PATH_EXISTS`, `JSON_QUERY`) with SQL-style `lax`/`strict` path mode support for JSON path arguments, plus XML methods (`.query()`, `.value()`, `.exist()`, `.nodes()`) and typed/untyped XML casts.

Built-in scalar function mappings can be customized through `TdsQueryEngineOptions.ScalarFunctions` (or `AddScalarFunction`). By default `UPPER` maps to `string.ToUpperInvariant` and `LOWER` maps to `string.ToLowerInvariant`, but you can replace them (for example with provider-specific `ToUpper` / `ToLower` mappings).

Main unsupported SQL features include `LIKE`, recursive CTEs, many advanced subquery forms (for example scalar subqueries in projections), DML (`INSERT`/`UPDATE`/`DELETE`), and multiple statements/result sets.

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

        var user = context.UserContext; // ClaimsPrincipal from authentication result, if any

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
