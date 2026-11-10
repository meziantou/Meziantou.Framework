---
title: The query engine
description: Answer SELECT statements from IQueryable sources, apply INSERT, UPDATE and DELETE statements with your handlers, and answer stored procedure calls from .NET delegates, with per-user filtering and authorization.
---

The query engine is a ready-made query handler. It answers two kinds of requests:

- **SQL text** (`SELECT` statements, with or without parameters): it translates the statement to a LINQ query over the
  `IQueryable` sources you register as tables, called query roots, and returns the rows. `INSERT`, `UPDATE` and
  `DELETE` statements call the handlers you register for the query root they target. See [Modify data](#modify-data).
- **Stored procedure calls**: it calls the .NET delegate registered under the name of the procedure, with the
  parameters of the call, and returns what the delegate returns.

## Set up the query engine

Configure a [TdsQueryEngineOptions](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions), then use it as the query handler:

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

The factory is called for every query, with the [TdsQueryContext](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext) of the request. Use it to return the data the user
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

## Modify data

A query root is read-only unless you register the handlers of the statements that change it. Pass a
[TdsQueryRootWriteHandlers\<T\>](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryRootWriteHandlers{T}) to [AddQueryRoot](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.AddQueryRoot). The rows are instances of a class with settable
properties:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsQueryEngineTests.cs;region=TdsServer_QueryEngineProduct;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public decimal Price { get; set; }

    public int? Stock { get; set; }
}
```
<!-- endref -->

The handlers save the changes to a list:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsQueryEngineTests.cs;region=TdsServer_QueryEngineDataModification;format=md-fenced-code-block;dedent=true -->
```csharp
var products = new List<Product>
{
    new() { Id = 1, Name = "Keyboard", Price = 49.90m },
};
var productsLock = new Lock();

var queryEngineOptions = new TdsQueryEngineOptions();
queryEngineOptions.AddQueryRoot(
    "products",
    context => products.AsQueryable(),
    new TdsQueryRootWriteHandlers<Product>
    {
        Insert = (context, rows, cancellationToken) =>
        {
            lock (productsLock)
            {
                products.AddRange(rows);
            }

            return ValueTask.CompletedTask;
        },

        // The engine has already assigned the new values to the rows, so there is nothing to save
        Update = (context, rows, cancellationToken) => ValueTask.CompletedTask,
        Delete = (context, rows, cancellationToken) =>
        {
            lock (productsLock)
            {
                products.RemoveAll(rows.Contains);
            }

            return ValueTask.CompletedTask;
        },
    });

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
command.CommandText = """
    INSERT INTO products (Id, Name, Price) VALUES (2, 'Mouse', 19.50);
    UPDATE products SET Price = Price * 0.9 WHERE Price > 40;
    """;
var recordsAffected = await command.ExecuteNonQueryAsync();

Assert.Equal(2, recordsAffected);
Assert.Equal(44.910m, products[0].Price);
```
<!-- endref -->

The engine prepares the rows, and your handlers save them:

| Statement | What the handler receives |
| --- | --- |
| `INSERT` | New rows, created with the public parameterless constructor of `T`. The columns of the statement are set; the other properties keep the values of the constructor. |
| `UPDATE` | The rows of the query root that match the statement, with their new values already assigned. |
| `DELETE` | The rows of the query root that match the statement. |

- The engine computes and converts the new values of every row before it changes the first row, so an invalid value
  changes nothing. A handler is called once per statement, only when the statement changes at least one row.
- The rows of `UPDATE` and `DELETE` are read with the query root, filtered by the `WHERE` clause, and
  materialized with [MaterializeAsync](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.MaterializeAsync). With Entity Framework Core, the rows are tracked entities: the
  `Update` handler calls `SaveChangesAsync`, and the `Delete` handler calls `RemoveRange`, then `SaveChangesAsync`.
- A property of a non-nullable value type, or of a reference type annotated as non-nullable (`string`, not `string?`),
  is a `NOT NULL` column: setting it to `NULL` returns the error 515. Read-only properties cannot be set, and are not
  part of an `INSERT` without a column list.
- `SqlCommand.ExecuteNonQuery` returns the number of rows the statements changed. The statements of a request run in
  order, so a `SELECT` after an `INSERT` reads the new rows when the query root returns them.
- A handler is `null` by default: the statement returns an error, as it does on a query root registered without
  handlers.

The handlers receive the [TdsQueryContext](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext) of the request, which gives the user and the
[TransactionId](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.TransactionId) of the transaction the request runs in. The engine does not undo the changes of a rolled back
transaction: track them by transaction if you need to. See [Transactions](05-HandlingQueries.md#transactions).

## Stored procedures

[StoredProcedures](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.StoredProcedures) maps procedure names (case-insensitive) to delegates. The parameters of the delegate receive the
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
- A [TdsQueryContext](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext) parameter receives the request (and its [UserContext](cref:Meziantou.Framework.Tds.Handler.TdsQueryContext.UserContext)), and a `CancellationToken` parameter the
  token of the request. They are not parameters of the procedure.
- The delegate can be asynchronous: the engine awaits a `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`.

What the delegate returns becomes the result:

| Returned value | Result |
| --- | --- |
| A [TdsQueryResult](cref:Meziantou.Framework.Tds.Handler.TdsQueryResult) | Sent as is |
| A collection or an `IQueryable` of objects | One row per item, one column per public property or field |
| A collection of scalar values (`int`, `string`, …) | One column named `Value`, one row per item |
| A scalar value, or `null` | One column named `Value`, one row |

A returned `IQueryable` is enumerated synchronously: [MaterializeAsync](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.MaterializeAsync) only runs the queries translated from SQL text.
For an asynchronous source, make the delegate asynchronous and return the materialized rows.

The client calls the procedure as a SQL Server stored procedure. See [Connecting clients](10-ConnectingClients.md).

A call to `sp_executesql`, which is how clients send SQL text with parameters, is not a stored procedure: the engine
runs its SQL text as a query.

## Authorization

[IsAuthorized](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.IsAuthorized) is called before a stored procedure or a query root is used, with the request, the kind of resource and
its name. The kind of a query root is [QueryRoot](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineResourceKind.QueryRoot) when a statement reads it, and
[QueryRootInsert](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineResourceKind.QueryRootInsert), [QueryRootUpdate](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineResourceKind.QueryRootUpdate) or [QueryRootDelete](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineResourceKind.QueryRootDelete) when it is the target of an `INSERT`,
`UPDATE` or `DELETE` statement. Return `false` to deny access:

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
on the object 'admin_customers'.", or "The DELETE permission …" for a `DELETE` statement. Replace [NotAuthorizedErrorFactory](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.NotAuthorizedErrorFactory) to return another [TdsQueryError](cref:Meziantou.Framework.Tds.Handler.TdsQueryError). The name is
the table name without its schema or database, so a qualified name cannot reach a denied query root.

## Run the queries with your provider

By default, the engine runs the translated query by enumerating the `IQueryable` synchronously. Replace
[MaterializeAsync](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.MaterializeAsync) to run it with the asynchronous API of your provider, for instance `ToListAsync` with Entity
Framework Core. It receives the translated `IQueryable` and the `CancellationToken` of the request, and returns the
rows as an `IReadOnlyList<object?>`.

The translated query uses standard LINQ operators, but whether a provider can run it depends on the provider:
functions such as `string.ToUpperInvariant` are not supported by every provider. Map the SQL functions to methods your
provider supports with [AddScalarFunction](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.AddScalarFunction):

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsQueryEngineTests.cs;region=TdsServer_QueryEngineScalarFunction;format=md-fenced-code-block;dedent=true -->
```csharp
queryEngineOptions.AddScalarFunction(
    "UPPER",
    arguments => Expression.Call(arguments[0], typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!));
```
<!-- endref -->

The function receives the arguments of the SQL function as LINQ expressions and returns the expression of the call.
[ScalarFunctions](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions.ScalarFunctions) lists the mappings, keyed by SQL function name. A replaced mapping also replaces the handling of
`NULL` arguments: `UPPER(NULL)` returns `NULL` with the built-in mapping, and calls `ToUpper` on `null` with this one.

## Errors

The engine returns SQL Server-style errors instead of throwing:

| Number | Cause |
| --- | --- |
| 229 | The authorization callback denied access. |
| 515 | `NULL` assigned to a column that does not allow it, by an `INSERT` or an `UPDATE`. |
| 530 | A recursive common table expression ran more recursion levels than `MAXRECURSION` allows. |
| 8134 | Division by zero. |
| 8115 | Arithmetic overflow. |
| 50004 | Any other error: a syntax error, an unsupported statement, an unknown table, column or procedure, a missing parameter, incomplete parameters. |

Invalid statements return the number SQL Server uses when the engine models the error, for instance 207 for an unknown
column in an `INSERT` or 252 for a recursive common table expression without `UNION ALL`. The message of the error
describes the problem. Arithmetic and conversion exceptions thrown while the rows of a SQL
query are read become these errors too. Other exceptions thrown by your query roots, delegates or provider become the
error 50002 of the server. See [Returning results](06-ReturningResults.md#errors-sent-by-the-server).
