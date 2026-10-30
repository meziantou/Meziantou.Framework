---
title: Encryption
description: Configure TLS with a PFX file or a PEM certificate, require encryption, and know what clients do without it.
---

Without a TLS certificate, the server does not encrypt connections. The client then sends its password with the
obfuscation of the TDS protocol only, which anyone on the network path can reverse, and every query and result travels
in clear text. This is what SQL Server does too, and it is fine for a server that only listens on the loopback address,
such as a test server. Configure a certificate for anything else.

When no certificate is configured, the server logs a warning at startup.

## Configure a certificate

TLS is negotiated on the same endpoint as unencrypted connections, as SQL Server does: there is no separate port.
Configure either a PFX file:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/ReadmeExamples.cs;region=TdsServer_TlsPfx;format=md-fenced-code-block;dedent=true -->
```csharp
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
    options.TlsPfxPath = "certificates/server.pfx";
    options.TlsPfxPassword = "Password123!";
});
```
<!-- endref -->

or a PEM certificate and its private key:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/ReadmeExamples.cs;region=TdsServer_TlsPem;format=md-fenced-code-block;dedent=true -->
```csharp
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 14330, bindAddress: IPAddress.Loopback);
    options.TlsPemCertificatePath = "certificates/server.crt.pem";
    options.TlsPemPrivateKeyPath = "certificates/server.key.pem";
});
```
<!-- endref -->

- The certificate must contain its private key.
- Relative paths are relative to the current directory of the process.
- Configure one source only: PFX and PEM options together are an error, and so is [TlsPfxPassword](cref:Meziantou.Framework.Tds.TdsServerOptions.TlsPfxPassword) without
  [TlsPfxPath](cref:Meziantou.Framework.Tds.TdsServerOptions.TlsPfxPath) or one PEM path without the other.
- The certificate is loaded when the server starts, so a missing file or a wrong password fails at startup with an
  `InvalidOperationException`, a `FileNotFoundException` or a `CryptographicException`.
- Keep the PFX password out of the source code: read it from the configuration or a secret store.

The server uses TLS 1.2, the version `Microsoft.Data.SqlClient` negotiates with this kind of server.

## Require encryption

With a certificate, the server still accepts clients that do not encrypt. Set [RequireEncryption](cref:Meziantou.Framework.Tds.TdsServerOptions.RequireEncryption) to reject them:

<!-- ref:../../samples/Meziantou.Framework.TdsServer.Samples/Examples.cs;region=TdsServer_RequireEncryption;format=md-fenced-code-block;dedent=true -->
```csharp
builder.AddTdsServer(options =>
{
    options.AddTcpListener(port: 1433, bindAddress: IPAddress.Any);
    options.TlsPemCertificatePath = "certificates/server.crt.pem";
    options.TlsPemPrivateKeyPath = "certificates/server.key.pem";
    options.RequireEncryption = true; // reject clients that do not encrypt
});
```
<!-- endref -->

[RequireEncryption](cref:Meziantou.Framework.Tds.TdsServerOptions.RequireEncryption) needs a certificate: without one, the server fails to start.

## What clients do

The `Encrypt` keyword of the client's connection string and the configuration of the server decide whether a
connection is encrypted:

| Client | No certificate | Certificate | Certificate and [RequireEncryption](cref:Meziantou.Framework.Tds.TdsServerOptions.RequireEncryption) |
| --- | --- | --- | --- |
| `Encrypt=True` (or `Mandatory`) | Fails | Encrypted | Encrypted |
| `Encrypt=False` (or `Optional`) | Not encrypted | Only the login is encrypted | Encrypted |
| `Encrypt=Strict` | Fails | Fails | Fails |

- `Encrypt=True` is the default of `Microsoft.Data.SqlClient`, so clients need `Encrypt=Optional` (or `False`) to
  connect to a server without a certificate.
- When the client does not ask for encryption, the server still encrypts the login packet, which holds the password,
  then continues without TLS, as SQL Server does.
- `Encrypt=Strict` (TDS 8.0), where TLS starts before any TDS message, is not supported.

The client also validates the certificate. A self-signed certificate is rejected unless the client trusts it, or sets
`TrustServerCertificate=True`, which disables the validation and should be limited to tests. The name in the
certificate must match the server name of the connection string, or the `HostNameInCertificate` of the client.
