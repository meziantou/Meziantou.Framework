---
title: The query engine
description: Answer SELECT statements from IQueryable sources and stored procedure calls from .NET delegates, with per-user filtering and authorization.
---

The query engine is a ready-made query handler. It answers two kinds of requests:

- **SQL text** (`SELECT` statements, with or without parameters): it translates the statement to a LINQ query over the
  `IQueryable` sources you register as tables, called query roots, and returns the rows.
- **Stored procedure calls**: it calls the .NET delegate registered under the name of the procedure, with the
  parameters of the call, and returns what the delegate returns.

## Set up the query engine

Configure a `TdsQueryEngineOptions`, then use it as the query handler:

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

- `TdsQueryEngine.CreateQueryHandler(options)` creates the query handler of a standalone server. In ASP.NET Core,
  `app.MapTdsQueryEngine(options)` registers it. See [Hosting the server](03-HostingTheServer.md).
- Configure the options completely before creating the handler: the query roots, stored procedures, functions and
  schemas are copied at that point, and later changes are ignored.
- The SQL statement is translated to `products.Where(…).OrderBy(…).Select(…)`, so the `IQueryable` provider runs the
  filter and the sort. The SQL text never runs anywhere else.

## Query roots

`AddQueryRoot(name, factory)` exposes an `IQueryable<T>` as a table. The public properties of `T` are the columns, and
their .NET types set the column types:

| .NET type | Column type |
| --- | --- |
| `byte`, `short`, `int`, `long` | `tinyint`, `smallint`, `int`, `bigint` |
| `bool` | `bit` |
| `float`, `double` | `real`, `float` |
| `decimal` | `decimal` |
| `string` | `nvarchar` |
| `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan`, `DateTimeOffset` | `datetime2`, `date`, `time`, `time`, `datetimeoffset` |
| `Guid` | `uniqueidentifier` |
| `byte[]` | `varbinary` |
| Any other type | Its text, as `nvarchar` |

Nullable value types (`int?`) give nullable columns.

The factory is called for every query, with the `TdsQueryContext` of the request. Use it to return the data the user
is allowed to see, before the SQL statement adds its own filters. Here, `Customer` is a `record Customer(int Id,
string Name)`, and the user identifier comes from the claims set by the [authentication handler](04-Authentication.md):

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/ReadmeExamples.cs;region=TdsServer_QueryEngine;format=md-fenced-code-block;dedent=true -->
```csharp
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
        if (int.TryParse(context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var userId))
        {
            return customers.Where(customer => customer.Id == userId);
        }

        return customers;
    });
queryEngineOptions.StoredProcedures.Add(
    "GetCustomer",
    (TdsQueryContext context, int id) =>
    {
        var userIdClaim = context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, CultureInfo.InvariantCulture, out var userId))
        {
            return Array.Empty<Customer>().AsQueryable();
        }

        return customers.Where(customer => customer.Id == id && customer.Id == userId);
    });

app.MapTdsAuthenticationHandler((context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()));
app.MapTdsQueryEngine(queryEngineOptions);
```
<!-- endref -->

Table names are matched without their schema and database, ignoring case: `customers`, `dbo.customers` and
`otherdb.dbo.Customers` all read the `customers` query root. A table that is not registered returns an error.

The statements the engine can translate are listed in [Supported SQL](08-SupportedSql.md).

## Stored procedures

`StoredProcedures` maps procedure names (case-insensitive) to delegates. The parameters of the delegate receive the
parameters of the call, matched by name without the `@` prefix and converted to the type of the delegate parameter:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsQueryEngineTests.cs;region=TdsServer_QueryEngineStoredProcedure;format=md-fenced-code-block;dedent=true -->
```csharp
var products = new[]
{
    new { Id = 1, Name = "Keyboard" },
    new { Id = 2, Name = "Mouse" },
    new { Id = 3, Name = "Webcam" },
};

queryEngineOptions.StoredProcedures.Add(
    "SearchProducts",
    async (TdsQueryContext context, string name, int top = 10, CancellationToken cancellationToken = default) =>
    {
        await Task.Yield(); // call an async API with cancellationToken here
        return products.Where(product => product.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(top).ToArray();
    });
```
<!-- endref -->

- A parameter with a default value is optional; a missing parameter without one returns an error.
- A `TdsQueryContext` parameter receives the request (and its `UserContext`), and a `CancellationToken` parameter the
  token of the request. They are not parameters of the procedure.
- The delegate can be asynchronous: the engine awaits a `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`.

What the delegate returns becomes the result:

| Returned value | Result |
| --- | --- |
| A `TdsQueryResult` | Sent as is |
| A collection or an `IQueryable` of objects | One row per item, one column per public property or field |
| A collection of scalar values (`int`, `string`, …) | One column named `Value`, one row per item |
| A scalar value, or `null` | One column named `Value`, one row |

A returned `IQueryable` is enumerated synchronously: `MaterializeAsync` only runs the queries translated from SQL text.
For an asynchronous source, make the delegate asynchronous and return the materialized rows.

The client calls the procedure as a SQL Server stored procedure. See [Connecting clients](10-ConnectingClients.md).

A call to `sp_executesql`, which is how clients send SQL text with parameters, is not a stored procedure: the engine
runs its SQL text as a query.

## Authorization

`IsAuthorized` is called before a stored procedure or a query root is used, with the request, the kind of resource and
its name. Return `false` to deny access:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/ReadmeExamples.cs;region=TdsServer_Authorization;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

A denied access returns the SQL Server permission error 229 (class 14), for instance "The SELECT permission was denied
on the object 'admin_customers'." Replace `NotAuthorizedErrorFactory` to return another `TdsQueryError`. The name is
the table name without its schema or database, so a qualified name cannot reach a denied query root.

## Run the queries with your provider

By default, the engine runs the translated query by enumerating the `IQueryable` synchronously. Replace
`MaterializeAsync` to run it with the asynchronous API of your provider, for instance `ToListAsync` with Entity
Framework Core. It receives the translated `IQueryable` and the `CancellationToken` of the request, and returns the
rows as an `IReadOnlyList<object?>`.

The translated query uses standard LINQ operators, but whether a provider can run it depends on the provider:
functions such as `string.ToUpperInvariant` are not supported by every provider. Map the SQL functions to methods your
provider supports with `AddScalarFunction`:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsQueryEngineTests.cs;region=TdsServer_QueryEngineScalarFunction;format=md-fenced-code-block;dedent=true -->
```csharp
queryEngineOptions.AddScalarFunction(
    "UPPER",
    arguments => Expression.Call(arguments[0], typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!));
```
<!-- endref -->

The function receives the arguments of the SQL function as LINQ expressions and returns the expression of the call.
`ScalarFunctions` lists the mappings, keyed by SQL function name. A replaced mapping also replaces the handling of
`NULL` arguments: `UPPER(NULL)` returns `NULL` with the built-in mapping, and calls `ToUpper` on `null` with this one.

## Errors

The engine returns SQL Server-style errors instead of throwing:

| Number | Cause |
| --- | --- |
| 229 | The authorization callback denied access. |
| 8134 | Division by zero. |
| 8115 | Arithmetic overflow. |
| 50004 | Any other error: a syntax error, an unsupported statement, an unknown table, column or procedure, a missing parameter, incomplete parameters. |

The message of the error describes the problem. Arithmetic and conversion exceptions thrown while the rows of a SQL
query are read become these errors too. Other exceptions thrown by your query roots, delegates or provider become the
error 50002 of the server. See [Returning results](06-ReturningResults.md#errors-sent-by-the-server).
