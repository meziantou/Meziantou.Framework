---
title: Limitations
description: What the server does not support, so you can plan around it.
---

The library implements the wire protocol of PostgreSQL, not a database. This page lists what it does not do.

## General

- **Needs the ASP.NET Core shared framework.**
- **Not trimmable**: the package is not marked as compatible with trimming or Native AOT.
- **No database**: the server stores nothing and runs no SQL. Your query handler answers every request, including the
  queries tools send to the system catalog (`pg_catalog`, `information_schema`).

## Protocol

- **Protocol version 3.0 only.** Other versions, including 3.2, are rejected.
- **Simple and extended queries only.** `COPY`, the function call protocol, replication, and `LISTEN`/`NOTIFY`
  notifications are not supported. An unsupported message gets the error `08P01`, and the server skips the following
  messages until the client sends `Sync`.
- **One request at a time per connection.**
- **No cursors.** An `Execute` with a row limit returns the first rows, then ends the portal: a second `Execute` on the
  same portal runs the handler again.
- **UTF-8 only.** The server always reports `UTF8` as the client and server encoding, and ignores `client_encoding`.
- **Fixed session parameters.** The server sends `server_version`, `server_encoding`, `client_encoding`, `DateStyle`
  (`ISO, MDY`), `integer_datetimes` and `standard_conforming_strings` at login, and no other parameter, such as
  `TimeZone`. `SET` commands reach the handler and change nothing.
- **A value that does not match its column type closes the connection.** See
  [Column types](06-ReturningResults.md#column-types).
- **Errors of `Describe` are not reported.** See [Answer Describe](05-HandlingQueries.md#answer-describe).

## Authentication and encryption

- **Passwords only**: cleartext, MD5 and SCRAM-SHA-256. No Kerberos (GSSAPI), SSPI, certificate or LDAP
  authentication, and no SCRAM channel binding (SCRAM-SHA-256-PLUS).
- **The password must be known in clear** to call `ValidatePassword`: stored PostgreSQL password hashes cannot be used.
- **No encryption by default**: without a certificate, passwords (with the cleartext method) and data travel in clear.
  See [Encryption](07-Encryption.md).
- **No direct TLS negotiation**, no GSSAPI encryption, no client certificates.

## Hosting

- **A standalone server cannot be restarted** after `StopAsync`: create a new one.
- **`MaxConcurrentConnections` only applies to the standalone server**; in ASP.NET Core, use the limits of Kestrel.
