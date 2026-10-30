---
title: Introduction
description: A server that speaks the TDS protocol of SQL Server, so SQL Server clients can query your .NET code.
---

`Meziantou.Framework.TdsServer` is a server for TDS (Tabular Data Stream), the network protocol of SQL Server. Clients
such as `Microsoft.Data.SqlClient` connect to it as they would connect to SQL Server, but your code decides who can log
in and what each query returns. There is no database behind it: the library handles the protocol, you provide the
answers.

## What you can do with it

- **Replace SQL Server in tests**: start a server on a free port, return the rows your test needs, and check the SQL
  text and the parameters your application sends. See [Getting started](02-GettingStarted.md).
- **Expose your own data over the SQL Server protocol**: tools and applications that only know how to talk to SQL
  Server can query in-memory collections or any `IQueryable` source. See [The query engine](07-QueryEngine.md).
- **Control logins**: check the user name and password of every connection, and pass the identity of the user to the
  query handlers. See [Authentication](04-Authentication.md).
- **Answer queries and stored procedure calls**: read the SQL text, the procedure name and the typed parameters, and
  return result sets, informational messages or SQL Server-style errors. See [Handling queries](05-HandlingQueries.md)
  and [Returning results](06-ReturningResults.md).
- **Encrypt connections** with TLS, from a PFX file or a PEM certificate and key. See [Encryption](09-Encryption.md).

## How it works

A connection goes through two callbacks:

1. The **authentication handler** receives the login (user name, password, database, application name) and accepts or
   rejects it. When it accepts, it can attach a `ClaimsPrincipal` to the connection.
2. The **query handler** receives each request of the connection: a SQL batch, or a remote procedure call (RPC), which
   is how clients send stored procedure calls and parameterized queries. It returns the result sets or an error.

You write the query handler yourself, or use the built-in **query engine**, which runs stored procedures mapped to
.NET delegates and translates a subset of `SELECT` statements to LINQ queries over your `IQueryable` sources.

The server runs either inside an ASP.NET Core application, on Kestrel, or on its own with the [TdsServer](cref:Meziantou.Framework.Tds.TdsServer) class. See
[Hosting the server](03-HostingTheServer.md).

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Tds](cref:Meziantou.Framework.Tds) | The standalone [TdsServer](cref:Meziantou.Framework.Tds.TdsServer) and the server options ([TdsServerOptions](cref:Meziantou.Framework.Tds.TdsServerOptions)) |
| [Meziantou.Framework.Tds.Handler](cref:Meziantou.Framework.Tds.Handler) | The authentication and query callbacks, their contexts and results ([TdsResultSet](cref:Meziantou.Framework.Tds.Handler.TdsResultSet), [TdsColumn](cref:Meziantou.Framework.Tds.Handler.TdsColumn), …) |
| [Meziantou.Framework.Tds.Hosting](cref:Meziantou.Framework.Tds.Hosting) | The ASP.NET Core integration ([AddTdsServer](cref:Meziantou.Framework.Tds.Hosting.TdsServerBuilderExtensions.AddTdsServer), [MapTdsHandlers](cref:Meziantou.Framework.Tds.Hosting.TdsEndpointRouteBuilderExtensions.MapTdsHandlers), …) |
| [Meziantou.Framework.Tds.QueryEngine](cref:Meziantou.Framework.Tds.QueryEngine) | The built-in query engine ([TdsQueryEngineOptions](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngineOptions), [TdsQueryEngine](cref:Meziantou.Framework.Tds.QueryEngine.TdsQueryEngine)) |

## Next steps

- [Getting started](02-GettingStarted.md): install the package, start a server and query it.
- [Connecting clients](10-ConnectingClients.md): the connection strings that work with the server.
- [Limitations](11-Limitations.md): what the server does not do.
