---
title: Introduction
description: A server that speaks the PostgreSQL wire protocol, so PostgreSQL clients can query your .NET code.
---

`Meziantou.Framework.PostgreSqlServer` is a server for the wire protocol of PostgreSQL (version 3.0). PostgreSQL clients
and drivers connect to it as they would connect to PostgreSQL, but your code decides who can log in and what each query
returns. There is no database behind it: the library handles the protocol, you provide the answers.

It is the PostgreSQL counterpart of [TdsServer](../TdsServer/01-Introduction.md), which does the same for the
protocol of SQL Server.

## What you can do with it

- **Replace PostgreSQL in tests**: start a server on a free port, return the rows your test needs, and check the SQL
  text and the parameters your application sends. See [Getting started](02-GettingStarted.md).
- **Expose your own data over the PostgreSQL protocol**, to tools that only know how to talk to a database: a query
  gateway, a proxy, or a virtual table over an API.
- **Control logins** with cleartext, MD5 or SCRAM-SHA-256 password authentication. See
  [Authentication](04-Authentication.md).
- **Answer simple and extended queries**, with typed parameters, several result sets, notices, PostgreSQL errors and
  cancellation. See [Handling queries](05-HandlingQueries.md) and [Returning results](06-ReturningResults.md).
- **Encrypt connections** with TLS, from a PFX file or a PEM certificate and key. See [Encryption](07-Encryption.md).

## How it works

A connection goes through two callbacks:

1. The **authentication handler** receives the login (user name, database, startup parameters) and checks the password
   with `ValidatePassword`. It accepts or rejects the connection.
2. The **query handler** receives each request of the connection: the SQL text, its kind (simple query, extended query,
   or `Describe`) and its parameters. It returns result sets, a command tag, or an error.

The server does not parse or run the SQL: matching the text, or parsing it with a SQL parser, is up to the handler.

The server runs on its own with the `PostgreSqlServer` class, or inside an ASP.NET Core application, on Kestrel. See
[Hosting the server](03-HostingTheServer.md).

## Namespaces

| Namespace | Contains |
| --- | --- |
| `Meziantou.Framework.PostgreSql` | The standalone `PostgreSqlServer` and the options (`PostgreSqlServerOptions`) |
| `Meziantou.Framework.PostgreSql.Handler` | The callbacks, their contexts and results (`PostgreSqlResultSet`, `PostgreSqlColumn`, ...) |
| `Meziantou.Framework.PostgreSql.Hosting` | The ASP.NET Core integration (`AddPostgreSqlServer`, `MapPostgreSqlHandlers`, ...) |

## Next steps

- [Getting started](02-GettingStarted.md): install the package, start a server and query it.
- [Connecting clients](08-ConnectingClients.md): the connection strings that work with the server.
- [Limitations](09-Limitations.md): what the server does not do.
