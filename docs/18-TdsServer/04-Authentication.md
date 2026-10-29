---
title: Authentication
description: Accept or reject logins, attach the identity of the user to the connection, and return SQL Server-style login errors.
---

Every connection starts with a login. The server calls the authentication handler with what the client sent, and the
handler accepts or rejects the connection:

<!-- ref:../../tests/Meziantou.Framework.TdsServer.Tests/TdsServerProtocolTests.cs;region=TdsServer_Authentication;format=md-fenced-code-block;dedent=true -->
```csharp
TdsAuthenticationDelegate authenticate = (context, cancellationToken) =>
{
    if (context.UserName != "app" || context.Password != "Password123!")
    {
        return ValueTask.FromResult(TdsAuthenticationResult.Fail($"Login failed for user '{context.UserName}'."));
    }

    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "app"), new Claim(ClaimTypes.Role, "reader")], authenticationType: "password");
    return ValueTask.FromResult(TdsAuthenticationResult.Success(database: "Sales", userContext: new ClaimsPrincipal(identity)));
};

TdsQueryDelegate query = (context, cancellationToken) =>
{
    var user = context.UserContext; // the ClaimsPrincipal returned by the authentication handler

    var resultSet = new TdsResultSet();
    resultSet.Columns.Add(new TdsColumn("UserName", TdsColumnType.NVarChar));
    resultSet.Columns.Add(new TdsColumn("IsReader", TdsColumnType.Boolean));
    resultSet.Rows.Add([user?.Identity?.Name, user?.IsInRole("reader") ?? false]);

    var result = new TdsQueryResult();
    result.ResultSets.Add(resultSet);
    return ValueTask.FromResult(result);
};
```
<!-- endref -->

With these callbacks, a client that logs in as `app` gets `Sales` as its current database (`SqlConnection.Database`),
and its queries return `app` and `true`. A wrong password makes `SqlConnection.Open` throw a `SqlException` with the
number 18456 and the message `Login failed for user 'app'.`

## What the handler receives

`TdsAuthenticationContext` holds the login sent by the client:

| Property | Value |
| --- | --- |
| `UserName`, `Password` | The SQL login (`User ID` and `Password` in the connection string), or `null` when missing |
| `Database` | The requested database (`Initial Catalog` or `Database`), or `null` |
| `ApplicationName` | The `Application Name` of the connection string, or the default name of the client |
| `RemoteEndPoint` | The address and port of the client |
| `AuthenticationToken` | The SSPI data of the login packet, Base64-encoded, or `null` when the client sent none |

The server does not run an SSPI exchange: integrated Windows authentication and Microsoft Entra ID authentication are
not supported. `AuthenticationToken` only exposes the first SSPI message, for custom schemes.

Without TLS, the password travels with the obfuscation of the TDS protocol only, which anyone on the network can
reverse. See [Encryption](09-Encryption.md).

## Accept a login

`TdsAuthenticationResult.Success` accepts the connection. Both of its parameters are optional:

- `database`: the database reported to the client after the login.
- `userContext`: a `ClaimsPrincipal` that represents the user. The server attaches it to the connection, and every
  query of the connection gets it in `TdsQueryContext.UserContext`. The [query engine](07-QueryEngine.md) passes it to
  the query roots, the stored procedures and the authorization callback, so you can filter data per user.

## Reject a login

`TdsAuthenticationResult.Fail(message)` rejects the connection with a SQL Server-style error: the client throws a
`SqlException` with that message, and the server closes the connection. The error number defaults to 18456, the
number SQL Server uses for failed logins, the state to 1 and the class to 14; pass `errorNumber`, `errorState` and
`errorClass` to change them.

The handler runs for every connection, before its first query: keep it fast. An exception thrown by the handler
closes the connection without a login error, so the client only sees a broken connection: return `Fail` instead. Its
`CancellationToken` is canceled when the server stops.
