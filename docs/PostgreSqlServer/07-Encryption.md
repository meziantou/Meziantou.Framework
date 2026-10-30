---
title: Encryption
description: Configure TLS from a PFX file or a PEM certificate and key, require it, and connect with TLS from the client.
---

Without a certificate, the server answers the TLS request of the client (`SSLRequest`) with "not supported", and
clients that allow it, such as Npgsql with its default `SSL Mode=Prefer`, log in without encryption: the password
travels in clear with the cleartext method, as do the queries and the results with every method. Configure TLS for any
server that is not on the loopback address.

## Configure a certificate

Set a PFX file and its password, or a PEM certificate and its private key:

<!-- ref:../../samples/Meziantou.Framework.PostgreSqlServer.Samples/ReadmeExamples.cs;region=PostgreSqlServer_Tls;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new PostgreSqlServerOptions
{
    RequireEncryption = true,
    TlsPfxPath = "server.pfx",
    TlsPfxPassword = "…",
};
```
<!-- endref -->

| Option | Use |
| --- | --- |
| [TlsPfxPath](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.TlsPfxPath), [TlsPfxPassword](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.TlsPfxPassword) | A PFX (PKCS#12) file with the certificate and its private key |
| [TlsPemCertificatePath](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.TlsPemCertificatePath), [TlsPemPrivateKeyPath](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.TlsPemPrivateKeyPath) | A PEM certificate and a PEM private key, both required |

- Set one of the two forms: setting both throws `InvalidOperationException`, as do a password without a PFX file, a
  PEM certificate without its key, and a certificate without a private key. A missing file throws
  `FileNotFoundException`.
- The certificate is loaded once, by [StartAsync](cref:Meziantou.Framework.PostgreSql.PostgreSqlServer.StartAsync) for a standalone server and by [AddPostgreSqlServer](cref:Meziantou.Framework.PostgreSql.Hosting.PostgreSqlServerBuilderExtensions.AddPostgreSqlServer) in ASP.NET Core,
  so these errors appear at startup. Restart the server to use a renewed certificate.
- With a certificate, the server accepts the TLS request of the client, then reads the login over TLS. The TLS versions
  are the defaults of the operating system.

## Require encryption

[RequireEncryption](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.RequireEncryption) rejects the clients that do not use TLS, with the fatal error `28000` "TLS is required":

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_RequireTls;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new PostgreSqlServerOptions
{
    TlsPfxPath = pfxPath,
    TlsPfxPassword = pfxPassword,
    RequireEncryption = true, // reject the clients that do not upgrade to TLS
};
options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);
```
<!-- endref -->

[RequireEncryption](cref:Meziantou.Framework.PostgreSql.PostgreSqlServerOptions.RequireEncryption) without a certificate throws `InvalidOperationException` at startup, rather than accepting
plaintext connections.

## Connect with TLS

Set `SSL Mode=Require` (or `VerifyFull` to check the certificate against the host name) in the connection string:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_TlsClient;format=md-fenced-code-block;dedent=true -->
```csharp
// Trust Server Certificate accepts a self-signed certificate: use it in tests only
await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=Password123!;SSL Mode=Require;Trust Server Certificate=true;Server Compatibility Mode=NoTypeLoading");
await connection.OpenAsync();
```
<!-- endref -->

The server negotiates TLS the PostgreSQL way, after an `SSLRequest`. Direct TLS negotiation (`sslnegotiation=direct`)
is not supported, nor are client certificates.
