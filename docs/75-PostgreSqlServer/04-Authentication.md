---
title: Authentication
description: Choose the password method, check the user name, the password and the database, and reject logins with a PostgreSQL error.
---

The authentication handler is called once per connection, after the client sent its startup message and its password.
It returns `PostgreSqlAuthenticationResult.Success()` or `PostgreSqlAuthenticationResult.Fail(message)`:

<!-- ref:../../tests/Meziantou.Framework.PostgreSqlServer.Tests/PostgreSqlServerProtocolTests.cs;region=PostgreSqlServer_Authentication;format=md-fenced-code-block;dedent=true -->
```csharp
var users = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["app"] = "Password123!",
    ["reporting"] = "Reporting456!",
};

PostgreSqlAuthenticationDelegate authenticate = (context, cancellationToken) =>
{
    if (context.UserName is null || !users.TryGetValue(context.UserName, out var password) || !context.ValidatePassword(password))
    {
        return ValueTask.FromResult(PostgreSqlAuthenticationResult.Fail($"password authentication failed for user \"{context.UserName}\""));
    }

    if (context.Database is not "sales")
    {
        // 3D000: invalid_catalog_name
        return ValueTask.FromResult(PostgreSqlAuthenticationResult.Fail($"database \"{context.Database}\" does not exist", "3D000"));
    }

    // Every startup parameter the client sent, such as application_name
    _ = context.StartupParameters.TryGetValue("application_name", out var applicationName);
    return ValueTask.FromResult(PostgreSqlAuthenticationResult.Success());
};
```
<!-- endref -->

The context has:

| Member | Contains |
| --- | --- |
| `UserName` | The `user` startup parameter |
| `Database` | The `database` startup parameter, or `null` |
| `StartupParameters` | Every startup parameter the client sent, such as `application_name` or `client_encoding` |
| `RemoteEndPoint` | The address and port of the client |
| `Method` | The authentication method of the server |
| `ValidatePassword(password)` | Whether the client knows this password |

## Choose the method

`PostgreSqlServerOptions.AuthenticationMethod` sets the method the server requests:

| Method | What the client sends | Notes |
| --- | --- | --- |
| `ScramSha256` (default) | A proof that it knows the password | The password never travels. `ValidatePassword` is required: it also computes the proof the server sends back. |
| `Md5Password` | An MD5 hash of the password, the user name and a random salt | `Md5PasswordResponse` holds the hash. MD5 is weak: prefer SCRAM. |
| `ClearTextPassword` | The password itself | `Password` holds it. Use it over TLS only. |

`ValidatePassword` works with every method, so the handler does not depend on it. It needs the password in clear: the
server cannot check a password against a stored PostgreSQL hash. For a cleartext login, it compares the passwords in
constant time.

With SCRAM-SHA-256, returning `Success()` without a successful `ValidatePassword` call is a mistake of the handler: the
server logs an error and the client gets the error `XX000` "The authentication handler must call ValidatePassword when
SCRAM-SHA-256 is used". The channel-binding variant, SCRAM-SHA-256-PLUS, is not supported; clients fall back to
SCRAM-SHA-256.

## Reject a login

`Fail(message, errorCode)` sends a fatal error to the client, then closes the connection. `errorCode` is the SQLSTATE
the client sees, `28P01` (invalid password) by default. Other common codes are `28000` (invalid authorization) and
`3D000` (unknown database). Clients report the message as it is, so do not reveal which part of the login was wrong
when that matters.

When the handler throws, the server logs the exception and the client gets the fatal error `XX000` "Unhandled
authentication handler exception"; the exception message is not sent.

## Timeouts

The handler runs within `HandshakeTimeout` (30 seconds by default), which starts when the client connects: its
`CancellationToken` is canceled when it expires, and the connection is closed. Pass the token to the asynchronous
calls of the handler, such as a database lookup.
