---
title: Limitations
description: What the server does not support, so you can plan around it.
---

The library implements the protocol of SQL Server, not a database. This page lists what it does not do.

## General

- **Needs the ASP.NET Core shared framework.**
- **Not trimmable**: the package is not marked as compatible with trimming or Native AOT.
- **No database**: the server stores nothing and runs no SQL by itself. Your query handler, or the
  [query engine](07-QueryEngine.md), answers every request.

## Protocol

- **SQL batches and RPC calls only.** Transactions (`BeginTransaction`) and bulk loads (`SqlBulkCopy`) are not
  supported: their requests, like any other request type, return the error 50001 "Unsupported packet type".
- **No Multiple Active Result Sets (MARS)**: a connection with `MultipleActiveResultSets=True` fails.
- **One request at a time per connection.**
- **No output parameters, return values or row counts**: `ExecuteNonQuery` returns -1.
- **Errors close the `SqlClient` connection**: the server sends errors with the "server error" status, which
  `SqlClient` treats as severe. See [Return an error](06-ReturningResults.md#return-an-error).
- **Text columns use one collation.** `Variant`, `UserDefined` and `Table` columns are sent as `nvarchar` text.
- **No SQL Server Browser**: clients connect with `host,port`, not with an instance name.

## Authentication and encryption

- **SQL logins only.** There is no SSPI exchange, so integrated Windows authentication (NTLM, Kerberos) and Microsoft
  Entra ID authentication are not supported. See [Authentication](04-Authentication.md).
- **TLS 1.2 only**, negotiated in the TDS PRELOGIN exchange. `Encrypt=Strict` (TDS 8.0) is not supported. See
  [Encryption](09-Encryption.md).
- **No encryption by default**: without a certificate, passwords and data travel in clear text.

## Query engine

- One `SELECT` statement per request: no `LIKE`, no recursive common table expressions, no scalar subqueries in the
  selected columns, no `INSERT`, `UPDATE` or `DELETE`, no multiple statements or result sets. See
  [Supported SQL](08-SupportedSql.md).
- `JOIN … ON` matches `NULL` keys with each other.
- The XML methods take XPath 1.0, not XQuery.
- A stored procedure that returns an `IQueryable` is enumerated synchronously.
