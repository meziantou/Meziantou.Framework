---
title: Limitations
description: What the server does not support, so you can plan around it.
---

The library implements the protocol of SQL Server, not a database. This page lists what it does not do.

## General

- **Needs the ASP.NET Core shared framework.**
- **The query engine needs reflection**: the package is trimmable and compatible with Native AOT, but the query
  engine is not. See [Trimming and Native AOT](03-HostingTheServer.md#trimming-and-native-aot).
- **No database**: the server stores nothing and runs no SQL by itself. Your query handler, or the
  [query engine](07-QueryEngine.md), answers every request.

## Protocol

- **SQL batches, RPC calls, bulk loads and local transactions only.** Other request types return the error 50001
  "Unsupported packet type".
- **No distributed transactions** (`System.Transactions`, MSDTC): their requests return the error 50001. See
  [Transactions](05-HandlingQueries.md#transactions).
- **Bulk loads are received batch by batch**: a batch of `SqlBulkCopy` cannot be larger than `MaxMessageSize`, and
  decimal columns are declared `decimal(38, 10)`. See [Bulk loads](11-BulkLoads.md).
- **Text columns use one collation.** [Variant](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.Variant), [UserDefined](cref:Meziantou.Framework.Tds.Handler.TdsColumnType.UserDefined) and `Table` columns are sent as `nvarchar` text.

## Authentication and encryption

- **No Microsoft Entra ID authentication.** SQL logins and integrated Windows authentication (NTLM, Kerberos, through
  an SSPI exchange run by the authentication handler) are supported. See [Authentication](04-Authentication.md).
- **TLS 1.2 when TLS is negotiated in the TDS PRELOGIN exchange** (`Encrypt=True` or `Optional`). Use
  `Encrypt=Strict` for TLS 1.3. See [Encryption](09-Encryption.md).
- **No encryption by default**: without a certificate, passwords and data travel in clear text.

## Query engine

- `SELECT` statements only: no recursive common table expressions, no `INSERT`, `UPDATE` or `DELETE`. See
  [Supported SQL](08-SupportedSql.md).
- `LIKE` compares characters ordinally, so it is case-sensitive.
- The XML methods take XPath 1.0, not XQuery.
- A stored procedure that returns an `IQueryable` is enumerated synchronously.
